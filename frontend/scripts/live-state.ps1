param([Parameter(Mandatory=$true)][string]$OutputPath)
# Run with Windows PowerShell (.NET Framework). Read-only system baseline.
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Baseline already exists; choose a new explicit filename.' }
$root = Split-Path (Split-Path $PSScriptRoot)
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $root 'staging\AudioSwitch.exe'))
$audio = [Activator]::CreateInstance($assembly.GetType('AudioSwitch.AudioService'), $true)
try {
    $state = $audio.Read()
    $devices = @($state.Devices | ForEach-Object {
        $volume = $null; $spatial = $null; $issues = @()
        try { $volume = $audio.ReadVolume($_.Id) } catch { $issues += $_.Exception.Message }
        if ($_.Flow -eq 0) { try { $spatial = $audio.ReadSpatial($_.Id) } catch { $issues += $_.Exception.Message } }
        [pscustomobject]@{ Id=$_.Id; Name=$_.Name; Flow=$_.Flow; Volume=$volume; Spatial=$spatial; Issues=$issues }
    })
    $configurationPath = Join-Path $env:LOCALAPPDATA 'AudioSwitch\settings.json'
    $configuration = [IO.File]::ReadAllText($configurationPath)
    $settings = ($configuration | ConvertFrom-Json).Settings
    $startupKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try { $startup = if ($startupKey) { $startupKey.GetValue('AudioSwitch', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { $null } } finally { if ($startupKey) { $startupKey.Dispose() } }
    $record = [pscustomobject]@{ Captured=(Get-Date).ToString('o'); State=$state; Devices=$devices; Configuration=$configuration; ConfigurationHash=(Get-FileHash -LiteralPath $configurationPath -Algorithm SHA256).Hash; StartupCommand=$startup }
    $record | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
    [pscustomobject]@{ Devices=$devices.Count; Defaults=$state.Defaults; Priority=$settings.UseDevicePriority; GameMode=$settings.GameMode; AutomaticUpdates=$settings.AutoUpdateEnabled; Profiles=@($settings.DeviceProfiles.PSObject.Properties).Count; OutputDolbyConfigured=($null -ne $settings.DeviceProfiles.($state.Default(0,1)).Dolby); ReadIssues=@($devices | Where-Object { $_.Issues.Count }).Count; Saved=$OutputPath } | ConvertTo-Json -Depth 5
} finally { $audio.Dispose() }
