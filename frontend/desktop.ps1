param(
    [ValidateSet('dev', 'build', 'test', 'check')]
    [string]$Command = 'dev',
    [string]$NodePath
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
$previousEnvironment = @{}
foreach ($variable in @('PATH', 'CARGO_HOME', 'RUSTUP_HOME', 'CARGO_TARGET_DIR')) {
    $previousEnvironment[$variable] = [Environment]::GetEnvironmentVariable($variable, 'Process')
}
try {
    $localTools = Join-Path $projectRoot 'staging\ui-toolchain'
    if (Test-Path -LiteralPath (Join-Path $localTools 'cargo\bin\cargo.exe')) {
        $env:CARGO_HOME = Join-Path $localTools 'cargo'
        $env:RUSTUP_HOME = Join-Path $localTools 'rustup'
        $env:PATH = (Join-Path $env:CARGO_HOME 'bin') + ';' + $env:PATH
    }
    if (!(Get-Command cargo.exe -ErrorAction SilentlyContinue)) { throw '请先安装 Rust MSVC 工具链。参见 frontend/STAGE-2.md。' }
    $env:CARGO_TARGET_DIR = Join-Path $projectRoot 'staging\ui-target'
    if ($Command -eq 'test' -or $Command -eq 'check') {
        & cargo $Command --manifest-path (Join-Path $PSScriptRoot 'src-tauri\Cargo.toml') --locked
        if ($LASTEXITCODE -ne 0) { throw "Rust $Command 失败：$LASTEXITCODE" }
    } else {
        & (Join-Path $PSScriptRoot 'dev.ps1') -Command "desktop:$Command" -NodePath $NodePath
    }
} finally {
    foreach ($variable in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($variable, $previousEnvironment[$variable], 'Process') }
}
