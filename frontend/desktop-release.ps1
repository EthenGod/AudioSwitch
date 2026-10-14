# Copyright (C) 2026 EthenGod
# SPDX-License-Identifier: GPL-3.0-only
param([string]$OutputDirectory = 'releases', [string]$NodePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/candidate-common.ps1')
$root = Split-Path $PSScriptRoot
$version = Get-CandidateVersion
$outputRoot = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
$allowed = @('releases','staging') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $root $_)) }
if (!@($allowed | Where-Object { $outputRoot -eq $_ -or $outputRoot.StartsWith($_ + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) }).Count) { throw '双包输出只能放在项目 releases 或 staging 内。' }
$destination = Join-Path $outputRoot "v$version"
if (Test-Path -LiteralPath $destination) { throw "版本目录已存在，不覆盖：$destination" }
$ancestor = [IO.DirectoryInfo]::new($destination)
while ($ancestor) { if ($ancestor.Exists -and ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '输出路径不能包含链接。' }; $ancestor = $ancestor.Parent }

# Always compile from this checkout. The isolated candidate is verified before selecting release files.
$candidate = & (Join-Path $PSScriptRoot 'candidate-package.ps1') -NodePath $NodePath -TestBackend -PassThru
if (!$candidate.Payload -or $candidate.Version -cne $version) { throw '未收到本轮已验证构建。' }
$work = New-CandidateDirectory (Join-Path $root ('staging/desktop-release-' + [Guid]::NewGuid().ToString('N')))
$payload = New-CandidateDirectory (Join-Path $work 'payload')
$legacy = @($CandidateFiles[0..7])
$content = @($CandidateFiles[0..11]) + @('DESKTOP-README.txt')
foreach ($name in $content) {
    $source = if ($name -eq 'DESKTOP-README.txt') { Join-Path $PSScriptRoot 'desktop-readme.txt' } else { Join-Path $candidate.Payload $name }
    $target = Join-Path $payload $name
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy($source, $target, $false)
}
$rows = @($content | ForEach-Object { $path = Join-Path $payload $_; [ordered]@{ Path=$_; Length=(Get-Item -LiteralPath $path).Length; Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() } })
$manifest = [ordered]@{ Format='AudioSwitch.Desktop'; FormatVersion=1; AppVersion=$version; Platform='win-x64'; Files=$rows }
[IO.File]::WriteAllText((Join-Path $payload 'desktop-manifest.json'), ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
$assets = New-CandidateDirectory (Join-Path $work 'assets')
function Write-Package([string]$Name, [string[]]$Files) {
    $path = Join-Path $assets $Name
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $Files) {
            $entry = $zip.CreateEntry($file, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $input = [IO.File]::OpenRead((Join-Path $payload $file)); $output = $entry.Open()
            try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
Write-Package "AudioSwitch-v$version-win-x64.zip" $legacy
Write-Package "AudioSwitch-Desktop-v$version-win-x64.zip" (@($content) + @('desktop-manifest.json'))
[IO.File]::Copy((Join-Path $payload 'AudioSwitch.exe'), (Join-Path $assets 'AudioSwitch.exe'), $false)
# Use the production ZIP extraction/validation logic and isolated --check-files; never start TrayHost.
& (Join-Path $candidate.Backend 'AudioSwitch.Tests.exe') --release-archives $assets
if ($LASTEXITCODE -ne 0) { throw '双包真实解压与文件检查失败，不生成待上传目录。' }
$files = @(Get-ChildItem -LiteralPath $assets -File)
if ($files.Count -ne 3) { throw '待上传附件数量不正确。' }
[void][IO.Directory]::CreateDirectory($outputRoot)
New-Item -ItemType Directory -Path $destination -ErrorAction Stop | Out-Null
$hashes = @()
foreach ($file in $files) {
    $digest = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $target = Join-Path $destination $file.Name
    [IO.File]::Copy($file.FullName, $target, $false)
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $digest) { throw '复制后摘要不一致，请勿上传。' }
    $hashes += [ordered]@{ Name=$file.Name; Length=$file.Length; Sha256=$digest }
}
if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 3) { throw '发布目录包含意外文件，请勿上传。' }
[ordered]@{ Version=$version; Candidate=$candidate.Work; Payload=$payload; Destination=$destination; Assets=$hashes } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'verification.json') -Encoding UTF8
Write-Host "本地双包验证通过：$destination"
$hashes | ForEach-Object { Write-Host "$($_.Name) SHA-256 $($_.Sha256)" }
Write-Host "记录：$work/verification.json"
Write-Host '发布时必须同时保留旧名过渡 ZIP、新名 Desktop ZIP；单 EXE 仍是过渡后台。请先确认版本和完整源码，本脚本不提交、打标签或上传。'
