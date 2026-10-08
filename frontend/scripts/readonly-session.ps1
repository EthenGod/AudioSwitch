param([ValidateSet('Open','Close')][string]$Command, [switch]$WaitingWorker)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $root 'staging/AudioSwitch.Tests.exe'
$recordPath = Join-Path $root 'staging/readonly-session.json'
if ($Command -eq 'Open') {
    if (Get-Process AudioSwitch -ErrorAction SilentlyContinue) { throw '已有后台，请先由用户关闭。' }
    if (Test-Path -LiteralPath $recordPath) {
        $old = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
        $existing = Get-Process -Id $old.Pid -ErrorAction SilentlyContinue
        if ($existing -and $existing.Path -eq $exe -and $existing.StartTime.Ticks -eq $old.StartTicks) { throw '只读联调入口已运行。' }
    }
    $token = [Guid]::NewGuid().ToString('N')
    $stdout = Join-Path $root "staging/readonly-$token.stdout.log"
    $stderr = Join-Path $root "staging/readonly-$token.stderr.log"
    if ($WaitingWorker -and !(Test-Path -LiteralPath (Join-Path $root 'staging/readonly-wait-fixture/AudioSwitch.exe'))) { throw '请先编译 readonly-wait-worker.cs 夹具。' }
    $mode = if ($WaitingWorker) {'--panel-readonly-wait-host'} else {'--panel-readonly-host'}
    $process = Start-Process -FilePath $exe -ArgumentList $mode,$token -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    [pscustomobject]@{Pid=$process.Id;StartTicks=$process.StartTime.Ticks;Token=$token;Executable=$exe;Stdout=$stdout;Stderr=$stderr} | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
    "Read-only bridge PID: $($process.Id)"
} else {
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    $process = Get-Process -Id $record.Pid -ErrorAction SilentlyContinue
    if ($process -and $process.Path -eq $exe -and $process.StartTime.Ticks -eq $record.StartTicks) {
        $stop = [Threading.EventWaitHandle]::OpenExisting("Local\AudioSwitch-ReadOnlyTest-$($record.Token)")
        try { [void]$stop.Set(); if (!$process.WaitForExit(10000)) { throw '只读入口仍未退出，请检查。' } } finally { $stop.Dispose() }
    }
    $auditPath = Join-Path $root "staging/readonly-session-$($record.Token).json"
    $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
    $unchanged = $audit.ConfigurationUnchanged -and ($audit.Before | ConvertTo-Json -Depth 12 -Compress) -ceq ($audit.After | ConvertTo-Json -Depth 12 -Compress)
    [pscustomobject]@{Exited=!(Get-Process -Id $record.Pid -ErrorAction SilentlyContinue);ConfigurationUnchanged=$audit.ConfigurationUnchanged;AudioReadingsUnchanged=$unchanged;Audit=$auditPath}
    if (!$unchanged) { throw '前后只读记录不一致，请检查审计文件，不自动恢复或改写设置。' }
}
