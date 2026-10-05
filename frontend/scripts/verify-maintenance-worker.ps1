param([switch]$Online)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot)
$executable = Join-Path $projectRoot 'staging/AudioSwitch.exe'
$settings = Join-Path $env:LOCALAPPDATA 'AudioSwitch/settings.json'
$before = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { $null }
$results = @()
$kinds = if ($Online) { @('files', 'update') } else { @('files') }
foreach ($kind in $kinds) {
    $start = [Diagnostics.ProcessStartInfo]::new($executable, '--panel-maintenance')
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $process.StandardInput.WriteLine($kind); $process.StandardInput.Flush()
        if (!$process.WaitForExit(35000) -or !$output.Wait(1000)) { throw 'Read-only worker did not finish in time.' }
        $result = $output.Result | ConvertFrom-Json
        if ($result.Kind -ne $kind) { throw 'Worker returned the wrong task kind.' }
        if ($kind -eq 'files' -and $result.Status -ne 'passed') { throw ('File check failed: ' + $result.Message) }
        $results += [PSCustomObject]@{ Pid=$process.Id; Exited=$process.HasExited; Result=$result }
    } finally {
        if (!$process.HasExited) {
            $process.StandardInput.Close()
            if (!$process.WaitForExit(2500)) { $process.Kill(); [void]$process.WaitForExit(1000) }
        }
        $process.Dispose()
    }
}
$after = if (Test-Path -LiteralPath $settings) { (Get-FileHash -LiteralPath $settings).Hash } else { $null }
$record = [PSCustomObject]@{ Online=[bool]$Online; ConfigurationUnchanged=($before -eq $after); Checks=$results }
$record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $projectRoot 'staging/stage42-live-worker.json') -Encoding utf8
$results | ForEach-Object { [PSCustomObject]@{ Pid=$_.Pid; Exited=$_.Exited; Kind=$_.Result.Kind; Status=$_.Result.Status; Version=$_.Result.LatestVersion; Message=$_.Result.Message } } | Format-Table -Wrap
if ($before -ne $after) { throw 'Configuration hash changed during read-only checks; inspect before continuing.' }
if ($results | Where-Object { $_.Result.Status -eq 'error' -or $_.Result.Status -eq 'cancelled' }) { throw 'A read-only check did not complete; see staging/stage42-live-worker.json.' }
Write-Output 'PASS: read-only workers exited; active configuration hash unchanged.'
