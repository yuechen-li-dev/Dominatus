[CmdletBinding()]
param(
    [string]$GodotPath,
    [UInt64]$SmokeFrames = 120,
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
$quitAfterFrames = [Math]::Max([int]($SmokeFrames * 4), 300)

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
    $env:DOMINATUS_GODOT_SMOKE_FRAMES = $SmokeFrames.ToString()
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
$activityChanges = @($villagers | Where-Object { $_.activity -ne "Arriving" -or $_.need -ne "Settling in" })
$logText = Get-Content $logPath -Raw
$logLines = Get-Content $logPath
$errorLines = @($logLines | Where-Object { $_ -match '(^|\s)ERROR[: ]' -or $_ -match 'System\.ArgumentException:' })
$duplicateLines = @($logLines | Where-Object { $_ -match 'ScriptTypeBiMap' -or $_ -match 'same key has already been added' })

Assert-Condition ([uint64]$snapshot.tickCount -gt 0) "TinyTown never ticked. tickCount=$($snapshot.tickCount)"
Assert-Condition ([int]$snapshot.agentCount -eq 4) "Expected 4 agents, found $($snapshot.agentCount)"
Assert-Condition ($movedVillagers.Count -ge 1) "Expected at least one villager to move more than 8 units."
Assert-Condition ($activityChanges.Count -ge 1) "Expected at least one villager activity or need label to change."
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
Write-Host "Artifacts:"
Write-Host "  Log: $logPath"
Write-Host "  Debug JSON: $debugJsonPath"
Write-Host "  $screenshotMessage"
