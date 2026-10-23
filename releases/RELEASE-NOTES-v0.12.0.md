# 声间 AudioSwitch v0.12.0

> 待发布稿：上传附件并正式发布后，下面的下载按钮才可用。

新版界面来了，原有设备排序、音量和音效设置继续保留。

[![下载 AudioSwitch.exe](https://img.shields.io/badge/%E4%B8%8B%E8%BD%BD%20AudioSwitch.exe-2563EB?style=for-the-badge)](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.0/AudioSwitch.exe)

**下载后放到可写文件夹，保留 `AudioSwitch.exe` 文件名，双击运行。** [按钮不显示？点这里下载](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.0/AudioSwitch.exe)。

注意：本版单独的 EXE 是过渡版，**不包含新界面**，运行后可通过“检查更新”继续安装新界面。想一步到位，请点下面的按钮：

[![直接使用新界面](https://img.shields.io/badge/%E7%9B%B4%E6%8E%A5%E4%BD%BF%E7%94%A8%E6%96%B0%E7%95%8C%E9%9D%A2-64748B?style=for-the-badge)](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.0/AudioSwitch-Desktop-v0.12.0-win-x64.zip)

[完整界面包](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.0/AudioSwitch-Desktop-v0.12.0-win-x64.zip)下载后，**完整解压**，再运行里面的 `AudioSwitch.exe`。适用于 Windows 10 / 11 64 位，需要 .NET Framework 4.8；若提示缺少 WebView2，请按提示安装后重试。

## ✨ 更新内容

- 全新桌面界面：声音设备、自动切换和应用设置分为三个页面，支持深浅色。
- 在设备设置中统一调整音量、空间音效、免打扰规则和 Dolby 方案。
- 导入配置前可先预览，替换前自动备份，方便恢复原设置。

## 🛠️ 修复内容

- 修复导入无效备份时，错误提示可能带出配置原文的问题；现在会提示重新选择文件。

## ⚠️ 已知问题

- 部分设备或 Dolby 组件版本仍不支持 Dolby 方案，可继续使用基础设备切换功能。

**旧版用户：** v0.11.x 需先通过“检查更新”安装过渡版，再检查一次更新安装新界面；也可退出旧版托盘后，直接使用上面的完整界面包。原有设置继续保留。线上连续升级尚待发布后验证，其他未验证范围见[交付记录](https://github.com/EthenGod/AudioSwitch/blob/v0.12.0/releases/DELIVERY-v0.12.0.md)。

源码在本页 **Source code** 中，普通使用无需下载。自有代码采用 GPL-3.0-only，第三方组件许可见包内说明。
