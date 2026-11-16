#Requires -Version 7.0
# Print a redacted tail of the last EditMode log, then the NUnit failures. Run
# under always() when the test step fails or times out; a timed-out step never
# reaches its own failure handler, so this step is the only reader of the
# streamed log.
[CmdletBinding()]
param(
    [string]$UnityVersion = $env:UNITY_VERSION,
    [string]$LogPath = $(if ($env:RUNNER_TEMP -and $UnityVersion) {
            Join-Path $env:RUNNER_TEMP "dxaudio-results-$UnityVersion/editmode.log"
        } else { '' }),
    [string]$ResultsPath = $(if ($env:RUNNER_TEMP -and $UnityVersion) {
            Join-Path $env:RUNNER_TEMP "dxaudio-results-$UnityVersion/editmode-results.xml"
        } else { '' })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'unity-log.ps1')
Write-Host "Unity EditMode log tail (redacted):"
Write-RedactedLogTail -Path $LogPath

# An exit code alone cannot say which test failed; the result file can.
if ($ResultsPath -and (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
    Write-Host "Unity EditMode failures:"
    try {
        [xml]$results = Get-Content -LiteralPath $ResultsPath -Raw
        $failed = 0
        foreach ($case in $results.SelectNodes('//test-case[@result="Failed" or @result="Error"]')) {
            ++$failed
            $message = ''
            $failure = $case.SelectSingleNode('failure/message')
            if ($null -ne $failure) {
                $message = ($failure.InnerText -replace '\s+', ' ').Trim()
            }
            Write-Host "  $($case.fullname): $message"
        }
        Write-Host "  $failed failed case(s)."
    } catch {
        Write-Host "  Could not read $ResultsPath : $_"
    }
} else {
    Write-Host "Unity EditMode failures: no result file at $ResultsPath."
}
