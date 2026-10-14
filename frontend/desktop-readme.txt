声间 Desktop 完整包

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

从旧版升级：旧更新器先安装同版本发布中的 AudioSwitch-v<版本>-win-x64.zip 过渡包，
过渡版再次检查更新后选择 AudioSwitch-Desktop-v<版本>-win-x64.zip，允许同版本迁移。
缺少 WebView2 时保留过渡版，安装 Runtime 后重试。
不要用 Desktop ZIP 替换旧名 ZIP，也不要把候选包改名后用来更新。
单独的 AudioSwitch.exe 是过渡后台，不是包含新界面的单文件程序。

desktop-manifest.json 固定列出版本、平台、13 个内容文件的长度和 SHA-256。
包内校验用于完整性，不替代发布者身份验证。请从项目正式发布页获取程序。
保留所有许可文件，并随分发提供该构建对应的完整源码。
