#Requires -Version 7.0
# Compatibility entry point for the Unity workflow. Validation logic lives in
# unity_validation.py; this file only maps the historical parameters.
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

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$arguments = @('run')

function Add-ValueArgument([string]$name, [string]$value) {
    # One token keeps option-like values (for example a label that starts with a
    # dash) from being parsed as flags. An explicit empty value still overrides
    # the environment default, as the historical parameter binding did.
    $script:arguments += "$name=$value"
}

Add-ValueArgument '--unity-version' $UnityVersion
Add-ValueArgument '--unity-editor-path' $UnityEditorPath
Add-ValueArgument '--package-root' $PackageRoot
Add-ValueArgument '--dxmessaging-version' $DxMessagingVersion
Add-ValueArgument '--label' $Label
Add-ValueArgument '--consumer-archive' $ConsumerArchive
Add-ValueArgument '--unity-import-archive' $UnityImportArchive
if ($ArchiveSmoke) { $arguments += '--archive-smoke' }
if ($IntegrationPlayerSmoke) { $arguments += '--integration-player-smoke' }
Add-ValueArgument '--editor-timeout-seconds' ([string]$EditorTimeoutSeconds)
Add-ValueArgument '--editor-stall-seconds' ([string]$EditorStallSeconds)
Add-ValueArgument '--completion-grace-seconds' ([string]$CompletionGraceSeconds)
Add-ValueArgument '--activation-retry-budget-seconds' ([string]$ActivationRetryBudgetSeconds)

& uv run --no-project (Join-Path $PSScriptRoot 'unity_validation.py') @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Unity validation failed with exit code $LASTEXITCODE."
}
