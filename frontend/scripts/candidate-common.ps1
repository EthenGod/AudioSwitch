# Candidate format is deliberately separate from the installed eight-file updater.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$CandidateFiles = @('AudioSwitch.exe','AudioSwitch.exe.config','vendor/svcl/svcl.exe','vendor/svcl/readme.txt','vendor/svcl/svcl.chm','LICENSE','NOTICE.txt','THIRD_PARTY.md','audio-switch-panel.exe','THIRD-PARTY-FRONTEND.txt','THIRD-PARTY-RUST.txt','THIRD-PARTY-RUST-STDLIB.html','CANDIDATE-README.txt')
$CandidateManifest = 'candidate-manifest.json'
function Get-CandidateVersion {
    $root = Split-Path (Split-Path $PSScriptRoot)
    $text = [IO.File]::ReadAllText((Join-Path $root 'src/AppVersion.cs'))
    $match = [regex]::Match($text, 'Number = "([0-9]+\.[0-9]+\.[0-9]+)"')
    if (!$match.Success) { throw '无法读取后台版本。' }
    return $match.Groups[1].Value
}
function New-CandidateDirectory([string]$Path) {
    $root = [IO.Path]::GetFullPath((Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'staging'))
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '候选包输出只能位于本项目 staging 内。' }
    # Reject existing output and reparse-point ancestors; never overwrite a prior artifact.
    if (Test-Path -LiteralPath $full) { throw "目录已存在，不覆盖：$full" }
    $ancestor = [IO.DirectoryInfo]::new([IO.Path]::GetDirectoryName($full))
    while ($ancestor) {
        if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '输出路径包含链接。' }
        $ancestor = $ancestor.Parent
    }
    [void][IO.Directory]::CreateDirectory($full)
    return $full
}
function Assert-CandidateExecutable([string]$Path, [string]$Version, [switch]$Backend) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5a4d) { throw '不是 Windows 程序。' }
        $stream.Position = 0x3c; $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $stream.Length - 26) { throw 'PE 头无效。' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw '程序不是 Windows x64。' }
    } finally { $reader.Dispose() }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ("$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)" -cne $Version) { throw "程序版本不匹配：$Path" }
    if ($Backend) {
        $assembly = [Reflection.AssemblyName]::GetAssemblyName($Path)
        if ($assembly.Name -cne 'AudioSwitch' -or $assembly.Version.ToString(3) -cne $Version) { throw '后台程序集不匹配。' }
    }
}
function Write-CandidateManifest([string]$Directory, [string]$Version) {
    $files = @($CandidateFiles | ForEach-Object {
        $path = Join-Path $Directory $_
        if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '不能打包链接。' }
        [ordered]@{ Path=$_; Length=(Get-Item -LiteralPath $path).Length; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ Format='AudioSwitch.UI.Candidate'; FormatVersion=1; AppVersion=$Version; Platform='win-x64'; Files=$files }
    [IO.File]::WriteAllText((Join-Path $Directory $CandidateManifest), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
}
function Compress-Candidate([string]$Directory, [string]$ArchivePath) {
    # Fixed order and ZIP dates make packaging the same input bytes reproducible.
    $file = [IO.File]::Open($ArchivePath, [IO.FileMode]::CreateNew)
    $zip = [IO.Compression.ZipArchive]::new($file, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in @($CandidateFiles) + @($CandidateManifest)) {
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $input = [IO.File]::OpenRead((Join-Path $Directory $name)); $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
        }
    } finally { $zip.Dispose(); $file.Dispose() }
}
function Test-CandidateArchive([string]$ArchivePath, [string]$Version, [string]$ExtractTo) {
    if ((Get-Item -LiteralPath $ArchivePath).Length -gt 200MB) { throw '候选包过大。' }
    # Keep this handle open through validation and extraction to prevent replacement in between.
    $zip = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $expected = @($CandidateFiles) + @($CandidateManifest)
        if ($zip.Entries.Count -ne $expected.Count) { throw '候选包文件数量错误。' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $total = 0L
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -cnotin $expected -or !$seen.Add($entry.FullName)) { throw "候选包路径无效或重复：$($entry.FullName)" }
            if ((($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000 -or ($entry.ExternalAttributes -band 0x400)) { throw '候选包不能包含链接。' }
            if ($entry.Length -le 0 -or $entry.Length -gt 64MB) { throw '候选包文件大小无效。' }
            $total += $entry.Length
        }
        if ($total -gt 200MB) { throw '候选包展开后过大。' }
        $entry = $zip.GetEntry($CandidateManifest)
        if ($entry.Length -gt 1MB) { throw '候选清单过大。' }
        $reader = [IO.StreamReader]::new($entry.Open(), [Text.UTF8Encoding]::new($false,$true), $false)
        try {
            $chars = [char[]]::new(1MB + 1)
            $count = $reader.ReadBlock($chars,0,$chars.Length)
            if ($count -gt 1MB) { throw '候选清单解压内容过大。' }
            $manifest = [string]::new($chars,0,$count) | ConvertFrom-Json
        } finally { $reader.Dispose() }
        if ($manifest.Format -cne 'AudioSwitch.UI.Candidate' -or ($manifest.FormatVersion -isnot [long] -and $manifest.FormatVersion -isnot [int]) -or $manifest.FormatVersion -ne 1 -or $manifest.AppVersion -cne $Version -or $manifest.Platform -cne 'win-x64') { throw '候选清单格式、版本或平台不匹配。' }
        if (@($manifest.Files).Count -ne $CandidateFiles.Count) { throw '候选清单文件数量错误。' }
        $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($item in $manifest.Files) {
            if ($item.Path -isnot [string] -or $item.Path -cnotin $CandidateFiles -or !$listed.Add($item.Path)) { throw '候选清单路径无效或重复。' }
            if (($item.Length -isnot [long] -and $item.Length -isnot [int]) -or $item.Length -le 0 -or $item.Length -gt 64MB -or $item.Sha256 -isnot [string] -or $item.Sha256 -cnotmatch '^[0-9a-f]{64}$') { throw '候选清单大小或摘要无效。' }
            $entry = $zip.GetEntry($item.Path)
            if ($entry.Length -ne $item.Length) { throw "文件长度不匹配：$($item.Path)" }
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $buffer = [byte[]]::new(65536); $readTotal = 0L
                while (($count = $stream.Read($buffer,0,$buffer.Length)) -gt 0) {
                    $readTotal += $count
                    if ($readTotal -gt $item.Length) { throw '解压内容超过声明大小。' }
                    [void]$sha.TransformBlock($buffer,0,$count,$buffer,0)
                }
                [void]$sha.TransformFinalBlock($buffer,0,0)
                if ($readTotal -ne $item.Length) { throw '解压内容不足声明大小。' }
                $hash = [BitConverter]::ToString($sha.Hash).Replace('-','').ToLowerInvariant()
            } finally { $sha.Dispose(); $stream.Dispose() }
            if ($hash -cne $item.Sha256) { throw "文件摘要不匹配：$($item.Path)" }
        }
        if ($ExtractTo) {
            $destination = New-CandidateDirectory $ExtractTo
            foreach ($entry in $zip.Entries) {
                $path = Join-Path $destination $entry.FullName
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $false)
            }
            Assert-CandidateExecutable (Join-Path $destination 'AudioSwitch.exe') $Version -Backend
            Assert-CandidateExecutable (Join-Path $destination 'audio-switch-panel.exe') $Version
        }
        return [pscustomobject]@{Version=$Version;Files=$zip.Entries.Count;ExpandedBytes=$total;Sha256=(Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash;ExtractedTo=$ExtractTo}
    } finally { $zip.Dispose() }
}
