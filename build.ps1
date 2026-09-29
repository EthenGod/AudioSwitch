# Copyright (C) 2026 EthenGod
# SPDX-License-Identifier: GPL-3.0-only
# This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
param([switch]$Test, [string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x 编译器。' }
$output = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
# Embed companion hashes; no extra distribution file is needed by older updaters.
$integrityFiles = @('AudioSwitch.exe.config', 'vendor/svcl/svcl.exe', 'vendor/svcl/readme.txt', 'vendor/svcl/svcl.chm', 'LICENSE', 'NOTICE.txt', 'THIRD_PARTY.md')
$integrityLines = foreach ($file in $integrityFiles) {
    $inputPath = Join-Path $PSScriptRoot $file
    if ($file -eq 'AudioSwitch.exe.config') { $inputPath = Join-Path $PSScriptRoot 'src/App.config' }
    '{0}|{1}|{2}' -f $file, (Get-Item -LiteralPath $inputPath).Length, (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
}
$selfHashMarker = 'AudioSwitch-Self-SHA256-v1='
$integrityLines += $selfHashMarker + ('0' * 64)
$integrityManifest = Join-Path $output 'integrity-manifest.txt'
[IO.File]::WriteAllLines($integrityManifest, [string[]]$integrityLines, [Text.UTF8Encoding]::new($false))
$integrityResource = '/resource:' + $integrityManifest + ',AudioSwitch.Integrity'
$repairResources = @("/resource:$PSScriptRoot\src\App.config,AudioSwitch.Repair.Config", "/resource:$PSScriptRoot\vendor\svcl\svcl.exe,AudioSwitch.Repair.Svcl")
# Preserve the complete original third-party distribution and notices in single-EXE downloads.
foreach ($file in @('vendor/svcl/readme.txt', 'vendor/svcl/svcl.chm', 'LICENSE', 'NOTICE.txt', 'THIRD_PARTY.md')) {
    $repairResources += "/resource:$PSScriptRoot\$file,AudioSwitch.Distribution.$file"
}
function Set-ExecutableIntegrity([string]$path) {
    # The digest occupies its own fixed 64-byte resource slot, excluded from hashing.
    # Every other byte of the final executable participates, including the companion manifest.
    $bytes = [IO.File]::ReadAllBytes($path)
    $ascii = [Text.Encoding]::ASCII.GetString($bytes)
    $markerPosition = $ascii.IndexOf($selfHashMarker, [StringComparison]::Ordinal)
    if ($markerPosition -lt 0 -or $ascii.IndexOf($selfHashMarker, $markerPosition + 1, [StringComparison]::Ordinal) -ge 0) { throw '程序完整性标记缺失或重复。' }
    $digestPosition = $markerPosition + $selfHashMarker.Length
    if ($ascii.Substring($digestPosition, 64) -ne ('0' * 64)) { throw '程序完整性校验槽无效。' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $sha.TransformBlock($bytes, 0, $digestPosition, $bytes, 0) | Out-Null
        $sha.TransformFinalBlock($bytes, $digestPosition + 64, $bytes.Length - $digestPosition - 64) | Out-Null
        $digest = [Text.Encoding]::ASCII.GetBytes([BitConverter]::ToString($sha.Hash).Replace('-', ''))
    } finally { $sha.Dispose() }
    [Array]::Copy($digest, 0, $bytes, $digestPosition, 64)
    [IO.File]::WriteAllBytes($path, $bytes)
}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /main:AudioSwitch.Program "/win32manifest:$PSScriptRoot\src\app.manifest" "/out:$output\AudioSwitch.exe" $integrityResource @repairResources @references @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Set-ExecutableIntegrity (Join-Path $output 'AudioSwitch.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\App.config') -Destination (Join-Path $output 'AudioSwitch.exe.config')
$vendorOutput = Join-Path $output 'vendor\svcl'
New-Item -ItemType Directory -Force -Path $vendorOutput | Out-Null
foreach ($vendorFile in @('svcl.exe', 'readme.txt', 'svcl.chm')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "vendor\svcl\$vendorFile") -Destination (Join-Path $vendorOutput $vendorFile)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY.md') -Destination (Join-Path $output 'THIRD_PARTY.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination (Join-Path $output 'LICENSE')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NOTICE.txt') -Destination (Join-Path $output 'NOTICE.txt')
if ($Test) {
    $testSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName)
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output /codepage:65001 /main:AudioSwitch.Tests "/win32manifest:$PSScriptRoot\src\app.manifest" "/out:$output\AudioSwitch.Tests.exe" $integrityResource @repairResources @references @sources @testSources
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    Set-ExecutableIntegrity (Join-Path $output 'AudioSwitch.Tests.exe')
    & (Join-Path $output 'AudioSwitch.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
}
Write-Host "已生成 $output\AudioSwitch.exe"
