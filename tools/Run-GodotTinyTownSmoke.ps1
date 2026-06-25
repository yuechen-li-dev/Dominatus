[CmdletBinding()]
param(
    [string]$GodotPath,
    [UInt64]$SmokeFrames = 360,
    [switch]$LongRun,
    [UInt64]$LongRunSmokeFrames = 900,
    [string]$ArtifactsDir,
    [switch]$Headless,
    [switch]$CleanGodotCaches,
    [ValidateSet('FallbackShapes', 'StaticSprites', 'AnimatedSprites')]
    [string]$VisualMode = 'FallbackShapes',
    [string]$AtlasPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$sampleDir = Join-Path $repoRoot "samples\Dominatus.GodotTinyTown"
$sampleProject = Join-Path $sampleDir "Dominatus.GodotTinyTown.csproj"
$artifactsDir = if ($ArtifactsDir) { $ArtifactsDir } else { Join-Path $repoRoot "artifacts\godot-tinytown" }
$artifactsDir = [System.IO.Path]::GetFullPath($artifactsDir)
$logPath = Join-Path $artifactsDir "run.log"
$debugJsonPath = Join-Path $artifactsDir "tinytown-debug.json"
$screenshotPath = Join-Path $artifactsDir "tinytown-screenshot.png"
$targetSmokeFrames = if ($LongRun) { $LongRunSmokeFrames } else { $SmokeFrames }
$quitAfterFrames = [Math]::Max([int]($targetSmokeFrames * 4), 300)

function Resolve-GodotBinary {
    param([string]$CliPath)

    $candidates = @()
    if ($CliPath) { $candidates += $CliPath }
    if ($env:GODOT_BIN) { $candidates += $env:GODOT_BIN }
    $pathCommand = Get-Command godot -ErrorAction SilentlyContinue
    if ($pathCommand) { $candidates += $pathCommand.Source }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path $candidate)) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Unable to locate a Godot binary. Pass -GodotPath, set GODOT_BIN, or put 'godot' on PATH."
}

function Remove-RunArtifacts {
    if (Test-Path $artifactsDir) {
        Remove-Item $artifactsDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $artifactsDir | Out-Null
}

function Clear-GodotCaches {
    $cachePaths = @(
        (Join-Path $sampleDir ".godot\mono\temp"),
        (Join-Path $sampleDir ".godot\global_script_class_cache.cfg"),
        (Join-Path $sampleDir "bin"),
        (Join-Path $sampleDir "obj")
    )

    foreach ($path in $cachePaths) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force
        }
    }
}

function Add-LogLine {
    param([string]$Text)

    $Text | Out-File -FilePath $logPath -Append -Encoding utf8
}

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

$godotBin = Resolve-GodotBinary -CliPath $GodotPath
Remove-RunArtifacts

if ($CleanGodotCaches) {
    Clear-GodotCaches
}

$version = & $godotBin --version
Add-LogLine "Godot binary: $godotBin"
Add-LogLine "Godot version: $version"
Add-LogLine "Smoke frames: $targetSmokeFrames"
Add-LogLine ""
Add-LogLine "== dotnet build =="

& dotnet build $sampleProject 2>&1 | Tee-Object -FilePath $logPath -Append | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed. See $logPath"
}

Add-LogLine ""
Add-LogLine "== godot smoke run =="

$previousSmoke = $env:DOMINATUS_GODOT_SMOKE
$previousFrames = $env:DOMINATUS_GODOT_SMOKE_FRAMES
$previousArtifacts = $env:DOMINATUS_GODOT_SMOKE_ARTIFACTS
$previousVisualMode = $env:DOMINATUS_TINYTOWN_VISUAL_MODE
$previousAtlasPath = $env:DOMINATUS_TINYTOWN_ATLAS_PATH

try {
    $env:DOMINATUS_GODOT_SMOKE = "1"
    $env:DOMINATUS_GODOT_SMOKE_FRAMES = $targetSmokeFrames.ToString()
    $env:DOMINATUS_GODOT_SMOKE_ARTIFACTS = $artifactsDir
    $env:DOMINATUS_TINYTOWN_VISUAL_MODE = $VisualMode
    if ($AtlasPath) {
        $env:DOMINATUS_TINYTOWN_ATLAS_PATH = $AtlasPath
    }
    else {
        Remove-Item Env:DOMINATUS_TINYTOWN_ATLAS_PATH -ErrorAction SilentlyContinue
    }

    $godotArgs = @('--path', $sampleDir, '--quit-after', $quitAfterFrames)
    if ($Headless) {
        $godotArgs = @('--headless') + $godotArgs
    }

    & $godotBin @godotArgs 2>&1 | Tee-Object -FilePath $logPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Godot exited with code $LASTEXITCODE. See $logPath"
    }
}
finally {
    $env:DOMINATUS_GODOT_SMOKE = $previousSmoke
    $env:DOMINATUS_GODOT_SMOKE_FRAMES = $previousFrames
    $env:DOMINATUS_GODOT_SMOKE_ARTIFACTS = $previousArtifacts
    $env:DOMINATUS_TINYTOWN_VISUAL_MODE = $previousVisualMode
    $env:DOMINATUS_TINYTOWN_ATLAS_PATH = $previousAtlasPath
}

Assert-Condition (Test-Path $debugJsonPath) "Missing debug JSON artifact: $debugJsonPath"

$snapshot = Get-Content $debugJsonPath -Raw | ConvertFrom-Json
$villagers = @($snapshot.villagers)
$movedVillagers = @($villagers | Where-Object { [double]$_.distanceFromInitialPosition -gt 8.0 })
$navigationObservedVillagers = @($villagers | Where-Object { $_.observedNavigationActive -eq $true })
$nanVillagers = @($villagers | Where-Object {
    [double]::IsNaN([double]$_.position.x) -or
    [double]::IsNaN([double]$_.position.y) -or
    [double]::IsNaN([double]$_.velocity.x) -or
    [double]::IsNaN([double]$_.velocity.y)
})
$largeJumpVillagers = @($villagers | Where-Object { [double]$_.maxPhysicsStepDistance -gt 18.0 })
$dwellVillagers = @($villagers | Where-Object { $_.phase -eq "Dwell" })
$distinctObservedActivities = @($snapshot.observedActivities | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$observedDwellActivities = @($snapshot.observedDwellActivities | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$observedTravelActivities = @($snapshot.observedTravelActivities | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$nonEmergencyActivities = @($distinctObservedActivities | Where-Object { $_ -in @('ShopAtMarket', 'TendGarden', 'Socialize', 'Wander', 'Idle / Think') })
$villagersWithPhaseVariety = @($villagers | Where-Object { @($_.observedPhases).Count -ge 2 })
$highStressVillagers = @($villagers | Where-Object { [double]$_.maxNeed -ge 0.98 })
$fallbackVillagers = @($villagers | Where-Object { $_.usingFallbackVisuals -eq $true })
$logLines = Get-Content $logPath
$errorLines = @($logLines | Where-Object { $_ -match '(^|\s)ERROR[: ]' -or $_ -match 'System\.ArgumentException:' })
$duplicateLines = @($logLines | Where-Object { $_ -match 'ScriptTypeBiMap' -or $_ -match 'same key has already been added' })
$averageNeedUrgency = [double]$snapshot.averageNeedUrgency
$maxNeedUrgency = [double]$snapshot.maxNeedUrgency
$visualMode = [string]$snapshot.visualMode
$atlasSourceKind = [string]$snapshot.atlasSourceKind
$atlasPath = [string]$snapshot.atlasPath
$atlasTomlPath = [string]$snapshot.atlasTomlPath
$atlasTomlLoaded = [bool]$snapshot.atlasTomlLoaded
$atlasTomlWarnings = @($snapshot.atlasTomlWarnings)
$atlasWidth = [int]$snapshot.atlasWidth
$atlasHeight = [int]$snapshot.atlasHeight
$gridColumns = [int]$snapshot.gridColumns
$gridRows = [int]$snapshot.gridRows
$cellWidth = [int]$snapshot.cellWidth
$cellHeight = [int]$snapshot.cellHeight
$normalizedAtlasUsed = [bool]$snapshot.normalizedAtlasUsed
$alphaAtlasUsed = [bool]$snapshot.alphaAtlasUsed
$alphaDetected = [bool]$snapshot.alphaDetected
$transparentPixelCount = [long]$snapshot.transparentPixelCount
$villagerVisualMode = [string]$snapshot.villagerVisualMode
$destinationVisualMode = [string]$snapshot.destinationVisualMode
$fallbackVisualsUsed = [bool]$snapshot.fallbackVisualsUsed
$spriteAssetsLoaded = [int]$snapshot.spriteAssetsLoaded
$spriteEntitiesLoaded = [int]$snapshot.spriteEntitiesLoaded
$spriteAnimationsLoaded = [int]$snapshot.spriteAnimationsLoaded
$villagerSpritesLoaded = [int]$snapshot.villagerSpritesLoaded
$destinationSpritesLoaded = [int]$snapshot.destinationSpritesLoaded
$correctedFramesUsed = [int]$snapshot.correctedFramesUsed
$missingAssetWarnings = [int]$snapshot.missingAssetWarnings
$audioBridgeEnabled = [bool]$snapshot.audioBridgeEnabled
$audioProviderId = [string]$snapshot.audioProviderId
$generatedBarkCount = [int]$snapshot.generatedBarkCount
$playedBarkCount = [int]$snapshot.playedBarkCount
$audioArtifactsWritten = [int]$snapshot.audioArtifactsWritten
$audioPlaybackFailures = [int]$snapshot.audioPlaybackFailures
$audioArtifactDirectory = [string]$snapshot.audioArtifactDirectory
$barkingVillagers = @($villagers | Where-Object { [int]$_.barkCount -ge 1 })
$audioPlaybackActiveVillagers = @($villagers | Where-Object { $_.audioPlaybackActive -eq $true })
$audioFiles = if (-not [string]::IsNullOrWhiteSpace($audioArtifactDirectory) -and (Test-Path $audioArtifactDirectory)) {
    @(Get-ChildItem -Path $audioArtifactDirectory -Filter *.wav -File)
}
else {
    @()
}
$sidecarFiles = if (-not [string]::IsNullOrWhiteSpace($audioArtifactDirectory) -and (Test-Path $audioArtifactDirectory)) {
    @(Get-ChildItem -Path $audioArtifactDirectory -Filter *.audio.json -File)
}
else {
    @()
}
$firstSidecar = if ($sidecarFiles.Count -ge 1) {
    Get-Content $sidecarFiles[0].FullName -Raw | ConvertFrom-Json
}
else {
    $null
}

Assert-Condition ([uint64]$snapshot.tickCount -gt 0) "TinyTown never ticked. tickCount=$($snapshot.tickCount)"
Assert-Condition ([int]$snapshot.agentCount -eq 4) "Expected 4 agents, found $($snapshot.agentCount)"
Assert-Condition (-not [string]::IsNullOrWhiteSpace($visualMode)) "Expected visualMode in TinyTown smoke snapshot."
Assert-Condition ($audioBridgeEnabled) "Expected TinyTown audio bridge to be enabled."
Assert-Condition ($audioProviderId -eq "fake") "Expected fake audio provider by default, found '$audioProviderId'."
Assert-Condition ($generatedBarkCount -ge 1) "Expected at least one generated bark, found $generatedBarkCount."
Assert-Condition ($audioArtifactsWritten -ge 1) "Expected at least one written audio artifact, found $audioArtifactsWritten."
Assert-Condition ($audioFiles.Count -ge 1) "Expected at least one .wav bark artifact in '$audioArtifactDirectory'."
Assert-Condition ($sidecarFiles.Count -ge 1) "Expected at least one audio sidecar JSON in '$audioArtifactDirectory'."
Assert-Condition ($barkingVillagers.Count -ge 1) "Expected at least one villager bark snapshot."
Assert-Condition ($audioPlaybackFailures -eq 0) "Expected no audio playback failures, found $audioPlaybackFailures."
Assert-Condition ($null -ne $firstSidecar) "Expected to parse at least one audio sidecar JSON."
Assert-Condition ($firstSidecar.hiddenWatermarkAddedByDominatus -eq $false) "Expected hiddenWatermarkAddedByDominatus=false in sidecar."
Assert-Condition ($movedVillagers.Count -ge 1) "Expected at least one villager to move more than 8 units."
Assert-Condition ($navigationObservedVillagers.Count -ge 1) "Expected at least one villager to use navigation during the smoke run."
Assert-Condition ($nanVillagers.Count -eq 0) "Detected NaN position/velocity values in TinyTown smoke snapshot."
Assert-Condition ($largeJumpVillagers.Count -eq 0) "Detected unexpectedly large per-physics-frame jumps in TinyTown smoke snapshot."
Assert-Condition ($distinctObservedActivities.Count -ge 4) "Expected at least four distinct activities across the smoke run."
Assert-Condition ($observedTravelActivities.Count -ge 2) "Expected travel activity variety in the smoke run."
Assert-Condition ($observedDwellActivities.Count -ge 2) "Expected at least two dwell activities in the smoke run."
Assert-Condition ($nonEmergencyActivities.Count -ge 1) "Expected at least one non-emergency activity to appear."
Assert-Condition (($dwellVillagers.Count -ge 1) -or ($villagersWithPhaseVariety.Count -ge 1)) "Expected at least one villager to enter a non-travel dwell phase."
Assert-Condition ($averageNeedUrgency -le 0.62) "Average need urgency too high at end of smoke: $averageNeedUrgency"
Assert-Condition ($maxNeedUrgency -le 0.97) "Max need urgency too high at end of smoke: $maxNeedUrgency"
Assert-Condition ($highStressVillagers.Count -le 1) "Too many villagers ended the run near hard emergency."
Assert-Condition ($duplicateLines.Count -eq 0) "Detected duplicate ScriptTypeBiMap registration evidence in run.log"
Assert-Condition ($errorLines.Count -eq 0) "Detected unexpected ERROR lines in run.log"
Assert-Condition ($visualMode -eq $VisualMode) "Expected visualMode=$VisualMode, found $visualMode"
if (-not $Headless) {
    Assert-Condition ($snapshot.screenshotSaved -and (Test-Path $screenshotPath)) "Expected a saved screenshot artifact for non-headless smoke runs."
    Assert-Condition ($playedBarkCount -ge 1) "Expected at least one played bark during non-headless smoke, found $playedBarkCount."
}
if ($VisualMode -eq "FallbackShapes") {
    Assert-Condition ($villagerVisualMode -eq "FallbackShapes") "Expected fallback villager visuals by default, found $villagerVisualMode"
    Assert-Condition ($destinationVisualMode -eq "FallbackShapes") "Expected fallback destination visuals by default, found $destinationVisualMode"
    Assert-Condition ($fallbackVisualsUsed) "Expected fallback visuals to be in use when no art atlas is requested."
    Assert-Condition ($fallbackVillagers.Count -eq $villagers.Count) "Expected all villagers to report fallback visuals in fallback smoke mode."
    Assert-Condition ($spriteAssetsLoaded -eq 0) "Fallback smoke should not require sprite assets, but snapshot reported $spriteAssetsLoaded loaded assets."
    Assert-Condition ($missingAssetWarnings -eq 0) "Fallback smoke should not log missing asset warnings, but snapshot reported $missingAssetWarnings."
}
else {
    Assert-Condition ($villagerVisualMode -eq $VisualMode) "Expected villager visual mode $VisualMode, found $villagerVisualMode"
    Assert-Condition ($destinationVisualMode -eq "StaticSprites") "Expected destination visuals to use static sprites, found $destinationVisualMode"
    Assert-Condition (-not $fallbackVisualsUsed) "Sprite smoke should not fall back when a valid atlas is present."
    Assert-Condition ($fallbackVillagers.Count -eq 0) "Expected no villager fallback visuals in sprite mode."
    Assert-Condition ($spriteAssetsLoaded -gt 0) "Sprite smoke expected atlas-backed visuals, but snapshot reported $spriteAssetsLoaded loaded sprite assets."
    Assert-Condition ($alphaAtlasUsed) "Sprite smoke should prefer the cleaned alpha atlas path, but alphaAtlasUsed was false."
    Assert-Condition ($atlasSourceKind -in @('AlphaOriginal', 'AlphaNormalized', 'TomlAlphaOriginal', 'TomlAlphaNormalized')) "Sprite smoke should report an alpha atlas source kind, found $atlasSourceKind."
    Assert-Condition ($alphaDetected) "Sprite smoke expected atlas transparency to be detected."
    Assert-Condition ($transparentPixelCount -gt 0) "Sprite smoke expected transparent pixels in the alpha atlas."
    Assert-Condition ($atlasTomlLoaded) "Sprite smoke expected sprite atlas TOML metadata to load."
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($atlasTomlPath)) "Sprite smoke expected atlasTomlPath to be populated."
    Assert-Condition ($atlasTomlWarnings.Count -eq 0) "Sprite smoke expected no TOML metadata diagnostics, but found $($atlasTomlWarnings.Count)."
    Assert-Condition ($gridColumns -eq 12 -and $gridRows -eq 6) "Sprite smoke expected a 12x6 sprite grid, found ${gridColumns}x${gridRows}."
    Assert-Condition ($spriteEntitiesLoaded -ge 9) "Sprite smoke expected semantic sprite entities to load, found $spriteEntitiesLoaded."
    Assert-Condition ($spriteAnimationsLoaded -ge 16) "Sprite smoke expected villager animation metadata to load, found $spriteAnimationsLoaded."
    Assert-Condition ($villagerSpritesLoaded -eq 4) "Expected all four villager rows to resolve, found $villagerSpritesLoaded."
    Assert-Condition ($destinationSpritesLoaded -ge 5) "Expected destination prop sprites to resolve for at least five mapped destinations, found $destinationSpritesLoaded."
    Assert-Condition ($missingAssetWarnings -eq 0) "Sprite smoke should not log missing asset warnings, but snapshot reported $missingAssetWarnings."
    Assert-Condition ($atlasWidth -gt 0 -and $atlasHeight -gt 0) "Sprite smoke expected atlas dimensions, found ${atlasWidth}x${atlasHeight}."
    Assert-Condition ($cellWidth -gt 0 -and $cellHeight -gt 0) "Sprite smoke expected cell dimensions, found ${cellWidth}x${cellHeight}."
}

$screenshotMessage = if ((Test-Path $screenshotPath) -and $snapshot.screenshotSaved) {
    "Screenshot: $screenshotPath"
}
elseif ($snapshot.screenshotError) {
    "Screenshot unavailable: $($snapshot.screenshotError)"
}
else {
    "Screenshot unavailable."
}

Write-Host "Smoke harness passed."
Write-Host "Observed activities: $($distinctObservedActivities -join ', ')"
Write-Host "Observed dwell activities: $($observedDwellActivities -join ', ')"
Write-Host "Observed travel activities: $($observedTravelActivities -join ', ')"
Write-Host "Audio: bridge=$audioBridgeEnabled provider=$audioProviderId generated=$generatedBarkCount played=$playedBarkCount failures=$audioPlaybackFailures artifacts=$audioArtifactsWritten dir=$audioArtifactDirectory activeVillagers=$($audioPlaybackActiveVillagers.Count)"
Write-Host "Visuals: mode=$visualMode villagers=$villagerVisualMode destinations=$destinationVisualMode fallback=$fallbackVisualsUsed spriteAssets=$spriteAssetsLoaded warnings=$missingAssetWarnings"
Write-Host "Atlas: source=$atlasSourceKind path=$atlasPath tomlLoaded=$atlasTomlLoaded tomlPath=$atlasTomlPath size=${atlasWidth}x${atlasHeight} grid=${gridColumns}x${gridRows} cell=${cellWidth}x${cellHeight} normalized=$normalizedAtlasUsed alphaPath=$alphaAtlasUsed alphaDetected=$alphaDetected transparentPixels=$transparentPixelCount entities=$spriteEntitiesLoaded animations=$spriteAnimationsLoaded villagerSprites=$villagerSpritesLoaded destinationSprites=$destinationSpritesLoaded correctedFrames=$correctedFramesUsed"
Write-Host ("Need summary: average={0:N2} max={1:N2}" -f $averageNeedUrgency, $maxNeedUrgency)
Write-Host "Artifacts:"
Write-Host "  Log: $logPath"
Write-Host "  Debug JSON: $debugJsonPath"
Write-Host "  $screenshotMessage"
