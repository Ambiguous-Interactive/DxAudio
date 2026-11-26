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
$arguments = @('run')
foreach ($pair in @(
    @('--unity-version', $UnityVersion),
    @('--unity-editor-path', $UnityEditorPath),
    @('--package-root', $PackageRoot),
    @('--dxmessaging-version', $DxMessagingVersion),
    @('--label', $Label),
    @('--consumer-archive', $ConsumerArchive),
    @('--unity-import-archive', $UnityImportArchive)
)) {
    if (-not [string]::IsNullOrWhiteSpace($pair[1])) {
        $arguments += @($pair[0], $pair[1])
    }
}
if ($ArchiveSmoke) { $arguments += '--archive-smoke' }
if ($IntegrationPlayerSmoke) { $arguments += '--integration-player-smoke' }
$arguments += @(
    '--editor-timeout-seconds', $EditorTimeoutSeconds,
    '--editor-stall-seconds', $EditorStallSeconds,
    '--completion-grace-seconds', $CompletionGraceSeconds,
    '--activation-retry-budget-seconds', $ActivationRetryBudgetSeconds
)

& uv run --no-project (Join-Path $PSScriptRoot 'unity_validation.py') @arguments
exit $LASTEXITCODE
