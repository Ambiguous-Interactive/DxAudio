#Requires -Version 7.0
# Compatibility entry point for the Unity failure-report steps. The redacted
# log and NUnit output are owned by unity_validation.py.
[CmdletBinding()]
param(
    [string]$UnityVersion = $env:UNITY_VERSION,
    [string]$Label = $env:UNITY_TEST_LABEL,
    [string]$LogPath = '',
    [string]$ResultsPath = ''
)

$ErrorActionPreference = 'Stop'
$arguments = @('tail')
$arguments += "--unity-version=$UnityVersion"
$arguments += "--label=$Label"
if (-not [string]::IsNullOrWhiteSpace($LogPath)) { $arguments += "--log-path=$LogPath" }
if (-not [string]::IsNullOrWhiteSpace($ResultsPath)) { $arguments += "--results-path=$ResultsPath" }

& uv run --no-project (Join-Path $PSScriptRoot 'unity_validation.py') @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Unity log report failed with exit code $LASTEXITCODE."
}
