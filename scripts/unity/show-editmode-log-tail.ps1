#Requires -Version 7.0
# Print a redacted tail of one EditMode run's log, then its NUnit failures. One
# job runs the suite once per optional-integration state, so -Label selects the
# run. Run under failure() when a test step fails or times out; a timed-out step
# never reaches its own failure handler, so this step is the only reader of the
# streamed log.
[CmdletBinding()]
param(
    [string]$UnityVersion = $env:UNITY_VERSION,
    [string]$Label = $env:UNITY_TEST_LABEL,
    [string]$LogPath = '',
    [string]$ResultsPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'unity-log.ps1')

if ($env:RUNNER_TEMP -and $UnityVersion) {
    $runName = Get-EditModeRunName -UnityVersion $UnityVersion -Label $Label
    if ([string]::IsNullOrWhiteSpace($LogPath)) {
        $LogPath = Join-Path $env:RUNNER_TEMP "dxaudio-results-$runName/editmode.log"
    }
    if ([string]::IsNullOrWhiteSpace($ResultsPath)) {
        $ResultsPath = Join-Path $env:RUNNER_TEMP "dxaudio-results-$runName/editmode-results.xml"
    }
}

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
