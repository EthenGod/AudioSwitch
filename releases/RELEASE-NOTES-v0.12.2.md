# 声间 AudioSwitch v0.12.2 正式版

[![下载 AudioSwitch.exe](https://img.shields.io/badge/%E4%B8%8B%E8%BD%BD%20AudioSwitch.exe-2563EB?style=for-the-badge)](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.2/AudioSwitch.exe)

**将 EXE 下载到可写的新文件夹，保留文件名，双击运行。** 首次运行会将完整程序展开到旁边的 `AudioSwitch` 文件夹。升级前请先从托盘退出旧版，已有配置会保留。

[直接下载 EXE](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.2/AudioSwitch.exe) ｜ [下载完整 ZIP](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.2/AudioSwitch-Desktop-v0.12.2-win-x64.zip)

ZIP 请全部解压后再运行其中的 `AudioSwitch.exe`。桌面面板需要 Microsoft Edge WebView2。

## ✨ 更新内容

- 设备接入和断开时，右下角提示会显示当前设备。将鼠标移到操作按钮上即可预览切换结果。托盘右键菜单隐藏了“处理设备变化”。
- 检查更新支持选择仅接收正式版或同时接收测试版，发行说明支持 Markdown。文件检查会逐项显示结果。
- 游戏模式降低后台进程优先级，支持时启用效率模式，同时保持设备变化的响应速度。

## 🛠️ 修复内容

- 修复首次保存 Dolby 方案后，返回设备设置再保存时误报冲突的问题。
- 修复设备提示窗口展开、收起时正文抖动的问题。切换面板页面时，右侧滚动条长度会平滑过渡。

源码见本页 **Source code**。自有代码采用 GPL-3.0-only，第三方组件许可随包提供。
