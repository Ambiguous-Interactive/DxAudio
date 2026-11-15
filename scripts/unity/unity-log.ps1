# Shared redacted Unity log output for the DxAudio Unity workflows.
#
# The runner's job log is shared and durable, so this helper removes every
# credential shape the caller knows before it prints anything.
function Write-RedactedLogTail {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$MaxLines = 40
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-Host "  | (no log file at $Path)"
        return
    }
    $text = (Get-Content -LiteralPath $Path -Tail $MaxLines -ErrorAction Stop) -join "`n"
    foreach ($secret in @($env:UNITY_SERIAL, $env:UNITY_EMAIL, $env:UNITY_PASSWORD)) {
        if (-not [string]::IsNullOrEmpty($secret)) {
            $text = $text.Replace($secret, '[REDACTED]')
        }
    }
    $text = [regex]::Replace($text, 'SC-[A-Za-z0-9-]{8,}', '[REDACTED]')
    $text = [regex]::Replace($text, '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]', '?')
    foreach ($line in ($text -split "`n")) {
        Write-Host "  | $line"
    }
}
