[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$TomlPath,
    [string]$Out = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$toolProject = Join-Path $repoRoot "tools\Dominatus.SpriteAtlasPreview\Dominatus.SpriteAtlasPreview.csproj"

$arguments = @("run", "--project", $toolProject, "--", $TomlPath)
if (-not [string]::IsNullOrWhiteSpace($Out)) {
    $arguments += @("--out", $Out)
}

& dotnet @arguments
exit $LASTEXITCODE
