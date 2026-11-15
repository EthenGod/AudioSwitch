# 声间 AudioSwitch v0.13.0 正式版

[简体中文](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.md) | [English](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.en.md) | [日本語](https://github.com/EthenGod/AudioSwitch/blob/master/releases/RELEASE-NOTES-v0.13.0.ja.md)

新增英语、日语和跟随系统语言，支持自定义设备名称。

[![下载 AudioSwitch.exe](https://img.shields.io/badge/%E4%B8%8B%E8%BD%BD%20AudioSwitch.exe-2563EB?style=for-the-badge)](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch.exe)

**将 EXE 下载到可写的新文件夹，保留文件名，双击运行。** 首次运行会展开到旁边的 `AudioSwitch` 文件夹，请保留整个文件夹。升级前先从托盘退出旧版，已有配置会保留。

[直接下载 EXE](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch.exe) ｜ [下载完整 ZIP](https://github.com/EthenGod/AudioSwitch/releases/download/v0.13.0/AudioSwitch-Desktop-v0.13.0-win-x64.zip)

ZIP 请全部解压后运行其中的 `AudioSwitch.exe`。桌面面板需要 Microsoft Edge WebView2。

## ✨ 更新内容

- 首次启动可选择简体中文、English、日本語或跟随系统。以后可在应用设置中更改；没有匹配的系统语言时使用中文。
- 设备设置支持自定义显示名称。本机保存过的名称可逐词补全，按 Tab 接受下一个词。
- 语言切换加入文字和布局过渡，报错窗口配色与主界面统一，并提供三语使用指南。
- 单 EXE 展开成功后只留下 `AudioSwitch` 文件夹，不再生成多余的压缩包目录。

## 🛠️ 修复内容

- 修复更新或修复任务取消期间仍可能接收后续操作的问题，避免重复提交。
- 修复旧配置中的名称补全缓存损坏时，导入设置失败的问题。
- 修复浏览器面板从本地端口根地址打开时，未正确进入连接流程的问题。

源码见本页 **Source code**。自有代码采用 GPL-3.0-only，第三方组件许可随包提供。
