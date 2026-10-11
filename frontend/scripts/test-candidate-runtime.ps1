param([Parameter(Mandatory)][string]$ArchivePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'candidate-common.ps1')
$root = Split-Path (Split-Path $PSScriptRoot)
$work = New-CandidateDirectory (Join-Path $root ('staging/candidate-runtime-tests-' + [Guid]::NewGuid().ToString('N')))
$checks = [Collections.Generic.List[object]]::new()
$settings = Join-Path $env:LOCALAPPDATA 'AudioSwitch/settings.json'
function SettingsHash { if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { 'absent' } }
function TreeHash([string]$Directory) {
    @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName + '=' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n"
}
function Check([bool]$Condition, [string]$Name) {
    if (!$Condition) { throw "FAIL: $Name" }
    $checks.Add($Name); Write-Host "OK $Name"
}
function RunCheck([string]$Directory, [switch]$Maintenance) {
    $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $Directory 'AudioSwitch.exe'))
    $info.Arguments = if ($Maintenance) { '--panel-maintenance' } else { '--check-files --quiet' }
    $info.WorkingDirectory = $Directory
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true; $info.RedirectStandardOutput = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
    $process = [Diagnostics.Process]::Start($info)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        if ($Maintenance) { $process.StandardInput.WriteLine('files'); $process.StandardInput.Flush() }
        if (!$process.WaitForExit(10000)) { throw '只读检查未在限定时间内退出。' }
        if (!$output.Wait(1000)) { throw '只读检查输出未结束。' }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output.Result }
    } finally {
        $process.StandardInput.Close()
        if (!$process.HasExited -and !$process.WaitForExit(2000)) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
}
$beforeSettings = SettingsHash
$results = [Collections.Generic.List[object]]::new()
foreach ($mode in @('valid', 'notice-damaged', 'panel-missing', 'manifest-missing')) {
    $directory = Join-Path $work "中文 & (只读检查)/$mode"
    $package = Test-CandidateArchive ([IO.Path]::GetFullPath($ArchivePath)) (Get-CandidateVersion) $directory
    if ($mode -eq 'notice-damaged') { [IO.File]::AppendAllText((Join-Path $directory 'THIRD-PARTY-FRONTEND.txt'), 'test damage') }
    if ($mode -eq 'panel-missing') { Move-Item -LiteralPath (Join-Path $directory 'audio-switch-panel.exe') -Destination (Join-Path $directory 'panel.saved') }
    if ($mode -eq 'manifest-missing') { Move-Item -LiteralPath (Join-Path $directory 'candidate-manifest.json') -Destination (Join-Path $directory 'manifest.saved') }
    $before = TreeHash $directory
    $quiet = RunCheck $directory
    Check ($quiet.ExitCode -eq $(if ($mode -eq 'valid') { 0 } else { 2 })) "quiet whole-package check: $mode"
    $worker = RunCheck $directory -Maintenance
    $response = $worker.Output | ConvertFrom-Json
    Check ($worker.ExitCode -eq 0 -and $response.Status -eq $(if ($mode -eq 'valid') { 'passed' } else { 'failed' }) -and $response.Message.Contains('候选整包')) "maintenance Chinese whole-package result: $mode"
    Check ((TreeHash $directory) -ceq $before) "read-only checks leave all candidate files unchanged: $mode"
    $results.Add([pscustomobject]@{ Mode = $mode; Quiet = $quiet; Maintenance = $response })
}
Check ((SettingsHash) -ceq $beforeSettings) 'active user configuration unchanged'
[pscustomobject]@{ Archive = [IO.Path]::GetFullPath($ArchivePath); Checks = $checks; Results = $results; SettingsUnchanged = $true } |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $work 'results.json') -Encoding utf8
Write-Host "PASS: $($checks.Count) checks; $work/results.json"
