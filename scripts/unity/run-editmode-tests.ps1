#Requires -Version 7.0
# Run DxAudio's EditMode tests in a disposable Unity project.
#
# The package has no Unity project of its own, so this script creates a minimal
# project that references the checkout as a local package. The `testables` entry
# compiles Tests/Editor, which a plain file dependency does not include.
#
# The DxMessaging integration is optional: leave -DxMessagingVersion empty to
# prove the package compiles and its suite passes with the dependency absent, and
# set it to compile and test the integration assembly in the same run.
[CmdletBinding()]
param(
    [string]$UnityVersion = $env:UNITY_VERSION,
    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,
    [string]$PackageRoot = $env:GITHUB_WORKSPACE,
    [string]$DxMessagingVersion = $env:DXMESSAGING_VERSION
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

$projectPath = Join-Path $env:RUNNER_TEMP "dxaudio-unity-$UnityVersion"
if (Test-Path -LiteralPath $projectPath) {
    Remove-Item -LiteralPath $projectPath -Recurse -Force
}
$packagesPath = Join-Path $projectPath 'Packages'
$settingsPath = Join-Path $projectPath 'ProjectSettings'
New-Item -ItemType Directory -Force -Path `
    (Join-Path $projectPath 'Assets'), $packagesPath, $settingsPath | Out-Null

$testFramework = if ($UnityVersion -like '2021.*') { '1.1.33' } else { '1.4.6' }
$packageUri = $PackageRoot.Replace('\', '/')
$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.unity.test-framework' = $testFramework
        # The package uses AudioSource, AudioClip, and AudioMixerGroup. A
        # built-in module that the manifest omits is excluded from the project.
        'com.unity.modules.audio' = '1.0.0'
        'com.wallstop-studios.dxaudio' = "file:$packageUri"
    }
    testables = @('com.wallstop-studios.dxaudio')
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
Set-Content -LiteralPath (Join-Path $packagesPath 'manifest.json') -Value $manifest -Encoding utf8
Set-Content -LiteralPath (Join-Path $settingsPath 'ProjectVersion.txt') `
    -Value "m_EditorVersion: $UnityVersion" -Encoding utf8

$artifactsPath = Join-Path $env:RUNNER_TEMP "dxaudio-results-$UnityVersion"
New-Item -ItemType Directory -Force -Path $artifactsPath | Out-Null
$resultsPath = Join-Path $artifactsPath 'editmode-results.xml'
$logPath = Join-Path $artifactsPath 'editmode.log'

$unityArgs = @(
    '-batchmode'
    '-nographics'
    '-projectPath'
    $projectPath
    '-runTests'
    '-testPlatform'
    'EditMode'
    '-testResults'
    $resultsPath
    '-logFile'
    '-'
    '-serial'
    $env:UNITY_SERIAL
    '-username'
    $env:UNITY_EMAIL
    '-password'
    $env:UNITY_PASSWORD
)
# Unity writes its log to stdout. Tee it to the log file as it arrives so a
# timed-out run leaves the evidence the follow-up tail step reads. The final
# Out-Null keeps the log out of the shared job log.
$exitCode = 1
try {
    & $UnityEditorPath @unityArgs 2>&1 | Tee-Object -FilePath $logPath | Out-Null
    if (Test-Path -LiteralPath variable:\LASTEXITCODE) {
        $exitCode = $LASTEXITCODE
    }
} catch {
    Write-Host "Unity invocation failed: $_"
    throw
}

if (-not (Test-Path -LiteralPath $resultsPath -PathType Leaf)) {
    Write-Host "Unity did not write an EditMode result file (exit code $exitCode). Redacted log tail:"
    Write-RedactedLogTail -Path $logPath
    throw "Unity did not write an EditMode result file (exit code $exitCode). Log: $logPath"
}
if ($exitCode -ne 0) {
    Write-Host "Unity EditMode tests failed with exit code $exitCode. Redacted log tail:"
    Write-RedactedLogTail -Path $logPath
    throw "Unity EditMode tests failed with exit code $exitCode. Log: $logPath"
}
