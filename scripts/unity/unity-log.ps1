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

function Write-UnityEnvironmentWarnings {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-Host 'Unity environment warnings: nothing was scanned.'
        return
    }
    $patterns = [ordered]@{
        'while verifying Licensing Client signature' = 'Licensing client signature validation failed.'
        'LicensingClient has failed validation' = 'Licensing client was ignored after failing validation.'
        "failed because channel doesn't exist" = 'Licensing IPC channel was absent at connection attempt.'
    }
    $lines = @(Get-Content -LiteralPath $Path)
    $matches = 0
    foreach ($pattern in $patterns.Keys) {
        $count = @($lines | Select-String -SimpleMatch $pattern).Count
        if ($count -eq 0) { continue }
        $matches += $count
        Write-Host "::warning::$($patterns[$pattern]) Observed $count time(s). License cleanup is checked separately."
    }
    Write-Host "Unity environment warnings: scanned $($lines.Count) log line(s), $matches match(es)."
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

function Write-UnityNUnitTimings {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][System.Xml.XmlDocument]$Document)

    $root = $Document.DocumentElement
    if ($null -eq $root -or $root.Name -cne 'test-run') {
        throw 'Unity timing report requires an NUnit test-run.'
    }
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $lines = [Collections.Generic.List[string]]::new()
    $warnings = [Collections.Generic.List[string]]::new()
    foreach ($group in @(
        @{ Label = 'fixtures'; XPath = './/test-suite[@type="TestFixture"]'; Budget = 120 },
        @{ Label = 'test cases'; XPath = './/test-case'; Budget = 0 }
    )) {
        $timings = [Collections.Generic.List[object]]::new()
        foreach ($node in $root.SelectNodes($group.XPath)) {
            $seconds = 0.0
            $duration = $node.GetAttribute('duration')
            if ([string]::IsNullOrWhiteSpace($duration) -and $node.GetAttribute('result') -in @('Skipped', 'NotRun')) {
                continue
            }
            if (-not [double]::TryParse($duration, [Globalization.NumberStyles]::Float, $culture, [ref]$seconds) -or
                -not [double]::IsFinite($seconds) -or $seconds -lt 0) {
                throw "Unity timing report has a missing or invalid duration among its $($group.Label)."
            }
            $name = $node.GetAttribute('fullname')
            if (-not $name) { $name = $node.GetAttribute('name') }
            $timings.Add(@{ Name = $name; Seconds = $seconds })
            if (0 -lt $group.Budget -and $group.Budget -lt $seconds) {
                $safeName = (ConvertTo-SafeUnityReport -Text $name) -replace '\s+', ' '
                $warnings.Add("Fixture $safeName took $($seconds.ToString('F3', $culture))s, over the $($group.Budget)s budget.")
            }
        }
        $timings.Sort([Comparison[object]]{
            param($left, $right)
            $durationOrder = $right.Seconds.CompareTo($left.Seconds)
            if ($durationOrder -ne 0) { return $durationOrder }
            return [StringComparer]::Ordinal.Compare($left.Name, $right.Name)
        })
        $lines.Add("Unity EditMode slowest $($group.Label):")
        foreach ($timing in ($timings | Select-Object -First 30)) {
            $name = (ConvertTo-SafeUnityReport -Text $timing.Name) -replace '\s+', ' '
            $lines.Add("  | $($timing.Seconds.ToString('F3', $culture))s  $name")
        }
    }
    $renderTimings = @(
        foreach ($output in $root.SelectNodes('.//test-case[starts-with(@fullname, "WallstopStudios.DxAudio.Tests.Editor.DspHostTests.DspChainsStayWithinTheRenderBudget(")]/output')) {
            foreach ($line in ((ConvertTo-SafeUnityReport -Text $output.InnerText) -split '\r?\n')) {
                if ($line.StartsWith('DxAudio DSP render: ', [StringComparison]::Ordinal)) {
                    "  | $line"
                }
            }
        }
    )
    if (0 -lt $renderTimings.Count) {
        $lines.Add('Unity EditMode DSP render timings:')
        $lines.AddRange([string[]]$renderTimings)
    }
    foreach ($line in $lines) { Write-Host $line }
    foreach ($warning in $warnings) {
        Write-Host "::warning::$($warning.Replace('%', '%25'))"
    }
    if ($env:GITHUB_STEP_SUMMARY) {
        $safeHtml = [Net.WebUtility]::HtmlEncode($lines -join "`n")
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "`n### Unity EditMode timings`n`n<pre>$safeHtml</pre>`n" -Encoding utf8NoBOM
    }
}
