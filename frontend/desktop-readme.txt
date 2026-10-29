声间 Desktop 完整包

v0.12.1 测试版。请手动下载测试版，稳定版自动更新不会安装 Pre-release。

请将整个 ZIP 解压到独立目录，保留全部 14 个文件，再运行 AudioSwitch.exe。
需要 Windows x64、.NET Framework 4.8 和 Microsoft Edge WebView2 Evergreen Runtime。
WebView2 安装入口：https://developer.microsoft.com/microsoft-edge/webview2/
本包不捆绑浏览器，不自动安装系统组件。缺少 Runtime 时先安装，再重试。

AudioSwitch.exe --check-files --quiet 只读检查完整程序文件。
AudioSwitch.exe --check-webview2 只读检查 Runtime 安装登记，不启动音频后台。
检测到安装登记不等于已经验证浏览器可正常创建；启动失败时请修复 Runtime。

正常启动会执行原有已启用的设备优先级和音效规则。
关闭面板释放界面进程；从托盘退出才会关闭后台。
活动配置仍在 %LOCALAPPDATA%\AudioSwitch\settings.json，不在本包内。
更新和修复先备份程序文件；用户配置和已有备份不清空。

本版仅提供完整 ZIP 和单 EXE 便捷展开版，不提供过渡包。
单 EXE 内置同一个完整 ZIP，首次运行展开到旁边的 AudioSwitch 文件夹，再启动其中主程序。
重复运行复用已校验的同版本或更新版本；已有目录不完整、较旧或损坏时停止，不覆盖。
展开过程保留 AudioSwitch-unpack-唯一编号 中的包与失败记录，不清理用户文件。
展开入口支持 --background；--extract-only 只展开，不启动后台；--check-files --quiet 只读校验入口自身。
其他命令、快捷方式及开机自启使用展开文件夹内的主程序。入口在交接后退出，不常驻。
旧版 v0.11.x 如无法识别新格式，请从托盘退出后手动下载到新的文件夹；无需中转安装。
现有 Desktop 使用完整 ZIP 进行后续更新；不要把候选包改名用于更新。

desktop-manifest.json 固定列出版本、平台、13 个内容文件的长度和 SHA-256。
包内校验用于完整性，不替代发布者身份验证。请从项目正式发布页获取程序。
保留所有许可文件，并随分发提供该构建对应的完整源码。
