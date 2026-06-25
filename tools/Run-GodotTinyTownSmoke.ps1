[CmdletBinding()]
param(
    [string]$GodotPath,
    [UInt64]$SmokeFrames = 360,
    [switch]$LongRun,
    [UInt64]$LongRunSmokeFrames = 900,
    [string]$ArtifactsDir,
    [switch]$Headless,
    [switch]$CleanGodotCaches
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

try {
    $env:DOMINATUS_GODOT_SMOKE = "1"
    $env:DOMINATUS_GODOT_SMOKE_FRAMES = $targetSmokeFrames.ToString()
    $env:DOMINATUS_GODOT_SMOKE_ARTIFACTS = $artifactsDir

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
$logLines = Get-Content $logPath
$errorLines = @($logLines | Where-Object { $_ -match '(^|\s)ERROR[: ]' -or $_ -match 'System\.ArgumentException:' })
$duplicateLines = @($logLines | Where-Object { $_ -match 'ScriptTypeBiMap' -or $_ -match 'same key has already been added' })
$averageNeedUrgency = [double]$snapshot.averageNeedUrgency
$maxNeedUrgency = [double]$snapshot.maxNeedUrgency

Assert-Condition ([uint64]$snapshot.tickCount -gt 0) "TinyTown never ticked. tickCount=$($snapshot.tickCount)"
Assert-Condition ([int]$snapshot.agentCount -eq 4) "Expected 4 agents, found $($snapshot.agentCount)"
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
Write-Host ("Need summary: average={0:N2} max={1:N2}" -f $averageNeedUrgency, $maxNeedUrgency)
Write-Host "Artifacts:"
Write-Host "  Log: $logPath"
Write-Host "  Debug JSON: $debugJsonPath"
Write-Host "  $screenshotMessage"
