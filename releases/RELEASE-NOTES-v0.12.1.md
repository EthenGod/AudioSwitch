# 声间 AudioSwitch v0.12.1 测试版

本版重点更新托盘菜单、浏览器面板和界面交互，欢迎试用反馈。测试版需手动下载，不通过稳定版自动更新推送。

[![下载 AudioSwitch.exe](https://img.shields.io/badge/%E4%B8%8B%E8%BD%BD%20AudioSwitch.exe-2563EB?style=for-the-badge)](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.1/AudioSwitch.exe)

**下载到固定、可写的文件夹，保留文件名，双击运行。** EXE 已包含完整界面，首次运行会展开到旁边的 `AudioSwitch` 文件夹，请保留整个文件夹。

[按钮不显示？点这里下载](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.1/AudioSwitch.exe) | [完整程序包](https://github.com/EthenGod/AudioSwitch/releases/download/v0.12.1/AudioSwitch-Desktop-v0.12.1-win-x64.zip)

ZIP 请全部解压，再运行其中的 `AudioSwitch.exe`。已有旧版请先从托盘退出，再将新版放到新文件夹运行，原配置会保留。桌面面板需要 WebView2，缺少时按提示安装。

## ✨ 更新内容

- 重新设计托盘右键菜单，切换设备或模式后可继续操作，加入更自然的进出动画。
- 双击托盘可选择打开主面板或浏览器；支持保持浏览器端口开放、自定义端口，留空自动分配。
- 设备优先级支持拖动排序，耳机、扬声器、显示器和麦克风使用不同图标。
- 统一下拉框、开关、深浅色和页面过渡，精简 Dolby 编辑界面的重复说明。

## 🛠️ 修复内容

- 补齐桌面面板更新和文件修复的权限，避免操作被错误拦截。
- 修复托盘首次右键无响应、被折叠面板遮住，以及从子菜单返回后底部缺失、发黑和按钮高亮残留的问题。
- 改善面板启动白屏、刷新和保存设置时闪烁，报错提示不再跟随页面切换动画。
- 修复优先级列表拖不到首尾、多出滚动条，以及端口错误提示挤动布局的问题。

源码在本页 **Source code** 中，普通使用无需下载。自有代码采用 GPL-3.0-only，第三方组件许可见包内说明。
