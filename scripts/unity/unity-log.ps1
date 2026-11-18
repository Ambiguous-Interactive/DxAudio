# Shared Unity run naming and redacted log output for the DxAudio Unity workflows.

# One job runs the EditMode suite once per optional-integration state, and every
# run shares RUNNER_TEMP. A label keeps each run's project, results and log in
# its own path. An empty label keeps the historical version-only name.
function Get-EditModeRunName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$UnityVersion,
        [string]$Label = ''
    )
    if ([string]::IsNullOrWhiteSpace($Label)) {
        return $UnityVersion
    }
    # The name becomes a path segment, so accept only characters a shell and a
    # path both keep intact.
    if ($Label -notmatch '^[A-Za-z0-9-]+$') {
        throw "Label must be empty or match ^[A-Za-z0-9-]+\$; got '$Label'."
    }
    return "$UnityVersion-$Label"
}

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
