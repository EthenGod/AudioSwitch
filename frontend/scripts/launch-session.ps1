param(
    [ValidateSet('Open','Check','Snapshot','Sample','Idle','Close')][string]$Command,
    [Alias('PackageDirectory')][string]$CandidateDirectory,
    [string]$FixturePath,
    [switch]$NoBrowserDebug,
    [ValidatePattern('^[a-zA-Z0-9-]{1,64}$')][string]$RecordName = 'stage52-launch-session'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$recordPath = Join-Path $root "frontend/output/desktop/$RecordName.json"
$fixture = Join-Path $root 'staging/AudioSwitch.Tests.exe'
if ($FixturePath) { $fixture = (Resolve-Path -LiteralPath $FixturePath).Path }
$staging = [IO.Path]::GetFullPath((Join-Path $root 'staging')) + [IO.Path]::DirectorySeparatorChar
if (!$fixture.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($fixture) -cne 'AudioSwitch.Tests.exe') { throw 'Only a project staging test executable is allowed' }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($recordPath))
$config = Join-Path $env:LOCALAPPDATA 'AudioSwitch/settings.json'
function ConfigHash { if (Test-Path -LiteralPath $config) { (Get-FileHash -LiteralPath $config).Hash } }
function Request([string]$Action) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', "AudioSwitch-$sid-$session", [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(2000); $writer=[IO.StreamWriter]::new($pipe); $writer.AutoFlush=$true; $reader=[IO.StreamReader]::new($pipe)
        $writer.WriteLine((@{Action=$Action} | ConvertTo-Json -Compress))
        $reply=$reader.ReadLineAsync(); if(!$reply.Wait(15000)){throw 'Fixture reply timed out'}
        $state=$reply.Result | ConvertFrom-Json
        if($state.BackendPid -ne $record.Pid -or $state.Warning -ne '隔离启动验证：模拟设备，禁止音频及配置写入。'){throw 'Not the isolated launch fixture'}
        if($state.OperationError){throw $state.OperationError}; return $state
    } finally {$pipe.Dispose()}
}
function Panel([int]$Id) {
    $process=Get-Process -Id $Id -ErrorAction Stop
    if($process.Path -ne $record.Panel){throw 'Unexpected panel path'}
    return $process
}
function Sample {
    $tree=@(Get-CimInstance Win32_Process); $ids=[Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($record.Pid)
    do { $added=$false; foreach($item in $tree){if($ids.Contains([int]$item.ParentProcessId) -and $ids.Add([int]$item.ProcessId)){$added=$true}} } while($added)
    @($ids | ForEach-Object {$p=Get-Process -Id $_ -ErrorAction SilentlyContinue; if($p){[pscustomobject]@{Pid=$p.Id;StartTicks=$p.StartTime.Ticks;Name=$p.ProcessName;WorkingSetBytes=$p.WorkingSet64;PrivateBytes=$p.PrivateMemorySize64}}})
}
function IsBaseline($item) {
    return @($record.Before | Where-Object { $_.Pid -eq $item.Pid -and $_.StartTicks -eq $item.StartTicks }).Count -gt 0
}
function InitializeWindowCheck {
    if (!('LaunchTestWindow' -as [type])) {
        # Window titles are translated. The caller verifies the executable path first.
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class LaunchTestWindow {
    public delegate bool Callback(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    public static IntPtr Find(int processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, state) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId && IsWindowVisible(window) && GetWindowTextLength(window) > 0) {
                found = window;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
    }
}
function CloseWindow([int]$Id) {
    $windowProcess = Panel $Id
    InitializeWindowCheck
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do { $window = [LaunchTestWindow]::Find($Id); if ($window -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 } } while ($window -eq [IntPtr]::Zero -and !$windowProcess.HasExited -and $watch.ElapsedMilliseconds -lt 10000)
    if ($window -eq [IntPtr]::Zero -or ![LaunchTestWindow]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) -or !$windowProcess.WaitForExit(10000)) { throw 'Test window did not close normally' }
}
if($Command -eq 'Open') {
    if(Get-Process AudioSwitch,AudioSwitch.Tests,audio-switch-panel -ErrorAction SilentlyContinue){throw '已有声间进程；请先由用户关闭，不结束其他实例。'}
    $directory=(Resolve-Path -LiteralPath $CandidateDirectory).Path
    $staging=[IO.Path]::GetFullPath((Join-Path $root 'staging')) + [IO.Path]::DirectorySeparatorChar
    if(!$directory.StartsWith($staging,[StringComparison]::OrdinalIgnoreCase)){throw 'Only project staging candidates are accepted'}
    if(!$NoBrowserDebug -and (Get-NetTCPConnection -LocalPort 9223 -State Listen -ErrorAction SilentlyContinue)){throw '9223 already occupied'}
    $token=[Guid]::NewGuid().ToString('N'); $hash=ConfigHash
    $oldDebug=$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
    try {
        $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = if ($NoBrowserDebug) { $null } else { '--remote-debugging-port=9223 --remote-debugging-address=127.0.0.1' }
        $process=Start-Process -FilePath $fixture -ArgumentList '--frontend-launch-host',$token,('"'+$directory+'"') -WindowStyle Hidden -PassThru
    } finally {$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=$oldDebug}
    $record=[pscustomobject]@{Pid=$process.Id;StartTicks=$process.StartTime.Ticks;Token=$token;Fixture=$fixture;Panel=(Join-Path $directory 'audio-switch-panel.exe');ConfigBefore=$hash;DebugBrowser=(!$NoBrowserDebug)}
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
    Request snapshot | Out-Null
    $record | Add-Member NoteProperty Before (Sample)
    Request show | Select-Object BackendPid,FrontendPid,PromptPid
} else {
    $record=Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    $process=Get-Process -Id $record.Pid -ErrorAction Stop
    if($process.Path -ne $fixture -or $process.StartTime.Ticks -ne $record.StartTicks){throw 'Recorded fixture identity changed'}
    if($Command -eq 'Check') {
        $first=Request show; $again=Request show
        if(!$first.FrontendPid -or $first.FrontendPid -ne $again.FrontendPid){throw 'Repeated show did not reuse panel'}
        $panel=Panel $first.FrontendPid
        InitializeWindowCheck
        $wait=[Diagnostics.Stopwatch]::StartNew(); $window=[LaunchTestWindow]::Find($panel.Id)
        while(!$panel.HasExited -and $window -eq [IntPtr]::Zero -and $wait.ElapsedMilliseconds -lt 15000){Start-Sleep -Milliseconds 100;$window=[LaunchTestWindow]::Find($panel.Id)}
        if($window -eq [IntPtr]::Zero){throw 'Titled panel window was not created'}
        [void][LaunchTestWindow]::ShowWindowAsync($window,6)
        $minimize=[Diagnostics.Stopwatch]::StartNew()
        while(![LaunchTestWindow]::IsIconic($window) -and $minimize.ElapsedMilliseconds -lt 3000){Start-Sleep -Milliseconds 50}
        if(![LaunchTestWindow]::IsIconic($window)){throw 'Could not minimize test panel'}
        Request show | Out-Null
        $restore=[Diagnostics.Stopwatch]::StartNew()
        while([LaunchTestWindow]::IsIconic($window) -and $restore.ElapsedMilliseconds -lt 3000){Start-Sleep -Milliseconds 50}
        if([LaunchTestWindow]::IsIconic($window)){throw 'Repeated show did not restore minimized panel'}
        $panelOnly = @(Sample)
        $record | Add-Member -Force NoteProperty PanelOnly $panelOnly
        $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
        if(![LaunchTestWindow]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) -or !$panel.WaitForExit(10000)){throw 'Panel did not close normally'}
        $releaseWait = [Diagnostics.Stopwatch]::StartNew()
        do {
            $closedChildren = @($panelOnly | Where-Object { $p=Get-Process -Id $_.Pid -ErrorAction SilentlyContinue; !(IsBaseline $_) -and $p -and $p.StartTime.Ticks -eq $_.StartTicks })
            if ($closedChildren.Count) { Start-Sleep -Milliseconds 100 }
        } while ($closedChildren.Count -and $releaseWait.ElapsedMilliseconds -lt 10000)
        $record | Add-Member -Force NoteProperty FirstCloseRemaining $closedChildren
        $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
        if ($closedChildren.Count) { throw 'Closed panel left WebView2 children running' }
        $reopened=Request show
        if(!$reopened.FrontendPid -or $reopened.FrontendPid -eq $first.FrontendPid){throw 'Panel did not reopen'}
        $prompt=Request testShowPrompt; $repeat=Request testShowPrompt
        if(!$prompt.PromptPid -or $prompt.PromptPid -ne $repeat.PromptPid -or $prompt.FrontendPid -ne $reopened.FrontendPid){throw 'Prompt reuse or panel independence failed'}
        $record | Add-Member -Force NoteProperty Checks ([pscustomobject]@{RepeatedPanel=$first.FrontendPid;ReopenedPanel=$reopened.FrontendPid;Prompt=$prompt.PromptPid;Passed=5})
        $record.Checks
    } elseif($Command -eq 'Snapshot'){Request snapshot | Select-Object BackendPid,FrontendPid,PromptPid}
    elseif($Command -eq 'Sample'){$record | Add-Member -Force NoteProperty During (Sample);$record.During | Format-Table}
    elseif($Command -eq 'Idle') {
        $state = Request snapshot
        $beforeIdle = @(Sample)
        if ($state.PromptPid) { CloseWindow $state.PromptPid }
        if ($state.FrontendPid) { CloseWindow $state.FrontendPid }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        do {
            $remaining = @($beforeIdle | Where-Object { $p=Get-Process -Id $_.Pid -ErrorAction SilentlyContinue; !(IsBaseline $_) -and $p -and $p.StartTime.Ticks -eq $_.StartTicks })
            if ($remaining.Count) { Start-Sleep -Milliseconds 100 }
        } while ($remaining.Count -and $watch.ElapsedMilliseconds -lt 10000)
        $state = Request snapshot
        $record | Add-Member -Force NoteProperty AfterWindowsClosed @(Sample)
        $record | Add-Member -Force NoteProperty IdleRemaining $remaining
        $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
        if ($remaining.Count -or $state.FrontendPid -or $state.PromptPid -or @($record.AfterWindowsClosed | Where-Object { !(IsBaseline $_) }).Count) { throw 'UI processes remained after windows closed' }
        $record.AfterWindowsClosed | Format-Table
    }
    else {
        $record | Add-Member -Force NoteProperty BeforeClose (Sample)
        $stop=[Threading.EventWaitHandle]::OpenExisting("Local\AudioSwitch-LaunchTest-$($record.Token)")
        try{[void]$stop.Set()}finally{$stop.Dispose()}
        if(!$process.WaitForExit(10000)){throw 'Fixture did not exit'}
        $watch=[Diagnostics.Stopwatch]::StartNew()
        do {
            $remaining=@($record.BeforeClose | Where-Object {$p=Get-Process -Id $_.Pid -ErrorAction SilentlyContinue; $p -and $p.StartTime.Ticks -eq $_.StartTicks})
            if($remaining.Count){Start-Sleep -Milliseconds 200}
        }while($remaining.Count -and $watch.ElapsedMilliseconds -lt 10000)
        $record | Add-Member -Force NoteProperty Remaining $remaining
        $record | Add-Member -Force NoteProperty ConfigAfter (ConfigHash)
        $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
        if($remaining.Count -or $record.ConfigBefore -ne $record.ConfigAfter){throw 'Process or configuration check failed'}
        Write-Output "All $($record.BeforeClose.Count) fixture/UI child processes exited; configuration unchanged."
    }
}
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $recordPath -Encoding utf8
