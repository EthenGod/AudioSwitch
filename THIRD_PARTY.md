# 第三方组件

独立前端的设计参考、shadcn 组件来源及 npm / Tauri 许可说明见 [frontend/THIRD-PARTY.md](frontend/THIRD-PARTY.md)。本阶段未将这些依赖加入现有 Windows 发布包；前端生产构建会另附 npm 运行依赖的完整许可文字。Rust 桌面外壳目前仅本机构建验收，安装与更新分发仍留在第五阶段。

声间自有代码采用 `GPL-3.0-only`，见 `LICENSE` 和 `NOTICE.txt`。本文件单独标明的第三方组件仍遵守原作者许可，不能因附带于本仓库或发布包就按 GPL 重新授权。

本项目使用 Nir Sofer 的 **SoundVolumeCommandLine 1.28 x64**，仅在写入设备空间音效时短暂启动，不常驻、不安装服务。音量由 Windows Core Audio 直接设置，空间音效状态及可用格式由 Windows WinRT API 读取。

- 官方页面：https://www.nirsoft.net/utils/sound_volume_command_line.html
- 官方下载：https://www.nirsoft.net/utils/svcl-x64.zip
- 下载日期：2026-09-20
- `svcl.exe` SHA-256：`0C4738296D253495BC995DA7B5938E27B306C8F204DC9F7D4D5D455F1D8D3E38`
- 原始发行文件完整保留于 `vendor/svcl/`：`svcl.exe`、`svcl.chm`、`readme.txt`。

该组件为 freeware，原许可允许免费分发完整、未经修改的发行文件，但不允许收费或作为商业产品的一部分分发。完整许可见 `vendor/svcl/readme.txt`。含有该组件的发布包须遵守这项分发限制；商业分发前须另行取得许可或替换实现。该限制不改变用户对声间自有代码享有的 GPLv3 权利，也不把声间源码改为“仅限非商业使用”。

声间通过独立进程和 `/SetSpatial` 命令行参数调用 SVCL，不链接其代码。为离线修复和单 EXE 下载，程序资源中还保存了未经修改的 `svcl.exe`、`svcl.chm` 和 `readme.txt` 原始文件；运行时释放独立 EXE 后调用，不在声间进程中执行该组件。完整包和源码包仍保留 `vendor/svcl/` 原始文件。项目许可、声明和本说明也内嵌保存，使用 `AudioSwitch.exe --licenses` 可查看和复制完整许可文字。各文件仍遵守各自许可，内嵌存储不改变授权范围。参考 [GNU GPL FAQ：独立程序合集](https://www.gnu.org/licenses/gpl-faq.en.html#MereAggregation)。

Windows、.NET Framework 和 Dolby 驱动／Access 组件由系统或用户另行安装，保留各自许可；本项目不附带 Dolby DLL，不授予其分发或修改权限。对这些组件的调用适配不表示官方合作或授权。

空间音效写入后会用 Windows API 读取确认，不能仅凭工具退出成功就报告应用成功。第三方音效仍受驱动、系统、安装和授权状态约束。
