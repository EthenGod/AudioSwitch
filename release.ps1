# Copyright (C) 2026 EthenGod
# SPDX-License-Identifier: GPL-3.0-only
param([string]$OutputDirectory = 'releases')
$ErrorActionPreference = 'Stop'
$versionText = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\AppVersion.cs') -Raw
$match = [regex]::Match($versionText, 'Number = "([0-9]+\.[0-9]+\.[0-9]+)"')
if (!$match.Success) { throw '无法读取程序版本号。' }
$version = $match.Groups[1].Value
$outputRoot = Join-Path $PSScriptRoot $OutputDirectory
$destination = Join-Path $outputRoot "v$version"
if (Test-Path -LiteralPath $destination) { throw "版本目录已经存在，不会覆盖或清理：$destination。请更新版本号，或由你手动处理旧目录。" }
$buildDirectory = 'staging\release-' + [Guid]::NewGuid().ToString('N')
& (Join-Path $PSScriptRoot 'build.ps1') -Test -OutputDirectory $buildDirectory
$buildPath = Join-Path $PSScriptRoot $buildDirectory
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageName = "AudioSwitch-v$version-win-x64.zip"
$package = Join-Path $buildPath $packageName
$executable = Join-Path $buildPath 'AudioSwitch.exe'
$files = @('AudioSwitch.exe', 'AudioSwitch.exe.config', 'vendor/svcl/svcl.exe', 'vendor/svcl/readme.txt', 'vendor/svcl/svcl.chm', 'LICENSE', 'NOTICE.txt', 'THIRD_PARTY.md')
$archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $buildPath $file), $file) | Out-Null
    }
} finally { $archive.Dispose() }
# Keep the original eight-file ZIP contract for installed updaters.
$expectedExeHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    if ($archive.Entries.Count -ne $files.Count) { throw '发布包文件数量不正确。' }
    foreach ($name in $files) {
        if (@($archive.Entries | Where-Object { $_.FullName -eq $name }).Count -ne 1) { throw "发布包文件缺失或重复：$name" }
        $stream = $archive.GetEntry($name).Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($actualHash -ne (Get-FileHash -LiteralPath (Join-Path $buildPath $name) -Algorithm SHA256).Hash) { throw "发布包文件校验失败：$name" }
    }
} finally { $archive.Dispose() }
# Only validated upload assets go in the version folder; never overwrite a previous release.
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
New-Item -ItemType Directory -Path $destination | Out-Null
$publishedPackage = Join-Path $destination $packageName
$publishedExe = Join-Path $destination 'AudioSwitch.exe'
[IO.File]::Copy($package, $publishedPackage, $false)
[IO.File]::Copy($executable, $publishedExe, $false)
if ((Get-FileHash -LiteralPath $publishedExe -Algorithm SHA256).Hash -ne $expectedExeHash -or
    (Get-FileHash -LiteralPath $publishedPackage -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -or
    @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 2) { throw '发布目录校验失败，请勿上传其中的文件。' }
Write-Host "待上传目录：$destination"
Write-Host "完整程序包：$publishedPackage"
Write-Host "单 EXE：$publishedExe"
Write-Host "ZIP SHA-256：$((Get-FileHash -LiteralPath $publishedPackage -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Host "EXE SHA-256：$($expectedExeHash.ToLowerInvariant())"
Write-Host "请提交并推送全部对应源码，再创建 v$version 标签及正式 Release，只上传本目录的两个文件。源码使用 GitHub 自动生成的 Source code (zip/tar.gz)；不要遗漏未提交的构建必需文件。此脚本不会提交或发布。"
