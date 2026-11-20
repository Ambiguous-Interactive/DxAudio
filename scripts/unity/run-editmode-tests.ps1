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
    [switch]$ArchiveSmoke
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

$artifactsPath = Join-Path $env:RUNNER_TEMP "dxaudio-results-$runName"
if (Test-Path -LiteralPath $artifactsPath) {
    Remove-Item -LiteralPath $artifactsPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $artifactsPath | Out-Null
$resultsPath = Join-Path $artifactsPath 'editmode-results.xml'
$logPath = Join-Path $artifactsPath 'editmode.log'
New-Item -ItemType File -Path $logPath | Out-Null

$unityArgs = @(
    '-batchmode'
    '-nographics'
    '-projectPath'
    $projectPath
    '-logFile'
    '-'
    '-serial'
    $env:UNITY_SERIAL
    '-username'
    $env:UNITY_EMAIL
    '-password'
    $env:UNITY_PASSWORD
)
if ($ArchiveSmoke) {
    $unityArgs += @('-quit', '-executeMethod', 'WallstopStudios.DxAudio.Validation.ArchiveSmoke.Run')
} else {
    $unityArgs += @('-runTests', '-testPlatform', 'EditMode', '-testResults', $resultsPath)
}
# Unity writes its log to stdout. Tee it to the log file as it arrives so a
# timed-out run leaves the evidence the follow-up tail step reads. The final
# Out-Null keeps the log out of the shared job log.
$exitCode = 1
try {
    $env:DXMESSAGING_VERSION = $DxMessagingVersion
    & $UnityEditorPath @unityArgs 2>&1 | Tee-Object -FilePath $logPath | Out-Null
    if (Test-Path -LiteralPath variable:\LASTEXITCODE) {
        $exitCode = $LASTEXITCODE
    }
} catch {
    Write-RedactedLogTail -Path $logPath
    throw
}

if ($exitCode -ne 0) {
    Write-RedactedLogTail -Path $logPath
    throw "Unity failed with exit code $exitCode. Log: $logPath"
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
[xml]$results = Get-Content -LiteralPath $resultsPath -Raw
$testRun = $results.SelectSingleNode('/test-run')
if ($null -eq $testRun -or $testRun.GetAttribute('result') -ne 'Passed' -or [int]$testRun.GetAttribute('passed') -le 0) {
    throw 'Unity did not report a passing EditMode run with executed tests.'
}
foreach ($test in @(
    'AllExpectedAssembliesUseThePinnedAnalyzers',
    'AssemblyDefinitionsMatchTheCompilerReferenceGraph',
    'EditorOnlyAssembliesContainNoConcreteMonoBehaviours'
)) {
    $evidence = $results.SelectSingleNode("//test-case[@name='$test']")
    if ($null -eq $evidence -or $evidence.GetAttribute('result') -ne 'Passed') {
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
Write-Host "EditMode passed $($testRun.GetAttribute('passed')) tests on $runName."
