[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TomlPath,
    [string]$Selected = "",
    [string]$GodotPath = "",
    [string]$Artifacts = "",
    [switch]$NoSmoke
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "tools\Dominatus.SpriteForge.PreviewGodot"

function Resolve-GodotPath {
    param([string]$ExplicitPath)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $candidates += $ExplicitPath
    }

    if (-not [string]::IsNullOrWhiteSpace($env:GODOT_BIN)) {
        $candidates += $env:GODOT_BIN
    }

    $command = Get-Command godot -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    $candidates += @(
        "C:\Program Files\Godot\Godot_v4.7-stable_mono_win64.exe",
        "C:\Program Files\Godot\Godot_v4.7-stable_win64.exe",
        "C:\Tools\Godot\Godot_v4.7-stable_mono_win64.exe",
        "C:\Tools\Godot\Godot_v4.7-stable_win64.exe"
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Godot binary not found. Pass -GodotPath or set GODOT_BIN."
}

$resolvedTomlPath = (Resolve-Path -LiteralPath $TomlPath).Path
$resolvedArtifacts = if ([string]::IsNullOrWhiteSpace($Artifacts)) {
    Join-Path $repoRoot "artifacts\spriteforge"
} else {
    $Artifacts
}
$resolvedArtifacts = [System.IO.Path]::GetFullPath($resolvedArtifacts)
$runLogPath = Join-Path $resolvedArtifacts "run.log"
$previewPath = Join-Path $resolvedArtifacts "preview.png"
$debugJsonPath = Join-Path $resolvedArtifacts "preview-debug.json"

New-Item -ItemType Directory -Force -Path $resolvedArtifacts | Out-Null
if (Test-Path -LiteralPath $runLogPath) { Remove-Item -LiteralPath $runLogPath -Force }
if (Test-Path -LiteralPath $previewPath) { Remove-Item -LiteralPath $previewPath -Force }
if (Test-Path -LiteralPath $debugJsonPath) { Remove-Item -LiteralPath $debugJsonPath -Force }

$resolvedGodotPath = Resolve-GodotPath -ExplicitPath $GodotPath

$env:DOMINATUS_SPRITEFORGE_TOML = $resolvedTomlPath
$env:DOMINATUS_SPRITEFORGE_ARTIFACTS = $resolvedArtifacts
$env:DOMINATUS_SPRITEFORGE_SELECTED = $Selected
$env:DOMINATUS_SPRITEFORGE_SMOKE = if ($NoSmoke) { "0" } else { "1" }

$arguments = @(
    "--headless",
    "--path", $projectPath
)

$output = & $resolvedGodotPath @arguments 2>&1
$output | Out-File -LiteralPath $runLogPath -Encoding utf8

if ($LASTEXITCODE -ne 0) {
    if (Test-Path -LiteralPath $debugJsonPath) {
        try {
            $debugJson = Get-Content -LiteralPath $debugJsonPath -Raw | ConvertFrom-Json
            if ($debugJson.diagnostics) {
                Write-Host "SpriteForge preview diagnostics:"
                $debugJson.diagnostics | ForEach-Object {
                    Write-Host ("[{0}] {1}: {2}" -f $_.severity, $_.code, $_.message)
                }
            }
        } catch {
            # keep original failure if debug JSON is malformed
        }
    }

    throw "Godot preview failed with exit code $LASTEXITCODE. See $runLogPath"
}

if (-not (Test-Path -LiteralPath $previewPath)) {
    throw "Expected preview artifact was not written: $previewPath"
}

if (-not (Test-Path -LiteralPath $debugJsonPath)) {
    throw "Expected debug JSON artifact was not written: $debugJsonPath"
}

try {
    $debugJson = Get-Content -LiteralPath $debugJsonPath -Raw | ConvertFrom-Json
    if (-not $debugJson.success) {
        throw "Preview debug JSON reports success=false."
    }
} catch {
    throw "Preview debug JSON validation failed: $($_.Exception.Message)"
}

Write-Host "SpriteForge preview artifacts:"
Write-Host "  Preview: $previewPath"
Write-Host "  Debug:   $debugJsonPath"
Write-Host "  Log:     $runLogPath"
