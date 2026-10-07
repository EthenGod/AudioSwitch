# Windows PowerShell only: read current state and run the original capture-only worker.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot)
$exe = Join-Path $root 'staging/AudioSwitch.exe'
$configuration = Join-Path $env:LOCALAPPDATA 'AudioSwitch/settings.json'
function ConfigurationHash { if (Test-Path -LiteralPath $configuration) { (Get-FileHash -LiteralPath $configuration -Algorithm SHA256).Hash } }
$before = ConfigurationHash
$assembly = [Reflection.Assembly]::LoadFrom($exe)
$audio = [Activator]::CreateInstance($assembly.GetType('AudioSwitch.AudioService'), $true)
try {
    $state = $audio.Read(); $id = $state.Default(0,1)
    if (!$id) { throw 'No current output; no worker was started.' }
    $start = New-Object Diagnostics.ProcessStartInfo($exe, '--dolby-worker')
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.RedirectStandardInput=$true; $start.RedirectStandardOutput=$true
    $start.StandardOutputEncoding=New-Object Text.UTF8Encoding($false,$true)
    $process=[Diagnostics.Process]::Start($start)
    try {
        $output=$process.StandardOutput.ReadToEndAsync()
        $process.StandardInput.WriteLine((@{ DeviceId=$id; Profile=$null; OwnerPid=$PID } | ConvertTo-Json -Compress)); $process.StandardInput.Close()
        if (!$process.WaitForExit(15000) -or !$output.Wait(1000)) { throw 'Capture timed out.' }
        $result=$output.Result | ConvertFrom-Json
        if (!$result.Error -and !$result.Profile) { throw 'Capture result is incomplete.' }
        $afterState=$audio.Read(); $after=ConfigurationHash
        $sameDefaults=($state.Defaults | ConvertTo-Json -Compress) -eq ($afterState.Defaults | ConvertTo-Json -Compress)
        $record=[PSCustomObject]@{ Pid=$process.Id; Exited=$process.HasExited; ConfigurationUnchanged=($before -eq $after); DefaultsUnchanged=$sameDefaults; Captured=($null -ne $result.Profile); Result=$result }
        $record | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $root 'staging/stage44-readonly-capture.json') -Encoding UTF8
        $record | Select-Object Pid,Exited,ConfigurationUnchanged,DefaultsUnchanged,Captured | Format-List
        if ($result.Error) { Write-Output ('Capture reported: '+$result.Error) }
        if ($before -ne $after -or !$sameDefaults) { throw 'System state changed during read-only capture; inspect before continuing.' }
    } finally { if (!$process.HasExited) { $process.Kill(); [void]$process.WaitForExit(1500) }; $process.Dispose() }
} finally { $audio.Dispose() }
