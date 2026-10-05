# An isolated sleeping process stands in for the owner. No audio backend starts.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $root 'staging/ui-target/release/audio-switch-panel.exe'
$config = Join-Path $env:LOCALAPPDATA 'AudioSwitch/settings.json'
function ConfigHash { if (Test-Path -LiteralPath $config) { (Get-FileHash -LiteralPath $config -Algorithm SHA256).Hash } }
if (Get-Process AudioSwitch,audio-switch-panel -ErrorAction SilentlyContinue) { throw 'Close existing project test windows first; this script never stops them.' }
$before = ConfigHash
$owner = Start-Process -FilePath (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe') -ArgumentList '-NoProfile', '-NonInteractive', '-Command', 'Start-Sleep -Seconds 12' -WindowStyle Hidden -PassThru
$panel = Start-Process -FilePath $exe -ArgumentList '--prompt', "--owner=$($owner.Id)" -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 4
    if ($panel.HasExited) { throw 'Prompt exited before its owner.' }
    $processes = @(Get-CimInstance Win32_Process)
    $ids = [System.Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($panel.Id)
    do {
        $added = $false
        foreach ($process in $processes) { if ($ids.Contains([int]$process.ParentProcessId) -and $ids.Add([int]$process.ProcessId)) { $added = $true } }
    } while ($added)
    $during = @($ids | ForEach-Object { $item = Get-Process -Id $_ -ErrorAction SilentlyContinue; if ($item) { [PSCustomObject]@{ Pid=$item.Id; StartTicks=$item.StartTime.Ticks; WorkingSetBytes=$item.WorkingSet64 } } })
    if (!$owner.WaitForExit(20000)) { throw 'Isolated owner did not exit.' }
    if (!$panel.WaitForExit(10000)) { throw 'Prompt remained after owner exited.' }
    Start-Sleep -Seconds 2
    $remaining = @($during | Where-Object { $item = Get-Process -Id $_.Pid -ErrorAction SilentlyContinue; $item -and $item.StartTime.Ticks -eq $_.StartTicks })
    $after = ConfigHash
    [PSCustomObject]@{ OwnerPid=$owner.Id; PromptPid=$panel.Id; During=$during; Remaining=$remaining; ConfigurationUnchanged=($before -eq $after); OwnerExitClosedPrompt=$panel.HasExited } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'frontend/output/desktop/stage43-owner.json') -Encoding utf8
    if ($remaining.Count -or $before -ne $after) { throw 'Process or configuration check failed.' }
    Write-Output "Owner exit closed prompt; $($during.Count) prompt/WebView2 processes exited; configuration unchanged."
} finally {
    if (!$panel.HasExited -and $panel.Path -eq $exe) { [void]$panel.CloseMainWindow() }
    # The isolated owner exits itself; do not kill unrelated processes.
}
