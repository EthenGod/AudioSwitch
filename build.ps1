param([switch]$Test, [string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x 编译器。' }
$output = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $output | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /main:AudioSwitch.Program "/win32manifest:$PSScriptRoot\src\app.manifest" "/out:$output\AudioSwitch.exe" @references @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\App.config') -Destination (Join-Path $output 'AudioSwitch.exe.config')
$vendorOutput = Join-Path $output 'vendor\svcl'
New-Item -ItemType Directory -Force -Path $vendorOutput | Out-Null
foreach ($vendorFile in @('svcl.exe', 'readme.txt', 'svcl.chm')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "vendor\svcl\$vendorFile") -Destination (Join-Path $vendorOutput $vendorFile)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY.md') -Destination (Join-Path $output 'THIRD_PARTY.md')
if ($Test) {
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /utf8output /codepage:65001 /main:AudioSwitch.Tests "/win32manifest:$PSScriptRoot\src\app.manifest" "/out:$output\AudioSwitch.Tests.exe" @references @sources (Join-Path $PSScriptRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    & (Join-Path $output 'AudioSwitch.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
}
Write-Host "已生成 $output\AudioSwitch.exe"
