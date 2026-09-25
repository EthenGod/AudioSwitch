# 第三方组件

本项目使用 Nir Sofer 的 **SoundVolumeCommandLine 1.28 x64**，仅在写入设备空间音效时短暂启动，不常驻、不安装服务。音量由 Windows Core Audio 直接设置，空间音效状态及可用格式由 Windows WinRT API 读取。

- 官方页面：https://www.nirsoft.net/utils/sound_volume_command_line.html
- 官方下载：https://www.nirsoft.net/utils/svcl-x64.zip
- 下载日期：2026-09-20
- `svcl.exe` SHA-256：`0C4738296D253495BC995DA7B5938E27B306C8F204DC9F7D4D5D455F1D8D3E38`
- 原始发行文件完整保留于 `vendor/svcl/`：`svcl.exe`、`svcl.chm`、`readme.txt`。

该组件为 freeware，原许可允许免费分发完整、未经修改的发行文件，但不允许收费或作为商业产品的一部分分发。完整许可见 `vendor/svcl/readme.txt`。因此当前包含该组件的构建用于个人／非商业用途；商业分发前须另行处理授权或替换实现。

空间音效写入后会用 Windows API 读取确认，不能仅凭工具退出成功就报告应用成功。第三方音效仍受驱动、系统、安装和授权状态约束。
