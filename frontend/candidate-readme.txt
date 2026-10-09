声间 UI 候选包 — 仅用于隔离验收

这不是正式更新包。不要覆盖日常使用目录，也不要交给旧版更新器安装。
本包的托盘已接入新面板和设备提示；双击 AudioSwitch.exe 仍会执行已有的启动优先级和音效规则。
候选文件缺失、版本不符或校验失败会明确报错，不自动换回旧界面。

无音频写入的初步验证：
1. 将整个包解压到新的独立目录。
2. 不启动 AudioSwitch.exe。手动运行 audio-switch-panel.exe；没有后台时应显示连接提示。
3. 关闭面板后，界面及它自己的 WebView2 进程应全部退出。
连接正在运行的新版后台后，面板的保存、切换、应用操作会影响真实设置；本包不是模拟模式。

需要 Windows x64、.NET Framework 4.x 和 Microsoft Edge WebView2 Runtime。
面板使用系统已安装的 WebView2，未附带浏览器运行时或安装程序。
如果 Windows 无法打开面板或提示缺少 WebView2，请先关闭面板，按微软官方说明手动安装／修复 Runtime，再重试：
https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
本包不会自动下载安装 Runtime。面板启动失败时有原生中文提示；未在此电脑卸载系统组件来模拟缺失环境。

candidate-manifest.json 记录格式版本、程序版本、固定文件、长度与 SHA-256。
校验用于检查包内一致性，不是发布者签名，不能证明来源可信。不要运行来源不明的包。
保留 LICENSE、NOTICE.txt、THIRD_PARTY.md、两份 THIRD-PARTY 文本及 Rust 标准库许可 HTML。
本地候选包不替代正式分发时与该构建对应的完整源码交付。

旧更新包严格要求 8 个文件；本包为独立候选格式，不兼容旧更新器。
重复从托盘打开会复用已有面板；关闭面板释放界面，后台退出时面板与提示随之退出。
独立手工运行面板仍可用于无后台诊断；它不是托盘管理的启动入口。
升级安装与修复仍使用旧发布约定，尚不支持此候选包。不要用候选目录作为日常版本。
