param([Parameter(Mandatory=$true)][string]$SessionDirectory, [switch]$AllowStartupEffects)
# Explicitly authorized real-backend acceptance. Windows PowerShell only.
# This narrow test refuses startup routing changes or switching Dolby slots.
$ErrorActionPreference = 'Stop'
if (!$AllowStartupEffects) { throw 'Explicit authorization required; pass -AllowStartupEffects only after approving the live acceptance plan.' }
$directory = (Resolve-Path -LiteralPath $SessionDirectory).Path
$root = Split-Path (Split-Path $PSScriptRoot)
$staging = [IO.Path]::GetFullPath((Join-Path $root 'staging')) + [IO.Path]::DirectorySeparatorChar
if (!$directory.StartsWith($staging, [StringComparison]::OrdinalIgnoreCase)) { throw 'Session must be in project staging.' }
if (Test-Path -LiteralPath (Join-Path $directory 'live.before.json')) { throw 'Session already used; previous evidence is preserved.' }
if (Test-Path -LiteralPath (Join-Path $directory 'result.json')) { throw 'Session already has a result; choose a new directory.' }
$exe = Join-Path $directory 'desktop/AudioSwitch.exe'
$config = Join-Path $env:LOCALAPPDATA 'AudioSwitch/settings.json'
$audit = [ordered]@{ Started=$false; NormalExit=$false; RestoredDolby=$false; Error=$null; RecoveryError=$null }
$backend = $null
function SaveRecord($name, $value) { $value | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $directory $name) -Encoding UTF8 }
function AudioState($name) {
    & (Join-Path $PSScriptRoot 'live-state.ps1') -ExecutablePath $exe -OutputPath (Join-Path $directory $name) | Out-Null
    Get-Content -LiteralPath (Join-Path $directory $name) -Raw | ConvertFrom-Json
}
function Capture($name) {
    & (Join-Path $PSScriptRoot 'verify-dolby-capture.ps1') -ExecutablePath $exe -OutputPath (Join-Path $directory $name) | Out-Null
    $capture = Get-Content -LiteralPath (Join-Path $directory $name) -Raw | ConvertFrom-Json
    if (!$capture.Captured -or !$capture.ConfigurationUnchanged -or !$capture.DefaultsUnchanged -or $capture.Result.Error) { throw ('Capture failed: ' + $capture.Result.Error) }
    return $capture
}
function Equal($left,$right) { return ($left | ConvertTo-Json -Depth 35 -Compress) -ceq ($right | ConvertTo-Json -Depth 35 -Compress) }
function ReadSnapshot {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $pipe = New-Object IO.Pipes.NamedPipeClientStream('.',"AudioSwitch-$sid-$session",[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(2000)
        $writer=New-Object IO.StreamWriter($pipe); $writer.AutoFlush=$true
        $reader=New-Object IO.StreamReader($pipe)
        $writer.WriteLine('{"Action":"snapshot"}')
        $reply=$reader.ReadLineAsync(); if (!$reply.Wait(5000)) { throw 'Snapshot timed out.' }
        $state=$reply.Result | ConvertFrom-Json
        if ($state.BackendPid -ne $backend.Id -or $state.BackendExecutablePath -ne $exe) { throw 'Unexpected backend identity.' }
        return $state
    } finally { $pipe.Dispose() }
}
function SameBackend {
    $p=Get-Process -Id $backend.Id -ErrorAction SilentlyContinue
    return $p -and $p.Path -eq $exe -and $p.StartTime.Ticks -eq $backend.StartTime.Ticks
}
try {
    if (Get-Process AudioSwitch,AudioSwitch.Tests,audio-switch-panel -ErrorAction SilentlyContinue) { throw 'Existing application process.' }
    $check=Start-Process -FilePath $exe -ArgumentList '--check-files','--quiet' -WindowStyle Hidden -PassThru -Wait
    if ($check.ExitCode -ne 0) { throw 'Package integrity failed.' }
    $before=AudioState 'live.before.json'
    Copy-Item -LiteralPath $config -Destination (Join-Path $directory 'settings.live-before.json')
    $dolbyBefore=Capture 'dolby.live-before.json'
    $settings=($before.Configuration | ConvertFrom-Json).Settings
    if ($settings.AutoUpdateEnabled -or !$settings.UseDevicePriority -or !$settings.IncludeCommunications) { throw 'Startup settings differ from reviewed plan.' }
    if (@($before.Devices | Where-Object { $_.Issues.Count }).Count) { throw 'Incomplete audio baseline.' }
    foreach ($flow in 0,1) {
        $top=$settings.DeviceOrder | Where-Object { $_.Flow -eq $flow -and $_.Id -in $before.State.Devices.Id } | Select-Object -First 1
        foreach ($role in 0,1,2) { if (!$top -or $top.Id -ne $before.State.Defaults."${flow}:$role") { throw 'Startup may change routing; this narrow test must be re-planned.' } }
    }
    $profile=$settings.DeviceProfiles.($dolbyBefore.DeviceId).Dolby
    if (!$profile) { throw 'Expected configured Dolby profile is missing.' }
    foreach ($field in $profile.PSObject.Properties) {
        if ($null -ne $field.Value -and !(Equal $field.Value $dolbyBefore.Result.Profile.($field.Name))) { throw ('Saved Dolby differs from current state: ' + $field.Name) }
    }
    if ((Get-FileHash -LiteralPath $config).Hash -ne $before.ConfigurationHash) { throw 'Configuration changed during preparation.' }
    if (Get-Process AudioSwitch,AudioSwitch.Tests,audio-switch-panel -ErrorAction SilentlyContinue) { throw 'Application started during preparation.' }
    $oldDebug=$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
    try {
        $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=$null
        $backend=Start-Process -FilePath $exe -ArgumentList '--background' -WindowStyle Hidden -PassThru
    } finally { $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=$oldDebug }
    $audit.Started=$true
    SaveRecord 'session.json' ([pscustomobject]@{ Pid=$backend.Id; StartTicks=$backend.StartTime.Ticks; Executable=$exe; Panel=(Join-Path $directory 'desktop/audio-switch-panel.exe') })
    $timer=[Diagnostics.Stopwatch]::StartNew()
    do {
        if ($timer.Elapsed.TotalSeconds -gt 30) { throw 'Startup did not settle within 30 seconds.' }
        Start-Sleep -Milliseconds 300
        try { $snapshot=ReadSnapshot } catch { if ($timer.Elapsed.TotalSeconds -gt 15) { throw }; continue }
    } while (!$snapshot -or $snapshot.DolbyApplying)
    SaveRecord 'snapshot.started.json' $snapshot
    $started=AudioState 'live.started.json'
    $dolbyStarted=Capture 'dolby.started.json'
    $audit.StartupAudioUnchanged=(Equal $before.State.Defaults $started.State.Defaults) -and (Equal $before.Devices $started.Devices)
    $audit.StartupDolbyUnchanged=Equal $dolbyBefore.Result.Profile $dolbyStarted.Result.Profile
    if (!$audit.StartupAudioUnchanged -or !$audit.StartupDolbyUnchanged) { throw 'Unexpected startup audio change; stop inspection and verify recovery.' }
    SaveRecord 'ready.json' ([pscustomobject]$audit)
    Write-Output ('READY: real backend PID ' + $backend.Id + '; complete normal shutdown within 8 minutes.')
    $timer.Restart()
    while (SameBackend) {
        if ($timer.Elapsed.TotalMinutes -gt 8) { throw 'UI inspection timed out.' }
        Start-Sleep -Milliseconds 500
    }
    $audit.NormalExit=$true
} catch { $audit.Error=$_.Exception.Message }
finally {
    if ($audit.Started) {
        try {
            if (SameBackend) {
                # Emergency cleanup only; normal acceptance uses the backend cleanup path.
                $backend.Kill(); if (!$backend.WaitForExit(10000)) { throw 'Test backend did not exit.' }
            }
            Start-Sleep -Milliseconds 1500
            $after=AudioState 'live.after.json'
            $dolbyAfter=Capture 'dolby.after.json'
            $audit.AudioUnchanged=(Equal $before.State.Defaults $after.State.Defaults) -and (Equal $before.Devices $after.Devices)
            $audit.ConfigurationUnchanged=$before.ConfigurationHash -eq $after.ConfigurationHash
            $audit.StartupRegistrationUnchanged=Equal $before.StartupCommand $after.StartupCommand
            if (!(Equal $dolbyBefore.Result.Profile $dolbyAfter.Result.Profile)) {
                # Only undo an observed startup change that still has the same value;
                # never overwrite a change introduced later by the user or another app.
                if (!$dolbyStarted -or (Equal $dolbyBefore.Result.Profile $dolbyStarted.Result.Profile) -or
                    !(Equal $dolbyStarted.Result.Profile $dolbyAfter.Result.Profile) -or
                    $dolbyAfter.DeviceId -ne $dolbyBefore.DeviceId -or
                    $dolbyAfter.Result.Profile.MainProfile -ne $dolbyBefore.Result.Profile.MainProfile -or
                    $dolbyAfter.Result.Profile.SubProfile -ne $dolbyBefore.Result.Profile.SubProfile) { throw 'Dolby changed outside the recoverable startup scope; originals preserved, no blind restore.' }
                $start=New-Object Diagnostics.ProcessStartInfo($exe,'--dolby-worker')
                $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardInput=$true; $start.RedirectStandardOutput=$true
                $start.StandardOutputEncoding=New-Object Text.UTF8Encoding($false,$true)
                $worker=[Diagnostics.Process]::Start($start)
                try {
                    $output=$worker.StandardOutput.ReadToEndAsync()
                    $worker.StandardInput.WriteLine((@{ DeviceId=$dolbyBefore.DeviceId; Profile=$dolbyBefore.Result.Profile; OwnerPid=$PID } | ConvertTo-Json -Depth 10 -Compress)); $worker.StandardInput.Close()
                    if (!$worker.WaitForExit(15000) -or !$output.Wait(1000)) { throw 'Restore worker timeout.' }
                    $result=$output.Result | ConvertFrom-Json; SaveRecord 'dolby.restore-result.json' $result
                    if ($result.Error) { throw $result.Error }
                } finally { if (!$worker.HasExited) { $worker.Kill(); [void]$worker.WaitForExit(1500) }; $worker.Dispose() }
                $dolbyAfter=Capture 'dolby.restored.json'
                $audit.RestoredDolby=Equal $dolbyBefore.Result.Profile $dolbyAfter.Result.Profile
            }
            $audit.DolbyUnchanged=Equal $dolbyBefore.Result.Profile $dolbyAfter.Result.Profile
            if (!$audit.AudioUnchanged -or !$audit.ConfigurationUnchanged -or !$audit.StartupRegistrationUnchanged -or !$audit.DolbyUnchanged) { throw 'Final state differs; inspect saved originals. No blind configuration or routing restore.' }
        } catch { $audit.RecoveryError=$_.Exception.Message }
    }
    SaveRecord 'result.json' ([pscustomobject]$audit)
}
[pscustomobject]$audit | ConvertTo-Json -Depth 5
if ($audit.Error -or $audit.RecoveryError -or !$audit.Started -or !$audit.NormalExit) { exit 1 }
