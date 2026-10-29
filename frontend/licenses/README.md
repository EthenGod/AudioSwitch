# Rust 包缺失的许可原文

这些文件只补充 Cargo 包未携带的许可。`sources.json` 记录来源；GitHub 地址固定到包内 `.cargo_vcs_info.json` 的提交。`selectors` 使用 Mozilla 官方 MPL 2.0 原文，源码头声明与 Cargo 元数据均为 MPL-2.0。

版本映射在 `scripts/write-rust-notices.ps1` 中固定；依赖更新后如果缺少许可，打包停止，不能静默跳过。生成的归集还包含每个 crate 的完整版本、源码归档 URL 和包内 LICENSE／NOTICE／COPYRIGHT，包含 Windows 目标的构建依赖作为保守超集。

`WebView2-SDK-LICENSE.txt` 和 `WebView2-SDK-NOTICE.txt` 原样提取自微软官方 NuGet `Microsoft.Web.WebView2` 1.0.3800.47。版本取自 webview2-rs 提交 `edc2caf886175ccaebe86078c9cfe1ae2a187328` 的 `crates/update-bindings/src/main.rs`。该包 x64 `WebView2LoaderStatic.lib` 与本机锁定 crate 中的文件 SHA-256 一致：`89c6b872783b8f6c3cedbff618adb42082d455c615453ae10cfc753f1e8f25d8`。打包时再次核对。

来源：<https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3800.47/microsoft.web.webview2.1.0.3800.47.nupkg>。只保留许可文字，不把 SDK、静态库或系统浏览器文件作为额外运行文件复制到候选包。

Rust 标准库不是 Cargo 包依赖。打包时另从实际 `rustc --print sysroot` 对应的 `share/doc/rust/COPYRIGHT-library.html` 原样复制为 `THIRD-PARTY-RUST-STDLIB.html`；缺少该文件时停止打包。
