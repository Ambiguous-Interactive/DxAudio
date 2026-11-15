#Requires -Version 5.1
# Run DxAudio's EditMode tests in a disposable Unity project.
#
# The package has no Unity project of its own, so this script creates a minimal
# project that references the checkout as a local package. The `testables` entry
# compiles Tests/Editor, which a plain file dependency does not include.
[CmdletBinding()]
param(
    [string]$UnityVersion = $env:UNITY_VERSION,
    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,
    [string]$PackageRoot = $env:GITHUB_WORKSPACE
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
$packagesPath = Join-Path $projectPath 'Packages'
$settingsPath = Join-Path $projectPath 'ProjectSettings'
New-Item -ItemType Directory -Force -Path `
    (Join-Path $projectPath 'Assets'), $packagesPath, $settingsPath | Out-Null

$testFramework = if ($UnityVersion -like '2021.*') { '1.1.33' } else { '1.4.6' }
$packageUri = $PackageRoot.Replace('\', '/')
$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.unity.test-framework' = $testFramework
        'com.wallstop-studios.dxaudio' = "file:$packageUri"
    }
    testables = @('com.wallstop-studios.dxaudio')
} | ConvertTo-Json -Depth 4
Set-Content -LiteralPath (Join-Path $packagesPath 'manifest.json') -Value $manifest -Encoding utf8
Set-Content -LiteralPath (Join-Path $settingsPath 'ProjectVersion.txt') `
    -Value "m_EditorVersion: $UnityVersion" -Encoding utf8

$artifactsPath = Join-Path $env:RUNNER_TEMP "dxaudio-results-$UnityVersion"
New-Item -ItemType Directory -Force -Path $artifactsPath | Out-Null
$resultsPath = Join-Path $artifactsPath 'editmode-results.xml'
$logPath = Join-Path $artifactsPath 'editmode.log'

& $UnityEditorPath -batchmode -nographics `
    -projectPath $projectPath `
    -runTests -testPlatform EditMode `
    -testResults $resultsPath `
    -logFile $logPath `
    -serial $env:UNITY_SERIAL `
    -username $env:UNITY_EMAIL `
    -password $env:UNITY_PASSWORD
$exitCode = $LASTEXITCODE

if (-not (Test-Path -LiteralPath $resultsPath -PathType Leaf)) {
    throw "Unity did not write an EditMode result file (exit code $exitCode)."
}
if ($exitCode -ne 0) {
    throw "Unity EditMode tests failed with exit code $exitCode."
}
