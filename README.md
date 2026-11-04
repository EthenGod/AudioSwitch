# 声间 AudioSwitch

声间是一款 Windows 托盘工具，用来切换默认扬声器、耳机和麦克风。可以手动选择设备，也可以让声间在设备接入或断开时，按你设定的优先级切换。

[![前往 GitHub Releases 下载](https://img.shields.io/badge/%E5%89%8D%E5%BE%80%20GitHub%20Releases%20%E4%B8%8B%E8%BD%BD-2563EB?style=for-the-badge&logo=github&logoColor=white)](https://github.com/EthenGod/AudioSwitch/releases)

支持 Windows 10 / 11 x64。桌面面板需要 Microsoft Edge WebView2 Runtime。

## 下载与运行

打开 [GitHub Releases](https://github.com/EthenGod/AudioSwitch/releases)，在所选版本的 **Assets（附件）** 中下载。以 v0.12.2 为例，两种附件都包含完整桌面面板。

| 附件 | 使用方法 |
| --- | --- |
| **AudioSwitch.exe**（推荐） | 下载到可写的新文件夹，保留文件名，双击运行。首次启动会将完整程序展开到旁边的 `AudioSwitch` 文件夹。 |
| **AudioSwitch-Desktop-v0.12.2-win-x64.zip** | 将整个 ZIP 解压到新文件夹，运行其中的 `AudioSwitch.exe`。 |

手动升级前，先从托盘退出旧版，再启动新版。设备设置保存在当前 Windows 用户的配置目录中，升级时会沿用。

## 使用

- **切换设备** — 右键单击托盘图标，选择输出设备或麦克风。双击图标默认打开主面板，也可以在应用设置中改为打开本机浏览器面板。
- **自动切换** — 在“自动切换”中分别设置输出和输入设备的优先级。通过“同时切换通话设备”决定是否一并更改 Windows 默认通话设备。
- **设备预设** — 在“声音设备”中打开设备设置，为设备命名或保存音量。输出设备还可设置 Windows 空间音效，受支持的 Dolby 设备可保存独立方案。“仅保存”留待下次使用，“保存并应用”立即切换并应用预设。
- **游戏模式** — 在托盘菜单或应用设置中开启，可暂停自动检查更新和后台下载。设备切换继续工作。

关闭面板后，声间继续在托盘运行。要结束后台，请从托盘菜单退出。

## 配置与兼容性

配置文件位于 `%LOCALAPPDATA%\AudioSwitch\settings.json`。可在面板中导出备份，或导入已有配置。设备按 Windows 端点 ID 匹配，同名设备各自保留预设。[配置格式](CONFIGURATION.md)

声间切换的是 Windows 默认设备。已在应用内指定输出设备的软件可能继续使用原设备。Dolby 方案只适用于已适配的 DAX 组件和设备。[Dolby 均衡器说明](DOLBY-EQ.md)

## 开发与测试

C# 后台负责设备监听、切换和预设。桌面面板使用 React、TypeScript 和 Tauri 2。在 Windows 上，从仓库根目录构建后台并运行回归测试：

```powershell
.\build.ps1 -Test -OutputDirectory staging
```

[前端与桌面面板构建](frontend/README.md) ｜ [实机测试清单](MANUAL-TESTS.md) ｜ [发行与打包](releases/README.md)

## 许可证

声间自有代码采用 [GPL-3.0-only](LICENSE)。随附的 SoundVolumeCommandLine（SVCL）使用原作者许可，禁止收费分发或作为商业产品的一部分分发。[版权声明](NOTICE.txt) ｜ [第三方许可](THIRD_PARTY.md)
