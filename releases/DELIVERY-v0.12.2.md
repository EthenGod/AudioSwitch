# v0.12.2 正式版交付记录

2026-11-02 本地打包。发布附件取自 `releases/v0.12.2/`。发行说明在 [RELEASE-NOTES-v0.12.2.md](RELEASE-NOTES-v0.12.2.md)。

| 附件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| AudioSwitch.exe | 3739136 | `1c311366aa030a28ea2b47646d354ac786c407eb1fc615c53a68e9c6e7b261ef` |
| AudioSwitch-Desktop-v0.12.2-win-x64.zip | 3117806 | `61e15eef74b45b7e167666242b8fede35990e10c2ddc78f7017ab0281d875290` |

基于 master 的 `13577a1`，包含工作区的版本更新和托盘菜单调整。打包脚本重新编译了后台和面板，两个程序的产品版本均为 0.12.2。

- 后台 858 项、前端 161 项、Rust 46 项测试通过。Rust 跳过 3 项可选测试。
- EXE 展开的 14 个文件与 ZIP 逐字节一致。完整性检查通过，校验期间真实用户配置未变。
- 托盘菜单回归覆盖隐藏“处理设备变化”。本轮未进行实际插拔和线上升级测试。

构建日志在 `staging/release-0.12.2-final.log`，附件校验记录在 `staging/desktop-release-694eecb5f2d548e0ac8f704d99d63b7a/verification.json`。
