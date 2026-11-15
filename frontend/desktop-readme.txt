声间 AudioSwitch Desktop

简体中文：https://github.com/EthenGod/AudioSwitch/blob/master/README.md
English: https://github.com/EthenGod/AudioSwitch/blob/master/README.en.md
日本語：https://github.com/EthenGod/AudioSwitch/blob/master/README.ja.md

[简体中文]

v0.13.0 正式版

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
成功展开后只新增 AudioSwitch 文件夹。若展开中断，错误提示会显示本次临时目录的位置。
展开入口支持 --background；--extract-only 只展开，不启动后台；--check-files --quiet 只读校验入口自身。
其他命令、快捷方式及开机自启使用展开文件夹内的主程序。入口在交接后退出，不常驻。
旧版 v0.11.x 如无法识别新格式，请从托盘退出后手动下载到新的文件夹；无需中转安装。
现有 Desktop 使用完整 ZIP 进行后续更新；不要把候选包改名用于更新。

desktop-manifest.json 固定列出版本、平台、13 个内容文件的长度和 SHA-256。
包内校验用于完整性，不替代发布者身份验证。请从项目正式发布页获取程序。
保留所有许可文件，并随分发提供该构建对应的完整源码。

[English]

This section may contain machine-translated content. The Chinese section above is authoritative.

v0.13.0 stable release

Extract the entire ZIP into a separate folder. Keep all 14 files, then run AudioSwitch.exe.
Requires Windows x64, .NET Framework 4.8 and Microsoft Edge WebView2 Evergreen Runtime.
Get WebView2: https://developer.microsoft.com/microsoft-edge/webview2/
This package does not bundle a browser or install system components automatically. Install any missing runtime, then try again.

AudioSwitch.exe --check-files --quiet checks all program files without changing them.
AudioSwitch.exe --check-webview2 checks the Runtime's installation registration without starting the audio backend.
A registered installation does not prove that WebView2 can start. If startup fails, repair the Runtime.

Normal startup runs any enabled device-priority and sound-effect rules.
Closing the panel releases its UI process. Quit from the tray menu to stop the backend.
Your active settings remain at %LOCALAPPDATA%\AudioSwitch\settings.json, outside this package.
Updates and repairs back up program files first. They do not clear personal settings or existing backups.

This version provides a full ZIP and a self-extracting EXE. No transition package is provided.
The EXE embeds the same full ZIP. On first launch, it extracts into an adjacent AudioSwitch folder and runs the app inside.
Later launches reuse a verified installation of the same or a newer version. If the existing folder is incomplete, older or damaged, extraction stops without overwriting it.
Successful extraction creates only the AudioSwitch folder. If interrupted, the error message identifies the temporary folder used for that attempt.
The launcher supports --background. Use --extract-only to extract without starting the backend; --check-files --quiet checks the launcher itself without changing files.
For other commands, shortcuts and Windows startup, use the app inside the extracted folder. The launcher exits after handing over control.
If v0.11.x cannot read the new package format, quit it from the tray and download the new version into a new folder. No intermediate installation is needed.
Existing Desktop installations use the full ZIP for subsequent updates. Do not rename a candidate package for use as an update.

desktop-manifest.json lists the version, platform, sizes and SHA-256 hashes of the 13 content files.
Package checks verify integrity, not the publisher's identity. Download from the project's official release page.
Keep all license files and provide the complete matching source code when redistributing this build.

[日本語]

この説明には機械翻訳が含まれる場合があります。内容に相違がある場合は、上記の中国語の原文を優先してください。

v0.13.0 正式版

ZIP 全体を別のフォルダーに展開し、14 個のファイルをすべて残してから AudioSwitch.exe を実行してください。
Windows x64、.NET Framework 4.8、Microsoft Edge WebView2 Evergreen Runtime が必要です。
WebView2 の入手先：https://developer.microsoft.com/microsoft-edge/webview2/
ブラウザーは同梱していません。システムコンポーネントの自動インストールも行いません。Runtime がない場合は、インストールしてから再試行してください。

AudioSwitch.exe --check-files --quiet は、プログラムファイルを変更せずに確認します。
AudioSwitch.exe --check-webview2 は、音声処理のバックエンドを起動せずに Runtime のインストール登録を確認します。
登録が見つかっても、WebView2 が正常に起動することまでは確認できません。起動に失敗する場合は Runtime を修復してください。

通常の起動では、有効にしているデバイスの優先順位や音響効果のルールが実行されます。
パネルを閉じると UI プロセスを終了します。バックエンドを終了するにはトレイメニューから終了してください。
有効な設定ファイルは %LOCALAPPDATA%\AudioSwitch\settings.json にあり、このパッケージには含まれません。
更新と修復では、先にプログラムファイルをバックアップします。個人設定や既存のバックアップは削除しません。

このバージョンでは、完全版 ZIP と展開用 EXE を提供します。移行用パッケージは提供しません。
EXE には同じ完全版 ZIP が含まれます。初回起動時に同じ場所の AudioSwitch フォルダーへ展開し、中のプログラムを起動します。
次回以降は、検証済みの同一バージョンまたは新しいバージョンを使用します。既存フォルダーが不完全、古い、または破損している場合は、上書きせずに停止します。
展開に成功すると、AudioSwitch フォルダーだけが作成されます。途中で失敗した場合は、その処理で使用した一時フォルダーの場所をエラー画面に表示します。
展開用 EXE は --background に対応しています。--extract-only は展開だけを行い、バックエンドを起動しません。--check-files --quiet は展開用 EXE 自体を変更せずに確認します。
その他のコマンド、ショートカット、Windows の自動起動では、展開先のプログラムを使用してください。展開用 EXE は起動を引き継ぐと終了し、常駐しません。
v0.11.x が新しい形式を読み取れない場合は、トレイから終了し、新しいフォルダーへ手動でダウンロードしてください。中間バージョンのインストールは不要です。
既存の Desktop 版は、以降の更新に完全版 ZIP を使用します。候補版パッケージの名前を変更して更新に使わないでください。

desktop-manifest.json には、バージョン、プラットフォーム、13 個の内容ファイルのサイズと SHA-256 が記載されています。
パッケージの検証は完全性を確認するもので、配布者の身元を保証するものではありません。プロジェクトの公式リリースページから入手してください。
再配布する場合は、すべてのライセンスファイルを残し、このビルドに対応する完全なソースコードを提供してください。
