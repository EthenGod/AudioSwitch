param([Parameter(Mandatory)][string]$MetadataPath, [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$metadata = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json
$licenseRoot = Join-Path (Split-Path $PSScriptRoot) 'licenses'
$supplement = @{'alloc-stdlib@0.3.0'='alloc-stdlib.txt';'defmt-parser@1.0.0'='defmt-parser.txt';'selectors@0.38.0'='selectors-MPL-2.0.txt';'webview2-com@0.39.1'='webview2-com.txt';'webview2-com-sys@0.39.1'='webview2-com.txt';'webview2-com-macros@0.8.1'='webview2-com-macros.txt'}
$sources = Get-Content -LiteralPath (Join-Path $licenseRoot 'sources.json') -Raw | ConvertFrom-Json
$text = [Text.StringBuilder]::new()
[void]$text.AppendLine("Rust Windows x64 dependency notices (including build-time dependencies).`nGenerated from locked offline Cargo metadata; unmodified upstream crates.`nSource archives: https://static.crates.io/crates/NAME/NAME-VERSION.crate`nThese sources retain their individual licenses. This notice does not replace their terms.`n")
$packages = @($metadata.packages | Where-Object { $_.id -in $metadata.resolve.nodes.id -and $_.name -ne 'audio-switch-panel' } | Sort-Object name,version)
foreach ($package in $packages) {
    $root = Split-Path $package.manifest_path
    [void]$text.AppendLine("`n---`n$($package.name) $($package.version)`nLicense: $($package.license)`nRepository: $($package.repository)`nSource: https://static.crates.io/crates/$($package.name)/$($package.name)-$($package.version).crate")
    $files = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $_.Name -match '^(LICEN[SC]E|COPYING|NOTICE|COPYRIGHT)([-_.]|$)' } | Sort-Object FullName)
    if ($package.license_file) {
        $declared = Join-Path $root $package.license_file
        if (!(Test-Path -LiteralPath $declared -PathType Leaf)) { throw "缺少声明许可：$($package.name)" }
        if ($declared -notin $files.FullName) { $files += Get-Item -LiteralPath $declared }
    }
    foreach ($file in $files) {
        [void]$text.AppendLine("`n$($file.FullName.Substring($root.Length + 1))`n" + [IO.File]::ReadAllText($file.FullName))
    }
    if (!$files.Count) {
        $key = "$($package.name)@$($package.version)"
        if (!$supplement.ContainsKey($key)) { throw "依赖没有许可原文，停止打包：$key" }
        $name = $supplement[$key]
        [void]$text.AppendLine("License source: $($sources.$name)`n" + [IO.File]::ReadAllText((Join-Path $licenseRoot $name)))
    }
}
# The crate contains Microsoft's static loader; retain its own license and notices too.
$sys = $packages | Where-Object { $_.name -eq 'webview2-com-sys' }
$loader = Join-Path (Split-Path $sys.manifest_path) 'x64/WebView2LoaderStatic.lib'
if ((Get-FileHash -LiteralPath $loader -Algorithm SHA256).Hash -ne '89C6B872783B8F6C3CEDBFF618ADB42082D455C615453AE10CFC753F1E8F25D8') { throw 'WebView2 Loader 已变化，需要重新核对 SDK 许可。' }
[void]$text.AppendLine("`n---`nMicrosoft.Web.WebView2 SDK 1.0.3800.47 (static loader)`nSource: https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3800.47/microsoft.web.webview2.1.0.3800.47.nupkg")
foreach ($name in @('WebView2-SDK-LICENSE.txt','WebView2-SDK-NOTICE.txt')) { [void]$text.AppendLine([IO.File]::ReadAllText((Join-Path $licenseRoot $name))) }
[IO.File]::WriteAllText($OutputPath, $text.ToString(), [Text.UTF8Encoding]::new($false))
Write-Host "已归集 $($packages.Count) 个 Rust 依赖及 WebView2 Loader 的许可。"
