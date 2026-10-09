# An isolated sleeping process stands in for the owner. No audio backend starts.
param([switch]$Panel, [string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $root 'staging/ui-target/release/audio-switch-panel.exe'
if ($ExecutablePath) {
    $exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
    if (!$exe.StartsWith(([IO.Path]::GetFullPath((Join-Path $root 'staging')) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($exe) -ne 'audio-switch-panel.exe') { throw 'Only a staging panel is allowed.' }
}
$config = Join-Path $env:LOCALAPPDATA 'AudioSwitch/settings.json'
function ConfigHash { if (Test-Path -LiteralPath $config) { (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash } }
if (Get-Process AudioSwitch,audio-switch-panel -ErrorAction SilentlyContinue) { throw 'Close existing project test windows first; this script never stops them.' }
$before = ConfigHash
$owner = Start-Process -FilePath (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe') -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 12' -WindowStyle Hidden -PassThru
$arguments = @("--owner=$($owner.Id)"); if (!$Panel) { $arguments = @('--prompt') + $arguments }
$isPanel = [bool]$Panel
$panelProcess = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 4
    if ($panelProcess.HasExited) { throw 'Window exited before its owner.' }
    $processes = @(Get-CimInstance Win32_Process)
    $ids = [System.Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($panelProcess.Id)
    do {
        $added = $false
        foreach ($process in $processes) { if ($ids.Contains([int]$process.ParentProcessId) -and $ids.Add([int]$process.ProcessId)) { $added = $true } }
    } while ($added)
    $during = @($ids | ForEach-Object { $item = Get-Process -Id $_ -ErrorAction SilentlyContinue; if ($item) { [PSCustomObject]@{ Pid=$item.Id; StartTicks=$item.StartTime.Ticks; WorkingSetBytes=$item.WorkingSet64 } } })
    if (!$owner.WaitForExit(20000)) { throw 'Isolated owner did not exit.' }
    if (!$panelProcess.WaitForExit(10000)) { throw 'Window remained after owner exited.' }
    Start-Sleep -Seconds 2
    $remaining = @($during | Where-Object { $item = Get-Process -Id $_.Pid -ErrorAction SilentlyContinue; $item -and $item.StartTime.Ticks -eq $_.StartTicks })
    $after = ConfigHash
    $recordName = if ($isPanel) { 'stage52-panel-owner.json' } else { 'stage52-prompt-owner.json' }
    [PSCustomObject]@{ OwnerPid=$owner.Id; WindowPid=$panelProcess.Id; During=$during; Remaining=$remaining; ConfigurationUnchanged=($before -eq $after); OwnerExitClosedWindow=$panelProcess.HasExited } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root "frontend/output/desktop/$recordName") -Encoding utf8
    if ($remaining.Count -or $before -ne $after) { throw 'Process or configuration check failed.' }
    Write-Output "Owner exit closed window; $($during.Count) UI/WebView2 processes exited; configuration unchanged."
} finally {
    if (!$panelProcess.HasExited -and $panelProcess.Path -eq $exe) { [void]$panelProcess.CloseMainWindow() }
    # The isolated owner exits itself; do not kill unrelated processes.
}
