param(
    [ValidateSet('Open', 'Sample', 'Close')][string]$Command = 'Sample',
    [switch]$DebugBrowser,
    [switch]$Prompt
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $root 'staging\ui-target\release\audio-switch-panel.exe'
$output = Join-Path $root 'frontend\output\desktop'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$recordPath = Join-Path $output 'lifecycle.json'
$configPath = Join-Path $env:LOCALAPPDATA 'AudioSwitch\settings.json'
function ConfigurationHash {
    if (Test-Path -LiteralPath $configPath) { return (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash }
    return $null
}
function BackendSample {
    @(Get-Process AudioSwitch -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $root 'bin\AudioSwitch.exe') } | ForEach-Object {
        [PSCustomObject]@{ Pid=$_.Id; WorkingSetBytes=$_.WorkingSet64; PrivateBytes=$_.PrivateMemorySize64 }
    })
}
if ($Command -eq 'Open') {
    if (Get-Process audio-switch-panel -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }) { throw '已有本项目面板在运行，请先手动关闭。' }
    $before = BackendSample
    $hashBefore = ConfigurationHash
    $previousDebug = $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
    try {
        if ($DebugBrowser) {
            if (Get-NetTCPConnection -LocalPort 9223 -State Listen -ErrorAction SilentlyContinue) { throw '9223 已占用，不启动调试。' }
            $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = '--remote-debugging-port=9223 --remote-debugging-address=127.0.0.1'
        }
        if ($Prompt) { $panel = Start-Process -FilePath $exe -ArgumentList '--prompt' -WindowStyle Hidden -PassThru }
        else { $panel = Start-Process -FilePath $exe -WindowStyle Hidden -PassThru }
    } finally { $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = $previousDebug }
    [PSCustomObject]@{ Pid=$panel.Id; StartTicks=$panel.StartTime.Ticks; Executable=$exe; Before=$before; ConfigurationBefore=$hashBefore; DebugBrowser=[bool]$DebugBrowser; Opened=(Get-Date).ToString('o') } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
    Write-Output "Panel PID: $($panel.Id)"
    exit
}
$record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
$panel = Get-Process -Id $record.Pid -ErrorAction SilentlyContinue
if ($panel -and ($panel.Path -ne $exe -or $panel.StartTime.Ticks -ne $record.StartTicks)) { throw '面板路径或启动时间不一致，停止操作。' }
if ($Command -eq 'Sample') {
    if (!$panel) { throw '记录的面板已退出。' }
    $processes = @(Get-CimInstance Win32_Process)
    $ids = [System.Collections.Generic.HashSet[int]]::new()
    [void]$ids.Add($panel.Id)
    do {
        $added = $false
        foreach ($process in $processes) {
            if ($ids.Contains([int]$process.ParentProcessId) -and $ids.Add([int]$process.ProcessId)) { $added = $true }
        }
    } while ($added)
    $sample = @($ids | ForEach-Object {
        $item = Get-Process -Id $_ -ErrorAction SilentlyContinue
        if ($item) { [PSCustomObject]@{ Pid=$item.Id; Name=$item.ProcessName; StartTicks=$item.StartTime.Ticks; WorkingSetBytes=$item.WorkingSet64; PrivateBytes=$item.PrivateMemorySize64 } }
    })
    $record | Add-Member -Force NoteProperty During $sample
    $record | Add-Member -Force NoteProperty BackendDuring (BackendSample)
    $sample | Format-Table
} else {
    if ($panel) {
        if (!$panel.CloseMainWindow()) { throw '无法正常关闭面板，请手动关闭；不会强制结束进程。' }
        if (!$panel.WaitForExit(10000)) { throw '面板未在 10 秒内退出，请手动检查。' }
    }
    Start-Sleep -Seconds 3
    $remaining = @($record.During | ForEach-Object {
        $item = Get-Process -Id $_.Pid -ErrorAction SilentlyContinue
        if ($item -and $item.StartTime.Ticks -eq $_.StartTicks) { $item.Id }
    })
    $record | Add-Member -Force NoteProperty RemainingPids $remaining
    $record | Add-Member -Force NoteProperty After (BackendSample)
    $record | Add-Member -Force NoteProperty ConfigurationAfter (ConfigurationHash)
    Write-Output "Remaining panel/WebView2 processes: $($remaining.Count)"
    Write-Output "Configuration hash unchanged: $($record.ConfigurationBefore -eq $record.ConfigurationAfter)"
}
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
if ($Command -eq 'Close' -and $remaining.Count) { throw '仍有属于本次面板的进程，请检查记录。' }
