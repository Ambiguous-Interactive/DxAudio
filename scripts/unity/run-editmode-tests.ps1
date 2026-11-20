#Requires -Version 7.0
# Validate DxAudio in a disposable Unity project.
#
# The package has no Unity project of its own, so this script creates a minimal
# project that references a staged package. The `testables` entry compiles its
# tests. Archive mode uses the npm tarball and a separate consumer smoke.
#
# The DxMessaging integration is optional: leave -DxMessagingVersion empty to
# prove the package compiles and its suite passes with the dependency absent, and
# set it to compile and test the integration assembly in the same run. One job
# runs both states, so -Label keeps each run's project, results and log apart.
[CmdletBinding()]
param(
    [string]$UnityVersion = $env:UNITY_VERSION,
    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,
    [string]$PackageRoot = $env:GITHUB_WORKSPACE,
    [string]$DxMessagingVersion = $env:DXMESSAGING_VERSION,
    [string]$Label = $env:UNITY_TEST_LABEL,
    [string]$ConsumerArchive = $env:DXAUDIO_CONSUMER_ARCHIVE,
    [switch]$ArchiveSmoke,
    [ValidateRange(1, 2400)][int]$EditorTimeoutSeconds = 2400
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A host profile can set this true, which would turn Unity's normal stderr into
# a terminating error before the exit code is read.
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot 'unity-log.ps1')

if ($UnityVersion -notmatch '^\d+\.\d+\.\d+f\d+$') {
    throw "UnityVersion must be an exact editor release, for example 6000.6.7f1."
}
foreach ($required in @('UnityEditorPath', 'PackageRoot')) {
    if ([string]::IsNullOrWhiteSpace((Get-Variable -Name $required -ValueOnly))) {
        throw "$required is required."
    }
}
foreach ($name in @('UNITY_SERIAL', 'UNITY_EMAIL', 'UNITY_PASSWORD')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "$name is required."
    }
}

$runName = Get-EditModeRunName -UnityVersion $UnityVersion -Label $Label
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw 'RUNNER_TEMP is required.'
}
$artifactsPath = Join-Path $env:RUNNER_TEMP "dxaudio-results-$runName"
if (Test-Path -LiteralPath $artifactsPath) {
    Remove-Item -LiteralPath $artifactsPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $artifactsPath | Out-Null
$resultsPath = Join-Path $artifactsPath 'editmode-results.xml'
$logPath = Join-Path $artifactsPath 'editmode.log'

$projectPath = Join-Path $env:RUNNER_TEMP "dxaudio-unity-$runName"
if (Test-Path -LiteralPath $projectPath) {
    Remove-Item -LiteralPath $projectPath -Recurse -Force
}
$packagesPath = Join-Path $projectPath 'Packages'
$settingsPath = Join-Path $projectPath 'ProjectSettings'
$assetsPath = Join-Path $projectPath 'Assets'
New-Item -ItemType Directory -Force -Path $assetsPath, $packagesPath, $settingsPath | Out-Null

$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.unity.modules.audio' = '1.0.0'
        'com.unity.modules.imgui' = '1.0.0'
    }
}
if ($ArchiveSmoke) {
    if ([string]::IsNullOrWhiteSpace($ConsumerArchive) -or -not (Test-Path -LiteralPath $ConsumerArchive -PathType Leaf)) {
        throw 'The pristine consumer tarball is missing.'
    }
    if (-not [string]::IsNullOrWhiteSpace($DxMessagingVersion)) {
        throw 'The consumer archive smoke requires the optional integration to be absent.'
    }
    Copy-Item -LiteralPath $ConsumerArchive -Destination (Join-Path $packagesPath 'dxaudio.tgz')
    $manifest.dependencies['com.wallstop-studios.dxaudio'] = 'file:dxaudio.tgz'
    $editorPath = New-Item -ItemType Directory -Path (Join-Path $assetsPath 'Editor')
    Copy-Item -LiteralPath (Join-Path $PackageRoot '.net-tests/UnityValidation/ArchiveSmoke.cs') -Destination $editorPath.FullName
} else {
    $stagedPackage = Join-Path $projectPath 'DxAudio'
    $prepareScript = Join-Path $PSScriptRoot 'prepare-package.py'
    $samplePath = Join-Path $assetsPath 'BasicUsage'
    uv run --no-project $prepareScript --source $PackageRoot --destination $stagedPackage --sample-destination $samplePath
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stage the analysis package (exit code $LASTEXITCODE)."
    }
    $manifest.dependencies['com.wallstop-studios.dxaudio'] = 'file:../DxAudio'
    $manifest.dependencies['com.unity.test-framework'] = if ($UnityVersion -like '2021.*') { '1.1.33' } else { '1.4.6' }
    $manifest.testables = @('com.wallstop-studios.dxaudio')
}
if (-not [string]::IsNullOrWhiteSpace($DxMessagingVersion)) {
    # The integration is guarded by a version define, so without this entry the
    # project compiles the package exactly as a DxMessaging-free game does.
    $manifest.dependencies['com.wallstop-studios.dxmessaging'] = $DxMessagingVersion
    $manifest.scopedRegistries = @(
        [ordered]@{
            name = 'npmjs'
            url = 'https://registry.npmjs.org'
            scopes = @('com.wallstop-studios')
        }
    )
}
$manifest = $manifest | ConvertTo-Json -Depth 5
Set-Content -LiteralPath (Join-Path $packagesPath 'manifest.json') -Value $manifest -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $settingsPath 'ProjectVersion.txt') `
    -Value "m_EditorVersion: $UnityVersion" -Encoding utf8NoBOM

$unityArgs = @(
    '-batchmode'
    '-nographics'
    '-projectPath'
    $projectPath
    '-logFile'
    $logPath
    '-serial'
    $env:UNITY_SERIAL
    '-username'
    $env:UNITY_EMAIL
    '-password'
    $env:UNITY_PASSWORD
)
if ($ArchiveSmoke) {
    $unityArgs += @('-quit', '-executeMethod', 'WallstopStudios.DxAudio.Validation.ArchiveSmoke.Run')
    $EditorTimeoutSeconds = [Math]::Min($EditorTimeoutSeconds, 900)
} else {
    $unityArgs += @('-runTests', '-testPlatform', 'EditMode', '-testResults', $resultsPath)
}

# ArgumentList keeps paths and credential values in their original arguments.
# Unity writes the raw log to disk. Only sanitized output reaches the job log.
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $UnityEditorPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
if ($UnityEditorPath.EndsWith('.ps1', [StringComparison]::OrdinalIgnoreCase)) {
    $start.FileName = [Environment]::ProcessPath
    foreach ($item in @('-NoProfile', '-NonInteractive', '-File', $UnityEditorPath)) {
        $start.ArgumentList.Add($item)
    }
}
foreach ($item in $unityArgs) {
    $start.ArgumentList.Add($item)
}
$start.Environment['DXMESSAGING_VERSION'] = $DxMessagingVersion
$process = $null
try {
    $process = [System.Diagnostics.Process]::Start($start)
    [void]$process.StandardOutput.BaseStream.CopyToAsync([System.IO.Stream]::Null)
    [void]$process.StandardError.BaseStream.CopyToAsync([System.IO.Stream]::Null)
    if (-not $process.WaitForExit($EditorTimeoutSeconds * 1000)) {
        try {
            $process.Kill($true)
        } catch [InvalidOperationException] {
            if (-not $process.HasExited) { throw }
        }
        [void]$process.WaitForExit(5000)
        throw "Unity did not exit within $EditorTimeoutSeconds seconds."
    }
    $exitCode = $process.ExitCode
} catch {
    Write-RedactedLogTail -Path $logPath
    throw (ConvertTo-SafeUnityReport -Text $_.Exception.Message)
} finally {
    if ($null -ne $process) { $process.Dispose() }
}

if ($exitCode -ne 0) {
    Write-RedactedLogTail -Path $logPath
    throw "Unity failed with exit code $exitCode. Log: $logPath"
}
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
    throw 'Unity did not write a compiler log.'
}
if (Select-String -LiteralPath $logPath -Pattern '\b(?:AD0001|AD0002|CS8032|CS8034|CS9057)\b' -Quiet) {
    Write-RedactedLogTail -Path $logPath
    throw 'Unity reported an analyzer load failure or exception.'
}
if ($ArchiveSmoke) {
    if (-not (Select-String -LiteralPath $logPath -SimpleMatch 'DxAudio archive and player script validation passed.' -Quiet)) {
        Write-RedactedLogTail -Path $logPath
        throw 'Unity did not confirm the archive and player script validation.'
    }
    return
}

if (-not (Test-Path -LiteralPath $resultsPath -PathType Leaf)) {
    Write-RedactedLogTail -Path $logPath
    throw "Unity did not write an EditMode result file. Log: $logPath"
}
try {
    $results = Read-UnityNUnitResult -Path $resultsPath
    $passed = Assert-PassingUnityNUnitResult -Document $results
} catch {
    throw (ConvertTo-SafeUnityReport -Text $_.Exception.Message)
}
foreach ($test in @(
    'AllExpectedAssembliesUseThePinnedAnalyzers',
    'AssemblyDefinitionsMatchTheCompilerReferenceGraph',
    'EditorOnlyAssembliesContainNoConcreteMonoBehaviours'
)) {
    $evidence = $results.SelectNodes("//test-case[@fullname='WallstopStudios.DxAudio.Tests.Editor.AnalyzerActivation.$test']")
    if ($evidence.Count -ne 1 -or $evidence[0].GetAttribute('result') -ne 'Passed') {
        throw "Unity did not report a passing $test control."
    }
}
$integrationSuite = $results.SelectSingleNode(
    '//test-suite[@type="Assembly" and (@name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll" or @name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging")]'
)
$expectIntegration = -not [string]::IsNullOrWhiteSpace($DxMessagingVersion)
if (($null -ne $integrationSuite) -ne $expectIntegration -or ($expectIntegration -and [int]$integrationSuite.GetAttribute('passed') -le 0)) {
    throw 'The EditMode results do not match the expected DxMessaging test state.'
}
Write-Host "EditMode passed $passed tests on $runName."
