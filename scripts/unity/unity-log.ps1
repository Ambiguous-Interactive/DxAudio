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

function ConvertTo-SafeUnityReport {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$Text
    )
    $secrets = @($env:UNITY_SERIAL, $env:UNITY_EMAIL, $env:UNITY_PASSWORD) |
        Where-Object { -not [string]::IsNullOrEmpty($_) } | Sort-Object Length -Descending
    foreach ($secret in $secrets) {
        $Text = $Text.Replace($secret, '[REDACTED]')
    }
    $Text = $Text -replace '(?m)-----BEGIN [^\r\n-]*PRIVATE KEY-----\r?\n(?:[A-Za-z0-9+/=]+\r?\n)*(?:-----END [^\r\n-]*PRIVATE KEY-----|[A-Za-z0-9+/=]+$)?', '[REDACTED]'
    $Text = $Text -replace '(?i)(<License\s+id=")[^"]+(")', '$1[REDACTED]$2'
    $Text = $Text -replace '(?i)(\b(?:Authorization:\s*Bearer|Successfully updated the access token|Access token\s*[:=])[^\S\r\n]*["'']?)[A-Za-z0-9_+/=.\-]{5,}', '$1[REDACTED]'
    $Text = $Text -replace '(?i)(\b(?:UNITY_SERIAL|UNITY_EMAIL|UNITY_PASSWORD|GITHUB_TOKEN|API_KEY)\s*=|-(?:serial|username|password)\s+)(?:"[^"\r\n]*"|''[^''\r\n]*''|[^\s\[\]<>]+)', '$1[REDACTED]'
    $Text = $Text -replace '(?i)\b(?:SC-[A-Za-z0-9-]{8,}|gh[pousr]_[A-Za-z0-9_]{16,}|github_pat_[A-Za-z0-9_]{16,}|(?:AKIA|ASIA)[A-Z0-9]{16})\b', '[REDACTED]'
    return $Text -replace '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]|\r(?!\n)', '?'
}

# Sanitize before choosing the tail so a multiline credential that starts above
# the visible region still gets removed.
function Write-RedactedLogTail {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Path,
        [ValidateRange(1, 1000)][int]$MaxLines = 40
    )
    if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-Host "  | (no log file)"
        return
    }
    $raw = Get-Content -LiteralPath $Path -Raw
    $text = ConvertTo-SafeUnityReport -Text ([string]$raw)
    foreach ($line in ($text -split '\r?\n' | Select-Object -Last $MaxLines)) {
        Write-Host "  | $line"
    }
}

function Read-UnityNUnitResult {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.MaxCharactersInDocument = 64MB
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = [System.Xml.XmlDocument]::new()
        $document.Load($reader)
        return ,$document
    } finally {
        $reader.Dispose()
    }
}

function Assert-PassingUnityNUnitResult {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][System.Xml.XmlDocument]$Document)

    $root = $Document.DocumentElement
    if ($null -eq $root -or $root.Name -cne 'test-run' -or $root.GetAttribute('result') -cne 'Passed') {
        throw 'Unity did not report a passing EditMode run.'
    }
    $counts = @{}
    foreach ($name in @('total', 'passed', 'failed', 'inconclusive', 'skipped')) {
        if ($root.GetAttribute($name) -notmatch '^[0-9]{1,9}$') {
            throw "Unity EditMode result has an invalid $name count."
        }
        $counts[$name] = [int]$root.GetAttribute($name)
    }
    $cases = $root.SelectNodes('.//test-case')
    if ($counts.passed -eq 0 -or $counts.failed -ne 0 -or $counts.inconclusive -ne 0 -or
        $counts.total -ne ($counts.passed + $counts.skipped) -or $cases.Count -ne $counts.total -or
        $root.SelectNodes('.//test-case[@result="Passed"]').Count -ne $counts.passed -or
        $root.SelectNodes('.//test-case[@result="Skipped"]').Count -ne $counts.skipped) {
        throw 'Unity EditMode result does not account for each test in the passing run.'
    }
    return $counts.passed
}
