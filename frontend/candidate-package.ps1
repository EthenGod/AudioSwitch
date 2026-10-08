param([string]$NodePath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts/candidate-common.ps1')
$root = Split-Path $PSScriptRoot
$version = Get-CandidateVersion
$npm = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw | ConvertFrom-Json
$tauri = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src-tauri/tauri.conf.json') -Raw | ConvertFrom-Json
$cargo = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src-tauri/Cargo.toml') -Raw
$cargoVersion = [regex]::Match($cargo,'(?m)^version = "([0-9.]+)"').Groups[1].Value
if ($npm.version -cne $version -or $tauri.version -cne $version -or $cargoVersion -cne $version) { throw '前后端源码版本不一致，停止打包。' }
$work = New-CandidateDirectory (Join-Path $root ('staging/ui-candidate-' + [Guid]::NewGuid().ToString('N')))
$buildRelative = 'staging/' + (Split-Path $work -Leaf) + '/backend'
& (Join-Path $root 'build.ps1') -OutputDirectory $buildRelative
& (Join-Path $PSScriptRoot 'desktop.ps1') -Command build -NodePath $NodePath
$payload = New-CandidateDirectory (Join-Path $work 'payload')
foreach ($name in $CandidateFiles[0..7]) {
    $target = Join-Path $payload $name
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy((Join-Path $work "backend/$name"), $target, $false)
}
[IO.File]::Copy((Join-Path $root 'staging/ui-target/release/audio-switch-panel.exe'), (Join-Path $payload 'audio-switch-panel.exe'), $false)
[IO.File]::Copy((Join-Path $PSScriptRoot 'dist-desktop/THIRD-PARTY-NOTICES.txt'), (Join-Path $payload 'THIRD-PARTY-FRONTEND.txt'), $false)
$previousCargo = $env:CARGO_HOME; $previousRust = $env:RUSTUP_HOME; $previousPath = $env:PATH; $previousTarget = $env:CARGO_TARGET_DIR
try {
    $env:CARGO_TARGET_DIR = Join-Path $root 'staging/ui-target'
    $localCargo = Join-Path $root 'staging/ui-toolchain/cargo'
    if (Test-Path -LiteralPath (Join-Path $localCargo 'bin/cargo.exe')) {
        $env:CARGO_HOME = $localCargo; $env:RUSTUP_HOME = Join-Path $root 'staging/ui-toolchain/rustup'
        $env:PATH = (Join-Path $localCargo 'bin') + ';' + $env:PATH
    }
    $metadata = & cargo metadata --offline --locked --format-version 1 --filter-platform x86_64-pc-windows-msvc --manifest-path (Join-Path $PSScriptRoot 'src-tauri/Cargo.toml')
    if ($LASTEXITCODE -ne 0) { throw '离线 Cargo 许可清单失败。' }
    $metadataPath = Join-Path $work 'cargo-metadata.json'
    [IO.File]::WriteAllText($metadataPath, ($metadata -join "`n"), [Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'scripts/write-rust-notices.ps1') -MetadataPath $metadataPath -OutputPath (Join-Path $payload 'THIRD-PARTY-RUST.txt')
    $sysroot = & rustc --print sysroot
    if ($LASTEXITCODE -ne 0) { throw '无法读取当前 Rust 标准库许可位置。' }
    [IO.File]::Copy((Join-Path $sysroot 'share/doc/rust/COPYRIGHT-library.html'), (Join-Path $payload 'THIRD-PARTY-RUST-STDLIB.html'), $false)
} finally { $env:CARGO_HOME = $previousCargo; $env:RUSTUP_HOME = $previousRust; $env:PATH = $previousPath; $env:CARGO_TARGET_DIR = $previousTarget }
[IO.File]::Copy((Join-Path $PSScriptRoot 'candidate-readme.txt'), (Join-Path $payload 'CANDIDATE-README.txt'), $false)
Assert-CandidateExecutable (Join-Path $payload 'AudioSwitch.exe') $version -Backend
Assert-CandidateExecutable (Join-Path $payload 'audio-switch-panel.exe') $version
Write-CandidateManifest $payload $version
$archive = Join-Path $work "AudioSwitch-UI-CANDIDATE-v$version-win-x64.zip"
Compress-Candidate $payload $archive
$result = Test-CandidateArchive $archive $version (Join-Path $work 'verified')
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'verification.json') -Encoding utf8
Write-Host "独立候选包（不供旧更新器安装）：$archive"
Write-Host "验证目录：$work"
Write-Host "SHA-256：$($result.Sha256)"
