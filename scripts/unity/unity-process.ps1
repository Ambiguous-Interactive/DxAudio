# Unity writes its own log while it runs. Read only new bytes and share writes
# with the editor until it exits.
function Read-UnityLogDelta {
    param([string]$Path, [ref]$Offset)

    if (-not [IO.File]::Exists($Path)) { return '' }
    try {
        $stream = [IO.File]::Open($Path, 'Open', 'Read', ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    } catch [IO.IOException] {
        return ''
    }
    try {
        if ($stream.Length -lt $Offset.Value) { $Offset.Value = 0L }
        [void]$stream.Seek($Offset.Value, 'Begin')
        $reader = [IO.StreamReader]::new($stream)
        try {
            $text = $reader.ReadToEnd()
            $Offset.Value = $stream.Position
            return $text
        } finally {
            $reader.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Invoke-UnityEditorProcess {
    param(
        [Diagnostics.ProcessStartInfo]$Start,
        [string]$LogPath,
        [string]$ObservationPath,
        [double]$TimeoutSeconds,
        [int]$StallSeconds,
        [int]$CompletionGraceSeconds,
        [switch]$WatchTestCompletion
    )

    $receipt = [ordered]@{
        ProcessId = $null
        StartedUtc = [DateTime]::UtcNow
        EndedUtc = $null
        Origin = 'launch-failed'
        RawExitCode = $null
        ExitCode = $null
        CompletionExitCode = $null
    }
    $process = $null
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $process = [Diagnostics.Process]::Start($Start)
        $receipt.ProcessId = $process.Id
        $receipt.Origin = 'process-exit'
        [void]$process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
        [void]$process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
        $lastOutput = 0.0
        $completedTime = 0.0
        $offset = 0L
        $observedLength = 0L
        $partialLine = ''
        while (-not $process.WaitForExit(250)) {
            $elapsed = $watch.Elapsed.TotalSeconds
            if ($WatchTestCompletion -and $null -eq $receipt.CompletionExitCode) {
                $file = [IO.FileInfo]::new($LogPath)
                if ($file.Exists -and $file.Length -ne $observedLength) {
                    $observedLength = $file.Length
                    $lastOutput = $elapsed
                }
                $delta = if ($observedLength -ne $offset) { Read-UnityLogDelta -Path $LogPath -Offset ([ref]$offset) } else { '' }
                if ($delta.Length) {
                    $lastOutput = $elapsed
                    $buffer = $partialLine + $delta
                    $end = $buffer.LastIndexOf("`n")
                    if (0 -le $end) {
                        $completeLines = $buffer.Substring(0, $end + 1)
                        $partialLine = $buffer.Substring($end + 1)
                        if ($completeLines -cmatch '(?m)^Test run completed\. Exiting with code (?<code>[0-9]{1,3})(?:[ .(][^\r\n]*)?\r?$') {
                            $receipt.CompletionExitCode = [int]$Matches.code
                            $completedTime = $elapsed
                        }
                    } else {
                        $partialLine = $buffer
                    }
                }
            }
            $origin = if ($TimeoutSeconds -le $elapsed) {
                'wall-timeout'
            } elseif ($null -ne $receipt.CompletionExitCode -and $CompletionGraceSeconds -le ($elapsed - $completedTime)) {
                'completion-grace'
            } elseif ($WatchTestCompletion -and $null -eq $receipt.CompletionExitCode -and 0 -lt $StallSeconds -and $StallSeconds -le ($elapsed - $lastOutput)) {
                'stall-timeout'
            }
            if (-not $origin -or $process.HasExited) { continue }
            try {
                $process.Kill($true)
            } catch [InvalidOperationException] {
                if ($process.HasExited) { break }
                throw
            }
            $receipt.Origin = $origin
            if (-not $process.WaitForExit(5000)) {
                throw 'Unity did not stop after the watchdog terminated its process tree.'
            }
            break
        }
        $receipt.RawExitCode = $process.ExitCode
        $receipt.ExitCode = switch ($receipt.Origin) {
            'wall-timeout' { 124 }
            'stall-timeout' { 125 }
            'completion-grace' { $receipt.CompletionExitCode }
            default { $receipt.RawExitCode }
        }
        return $receipt
    } finally {
        if ($null -ne $process) { $process.Dispose() }
        $receipt.EndedUtc = [DateTime]::UtcNow
        $receipt | ConvertTo-Json | Set-Content -LiteralPath $ObservationPath -Encoding utf8NoBOM
    }
}

function Test-TransientUnityActivation {
    param([string]$LogPath, [string]$ResultsPath)

    if ([IO.File]::Exists($ResultsPath) -or -not [IO.File]::Exists($LogPath)) { return $false }
    $log = Get-Content -LiteralPath $LogPath -Raw
    if ($log -match '(?m)^(?:Application\.AssetDatabase Initial Refresh Start|\[ScriptCompilation\]|Test run completed\.|DxAudio .*validation passed\.)') {
        return $false
    }
    if ($log -match '(?im)Licensing::[^\r\n]*(?:\b20113\b|serial expired)') { return $false }
    return $log -match '(?im)Licensing::[^\r\n]*(?:\b20111\b|maximum number of activations)'
}
