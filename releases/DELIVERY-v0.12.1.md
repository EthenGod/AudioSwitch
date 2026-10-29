# v0.12.1 测试 Release 交付记录

日期：2026-10-29。已按用户要求生成本地待上传附件，未上传、打标签或发布。

**PR 前检查发现更新／修复命令缺少桌面权限。下列旧附件保留原样，不包含此修复，不应作为最终上传件。** 修复后候选包位于 `staging/pr-ready-20261029/v0.12.1/`，状态和摘要见 `staging/pr-ready-20261029/PR.md`。确认后再决定如何替换待上传附件，不混用两轮摘要。

## 附件

`releases/v0.12.1/` 仅有以下两个文件。发行说明和本记录均放在该目录外。

| 文件 | 字节数 | SHA-256 |
| --- | ---: | --- |
| AudioSwitch.exe | 3594240 | `66d75d1988f8aa6c1b08b595644dacc58ba4ba795a423139588bbc9d957f1b78` |
| AudioSwitch-Desktop-v0.12.1-win-x64.zip | 2976930 | `4feba8138beb9c58cb7c18af3b9cacf6c43e52bd523701527b69b69e4de60c0d` |

EXE 内置本轮完整 ZIP，首次运行展开到旁边的 AudioSwitch 文件夹。外层 EXE 与 ZIP 内的主程序用途不同，不应直接比较这两个 EXE 的校验值。展开出的 14 个文件应与 ZIP 对应文件逐字节一致。

## 本轮验证

- 构建来源为 `refactor/ui`，应用源码基于 `8c92ad1f9dbb1dd0604734df5aeae8dbd6800ead`。本轮追加发行文档，并将测试版说明加入包内 `DESKTOP-README.txt`；尚未提交这些改动。
- 前端 19 个测试文件、124 项测试通过。
- C# 普通回归 821 项通过，包含真实设备只读枚举。没有启动真实音频后台或执行音频写入。
- TypeScript 类型检查、前端生产构建、Tauri / Rust release 编译通过。前后端版本一致为 0.12.1。
- 完整 ZIP、外层 EXE、展开后主程序的完整性检查通过。包内 14 个文件、版本、长度及 SHA-256 校验通过。
- EXE 在独立目录展开，逐字节匹配 ZIP。重复展开、同版／新版复用、旧版和损坏目录保护、同名文件冲突、非法路径拒绝检查通过。真实用户配置未变。
- 两附件复制到待上传目录后摘要一致。发行说明三个下载入口均对应本版实际附件；没有占位符。
- 近期 UI 调整沿用已完成的浏览器、托盘截图与定向验证，见 HANDOFF.MD。本轮没有重复运行硬件和音频验收。

命令：

```powershell
.\release.ps1 -NodePath 'C:\Users\Ethen\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
# frontend 目录，使用上述 Node 和已安装 npm CLI
npm test -- --reporter=dot
```

证据：

- `staging/release-v0.12.1-20261029.log`
- `staging/release-v0.12.1-frontend-tests.log`
- `staging/ui-candidate-65525c6fcf4846a1b7ad099a06a46c00/backend-build.log`
- `staging/desktop-release-56555bdf19754af090d5c49d8b3ffbb6/verification.json`

构建保留三项非阻断警告：前端 app.js 为 500.83 kB，略超 Vite 分块提醒阈值；Rust 工具链报告用户路径规范化提示；C# 的 TrayPopup.Closing 隐藏 Form.Closing，产生 CS0108。编译和校验均正常退出，本轮未为消除提示引入无关代码改动。

## 发布时

1. 经授权提交并推送对应源码，包含本轮包内说明的改动，再创建 `v0.12.1` 标签。
2. 标题使用“声间 AudioSwitch v0.12.1 测试版”，正文复制 `RELEASE-NOTES-v0.12.1.md`。
3. 上传上述两个附件，勾选 **Set as a pre-release**，不要设为 Latest。测试版不进入稳定版自动更新。
4. 上传后核对 GitHub 附件摘要和下载按钮，再实际下载核对。当前链接只是预先写好的发行目标，尚未验证线上可用。

保留原发布包、日常 bin 和用户配置。未自动提交、推送、合并或发布。真实 GitHub 下载／升级、全新机器首次运行、全部设备插拔与睡眠唤醒、旧系统及跨屏 DPI 不由本轮编译和展开校验替代。
