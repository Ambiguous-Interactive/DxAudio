#Requires -Version 7.0
# Print a redacted tail of the last EditMode log. Run under always() when the
# test step fails or times out; a timed-out step never reaches its own failure
# handler, so this step is the only reader of the streamed log.
[CmdletBinding()]
param(
    [string]$UnityVersion = $env:UNITY_VERSION,
    [string]$LogPath = $(if ($env:RUNNER_TEMP -and $UnityVersion) {
            Join-Path $env:RUNNER_TEMP "dxaudio-results-$UnityVersion/editmode.log"
        } else { '' })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'unity-log.ps1')
Write-Host "Unity EditMode log tail (redacted):"
Write-RedactedLogTail -Path $LogPath
