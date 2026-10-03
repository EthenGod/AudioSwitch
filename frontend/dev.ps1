param(
    [ValidateSet('install', 'dev', 'typecheck', 'test', 'build', 'preview', 'desktop:dev', 'desktop:build')]
    [string]$Command = 'dev',
    [string]$NodePath
)
$ErrorActionPreference = 'Stop'
# Use an existing modern runtime; never install or replace a global Node version.
$candidates = @()
if ($NodePath) { $candidates += $NodePath }
else {
    $existingNode = Get-Command node.exe -ErrorAction SilentlyContinue
    if ($existingNode) { $candidates += $existingNode.Source }
    $candidates += Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
}
$selectedNode = $null
foreach ($candidate in $candidates) {
    if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
    $reportedVersion = & $candidate --version
    if ($LASTEXITCODE -eq 0 -and [Version]$reportedVersion.TrimStart('v') -ge [Version]'22.12.0') {
        $selectedNode = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
}
if (!$selectedNode) { throw '需要 Node.js 22.12 或更新版本。可通过 -NodePath 指定已有的独立 node.exe，不需要更改全局安装。' }
$npmCommand = Get-Command npm.cmd -ErrorAction SilentlyContinue
$npmCandidates = @((Join-Path (Split-Path $selectedNode) 'node_modules\npm\bin\npm-cli.js'))
if ($npmCommand) { $npmCandidates += Join-Path (Split-Path $npmCommand.Source) 'node_modules\npm\bin\npm-cli.js' }
$npmCli = $npmCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (!$npmCli) { throw '没有找到 npm-cli.js，请使用包含 npm 的 Node 发行包。' }
$previousPath = $env:PATH
Push-Location $PSScriptRoot
try {
    $env:PATH = (Split-Path $selectedNode) + ';' + $previousPath
    Write-Host "Node: $selectedNode ($reportedVersion)"
    if ($Command -eq 'install') {
        & $selectedNode $npmCli install --cache ../staging/npm-cache --no-audit --no-fund
    } else {
        if (!(Test-Path -LiteralPath 'node_modules')) { throw '请先执行 .\frontend\dev.ps1 -Command install。' }
        & $selectedNode $npmCli run $Command
    }
    if ($LASTEXITCODE -ne 0) { throw "前端命令失败：$Command，退出码 $LASTEXITCODE" }
} finally {
    $env:PATH = $previousPath
    Pop-Location
}
