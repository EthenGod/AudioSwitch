# Copyright (C) 2026 EthenGod
# SPDX-License-Identifier: GPL-3.0-only
param([string]$OutputDirectory = 'artifacts')
$ErrorActionPreference = 'Stop'
$versionText = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\AppVersion.cs') -Raw
$match = [regex]::Match($versionText, 'Number = "([0-9]+\.[0-9]+\.[0-9]+)"')
if (!$match.Success) { throw '无法读取程序版本号。' }
$version = $match.Groups[1].Value
$destination = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$package = Join-Path $destination "AudioSwitch-v$version-win-x64.zip"
$source = Join-Path $destination "AudioSwitch-v$version-source.zip"
if ((Test-Path -LiteralPath $package) -or (Test-Path -LiteralPath $source)) { throw '同名发布包已经存在。请换一个输出目录，或由你手动处理旧包。' }
$buildDirectory = 'staging\release-' + [Guid]::NewGuid().ToString('N')
& (Join-Path $PSScriptRoot 'build.ps1') -Test -OutputDirectory $buildDirectory
$buildPath = Join-Path $PSScriptRoot $buildDirectory
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files = @('AudioSwitch.exe', 'AudioSwitch.exe.config', 'vendor/svcl/svcl.exe', 'vendor/svcl/readme.txt', 'vendor/svcl/svcl.chm', 'LICENSE', 'NOTICE.txt', 'THIRD_PARTY.md')
$archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $buildPath $file), $file) | Out-Null
    }
} finally { $archive.Dispose() }
# Explicit source roots prevent including user settings, diagnostics, or local Dolby DLLs.
$sourceFiles = @('build.ps1', 'release.ps1', '.gitignore', 'LICENSE', 'NOTICE.txt', 'THIRD_PARTY.md', 'README.md', 'CONFIGURATION.md', 'MANUAL-TESTS.md', 'DOLBY-EQ.md', 'CODE_REVIEW.md', 'AGENTS.md')
foreach ($root in @('src', 'tests', 'vendor\svcl')) {
    $sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $root) -File -Recurse | ForEach-Object { $_.FullName.Substring($PSScriptRoot.Length + 1).Replace('\', '/') })
}
$archive = [IO.Compression.ZipFile]::Open($source, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $sourceFiles) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $PSScriptRoot $file), $file) | Out-Null
    }
} finally { $archive.Dispose() }
Write-Host "程序包：$package"
Write-Host "完整源码：$source"
Write-Host "SHA-256：$((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Host "请在提交并推送对应源码后，创建 v$version 标签和正式 Release，同时上传这两个附件。此脚本不会提交或发布。"
