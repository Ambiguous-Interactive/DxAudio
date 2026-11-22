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
    [switch]$IntegrationPlayerSmoke,
    [string]$UnityImportArchive,
    [ValidateRange(1, 2400)][int]$EditorTimeoutSeconds = 2400,
    [ValidateRange(0, 2400)][int]$EditorStallSeconds = 300,
    [ValidateRange(0, 2400)][int]$CompletionGraceSeconds = 120,
    [ValidateRange(0, 360)][int]$ActivationRetryBudgetSeconds = 360
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# A host profile can set this true, which would turn Unity's normal stderr into
# a terminating error before the exit code is read.
$PSNativeCommandUseErrorActionPreference = $false

. (Join-Path $PSScriptRoot 'unity-log.ps1')
. (Join-Path $PSScriptRoot 'unity-process.ps1')

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
$playerSmoke = $ArchiveSmoke -or $IntegrationPlayerSmoke
$importSmoke = -not [string]::IsNullOrWhiteSpace($UnityImportArchive)
if ($importSmoke) {
    if ($playerSmoke -or -not (Test-Path -LiteralPath $UnityImportArchive -PathType Leaf)) {
        throw 'Unity import validation requires its own archive and project.'
    }
    $editorPath = New-Item -ItemType Directory -Path (Join-Path $assetsPath 'Editor')
    Copy-Item -LiteralPath (Join-Path $PackageRoot '.net-tests/UnityValidation/UnityPackageSmoke.cs') -Destination $editorPath.FullName
} elseif ($playerSmoke) {
    if ([string]::IsNullOrWhiteSpace($ConsumerArchive) -or -not (Test-Path -LiteralPath $ConsumerArchive -PathType Leaf)) {
        throw 'The pristine consumer tarball is missing.'
    }
    if ($ArchiveSmoke -and ($IntegrationPlayerSmoke -or -not [string]::IsNullOrWhiteSpace($DxMessagingVersion))) {
        throw 'The consumer archive smoke requires the optional integration to be absent.'
    }
    if ($IntegrationPlayerSmoke -and [string]::IsNullOrWhiteSpace($DxMessagingVersion)) {
        throw 'The integration player smoke requires a DxMessaging version.'
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
$receipt = ''
if ($importSmoke) {
    $receipt = 'DxAudio Unity import validation passed.'
    $unityArgs += @('-executeMethod', 'WallstopStudios.DxAudio.Validation.UnityPackageSmoke.Run')
    $EditorTimeoutSeconds = [Math]::Min($EditorTimeoutSeconds, 300)
} elseif ($playerSmoke) {
    $method = if ($IntegrationPlayerSmoke) { 'RunWithIntegration' } else { 'Run' }
    $receipt = if ($IntegrationPlayerSmoke) { 'DxAudio integration player script validation passed.' } else { 'DxAudio archive and player script validation passed.' }
    $unityArgs += @('-quit', '-executeMethod', "WallstopStudios.DxAudio.Validation.ArchiveSmoke.$method")
    $EditorTimeoutSeconds = [Math]::Min($EditorTimeoutSeconds, 420)
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
if ($importSmoke) {
    $start.Environment['DXAUDIO_UNITYPACKAGE'] = $UnityImportArchive
}
$observationPath = Join-Path $artifactsPath 'process-observation.json'
$launchClock = [Diagnostics.Stopwatch]::StartNew()
$retryWindow = [Math]::Min($ActivationRetryBudgetSeconds, $EditorTimeoutSeconds)
$attempt = 0
try {
    while ($true) {
        $attempt++
        $process = Invoke-UnityEditorProcess -Start $start -LogPath $logPath -ObservationPath $observationPath `
            -TimeoutSeconds ($EditorTimeoutSeconds - $launchClock.Elapsed.TotalSeconds) `
            -StallSeconds $EditorStallSeconds -CompletionGraceSeconds $CompletionGraceSeconds -WatchTestCompletion:(-not ($playerSmoke -or $importSmoke))
        if ($process.Origin -ne 'process-exit' -or $process.ExitCode -eq 0 -or
            $retryWindow -le $launchClock.Elapsed.TotalSeconds -or
            -not (Test-TransientUnityActivation -LogPath $logPath -ResultsPath $resultsPath)) {
            break
        }
        $remaining = $retryWindow - $launchClock.Elapsed.TotalSeconds
        $delay = [Math]::Min([Math]::Min(60, 5 * [Math]::Pow(2, $attempt - 1)), $remaining / 2)
        if ($delay -le 0) { break }
        Write-Host '::warning::Unity activation hit its 20111 seat limit before project startup. Retrying within the activation budget.'
        Start-Sleep -Seconds $delay
        Remove-Item -LiteralPath $logPath
    }
} catch {
    Write-RedactedLogTail -Path $logPath
    throw (ConvertTo-SafeUnityReport -Text $_.Exception.Message)
}
Write-UnityEnvironmentWarnings -Path $logPath
if ($process.Origin -eq 'wall-timeout') {
    Write-RedactedLogTail -Path $logPath
    throw "Unity did not exit within $EditorTimeoutSeconds seconds."
}
if ($process.Origin -eq 'stall-timeout') {
    Write-RedactedLogTail -Path $logPath
    throw "Unity produced no log output for $EditorStallSeconds seconds before completion."
}
if ($process.Origin -eq 'completion-grace') {
    Write-Host '::warning::Unity reported test completion but required shutdown after its grace period. The NUnit result remains mandatory.'
}

$exitCode = $process.ExitCode
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
if ($playerSmoke -or $importSmoke) {
    if (-not (Select-String -LiteralPath $logPath -Pattern ('^' + [regex]::Escape($receipt) + '$') -Quiet)) {
        Write-RedactedLogTail -Path $logPath
        if ($importSmoke) { throw "Unity did not emit the expected import validation receipt: $receipt" }
        throw "Unity did not emit the expected player script validation receipt: $receipt"
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
$integrationSuites = $results.SelectNodes(
    '//test-suite[@type="Assembly" and (@name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging.dll" or @name="WallstopStudios.DxAudio.Tests.Integration.DxMessaging")]'
)
$expectIntegration = -not [string]::IsNullOrWhiteSpace($DxMessagingVersion)
if ($integrationSuites.Count -ne [int]$expectIntegration) {
    throw 'The EditMode results do not match the expected DxMessaging test state.'
}
if ($expectIntegration) {
    $suite = $integrationSuites[0]
    $reported = $suite.GetAttribute('passed')
    $actual = $suite.SelectNodes('.//test-case[@result="Passed"]').Count
    if ($reported -notmatch '^[0-9]{1,9}$' -or $actual -le 0 -or [int]$reported -ne $actual) {
        throw 'The EditMode results do not match the expected DxMessaging test state. The suite must account for its passing test cases.'
    }
}
Write-Host "EditMode passed $passed tests on $runName."
Write-UnityNUnitTimings -Document $results

foreach ($output in $results.SelectNodes("//test-case[@fullname='WallstopStudios.DxAudio.Tests.Editor.AllocationTests.CompareMusicVolumeCost']/output")) {
    foreach ($line in ($output.InnerText -split '\r?\n')) {
        if ($line -match '^DXAUDIO_VOLUME_PROBE (tick|apply) baseline median [0-9.]+ p95 [0-9.]+ us GC.Alloc [0-9]+ candidate median [0-9.]+ p95 [0-9.]+ us GC.Alloc [0-9]+$') {
            Write-Host $line
        }
    }
}
