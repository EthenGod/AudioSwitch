param([Parameter(Mandatory)][string]$ArchivePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'candidate-common.ps1')
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$version = Get-CandidateVersion
$root = Split-Path (Split-Path $PSScriptRoot)
$work = New-CandidateDirectory (Join-Path $root ('staging/candidate-tests-' + [Guid]::NewGuid().ToString('N')))
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Name) { if (!$Condition) { throw "FAIL: $Name" }; $checks.Add($Name); Write-Host "OK $Name" }
function Reject([scriptblock]$Action, [string]$Name) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Check $rejected $Name
}
$valid = Test-CandidateArchive $ArchivePath $version (Join-Path $work '中文 & (候选)/verified')
Check ($valid.Files -eq 14) 'complete 14-file package extracts under a Chinese path'
$extracted = $valid.ExtractedTo
Reject { Test-CandidateArchive $ArchivePath $version $extracted } 'existing output is never overwritten'
Reject { Test-CandidateArchive $ArchivePath $version (Join-Path $root 'candidate-escape') } 'extraction outside staging is rejected'
Reject { Assert-CandidateExecutable (Join-Path $extracted 'audio-switch-panel.exe') '99.0.0' } 'actual panel binary version mismatch is rejected'
Reject { Assert-CandidateExecutable (Join-Path $extracted 'AudioSwitch.exe') '99.0.0' -Backend } 'actual backend binary version mismatch is rejected'
$before = (Get-FileHash -LiteralPath $ArchivePath).Hash
Compress-Candidate $extracted (Join-Path $work 'repacked.zip')
Check ((Get-FileHash -LiteralPath (Join-Path $work 'repacked.zip')).Hash -eq $before) 'same bytes produce identical ZIP ordering timestamps and digest'
Reject { Compress-Candidate $extracted (Join-Path $work 'repacked.zip') } 'existing ZIP is never overwritten'
Check ((Get-FileHash -LiteralPath (Join-Path $work 'repacked.zip')).Hash -eq $before) 'refused overwrite leaves ZIP unchanged'

$modes = @('missing','extra','duplicate','traversal','absolute','backslash','ads','case','symlink','hash','length','format','version','platform','manifest-duplicate','manifest-path','manifest-size-type','manifest-hash-type','manifest-too-large','file-too-large','damaged-data','truncated')
foreach ($mode in $modes) {
    $path = Join-Path $work "$mode.zip"
    $source = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    $target = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($original in $source.Entries) {
            if ($mode -eq 'missing' -and $original.FullName -eq 'LICENSE') { continue }
            $name = $original.FullName
            if ($name -eq 'LICENSE') {
                switch ($mode) {
                    'duplicate' { $name = 'NOTICE.txt' }
                    'traversal' { $name = '../escape.txt' }
                    'absolute' { $name = 'C:/escape.txt' }
                    'backslash' { $name = 'vendor\svcl\escape.txt' }
                    'ads' { $name = 'LICENSE:stream' }
                    'case' { $name = 'license' }
                }
            }
            $entry = $target.CreateEntry($name)
            if ($mode -eq 'symlink' -and $name -eq 'LICENSE') { $entry.ExternalAttributes = -1577123840 }
            $input = $original.Open(); $output = $entry.Open()
            try {
                if ($name -eq $CandidateManifest -and $mode -in @('hash','length','format','version','platform','manifest-duplicate','manifest-path','manifest-size-type','manifest-hash-type')) {
                    $reader = [IO.StreamReader]::new($input)
                    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
                    switch ($mode) {
                        'hash' { $manifest.Files[0].Sha256 = '0' * 64 }
                        'length' { $manifest.Files[0].Length++ }
                        'format' { $manifest.FormatVersion = 2 }
                        'version' { $manifest.AppVersion = '99.0.0' }
                        'platform' { $manifest.Platform = 'win-arm64' }
                        'manifest-duplicate' { $manifest.Files[1].Path = $manifest.Files[0].Path }
                        'manifest-path' { $manifest.Files[0].Path = '../AudioSwitch.exe' }
                        'manifest-size-type' { $manifest.Files[0].Length = "$($manifest.Files[0].Length)" }
                        'manifest-hash-type' { $manifest.Files[0].Sha256 = 42 }
                    }
                    $bytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 6)); $output.Write($bytes,0,$bytes.Length)
                } elseif ($mode -eq 'manifest-too-large' -and $name -eq $CandidateManifest) {
                    $bytes = [byte[]]::new(1MB + 1); $output.Write($bytes,0,$bytes.Length)
                } elseif ($mode -eq 'file-too-large' -and $name -eq 'LICENSE') {
                    $bytes = [byte[]]::new(1MB); for ($i=0; $i -lt 65; $i++) { $output.Write($bytes,0,$bytes.Length) }
                } elseif ($mode -eq 'damaged-data' -and $name -eq 'LICENSE') {
                    $bytes = [byte[]]::new([int]$original.Length); $output.Write($bytes,0,$bytes.Length)
                } else { $input.CopyTo($output) }
            } finally { $output.Dispose(); $input.Dispose() }
        }
        if ($mode -eq 'extra') { $stream = $target.CreateEntry('settings.json').Open(); try { $stream.WriteByte(123) } finally { $stream.Dispose() } }
    } finally { $target.Dispose(); $source.Dispose() }
    if ($mode -eq 'truncated') { $stream=[IO.File]::OpenWrite($path); try { $stream.SetLength(20) } finally { $stream.Dispose() } }
    $destination = Join-Path $work "rejected-$mode"
    Reject { Test-CandidateArchive $path $version $destination } "reject $mode package"
    Check (!(Test-Path -LiteralPath $destination)) "$mode rejected before extraction"
}
[pscustomobject]@{Passed=$checks.Count;Checks=$checks;Archive=$ArchivePath;Output=$work} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'results.json') -Encoding utf8
Write-Host "PASS: $($checks.Count) candidate checks. $work"
