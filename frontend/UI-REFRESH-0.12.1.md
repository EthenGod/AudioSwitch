# v0.12.1 UI 调整记录

日期：2026-10-24。

本轮按新版 AGENTS.md 调整前端。没有改动音频核心或配置格式。
没有生成 Release 附件。没有提交、合并或发布。

## 用户可见变化

- 深浅主题统一使用 Radix Colors。背景使用 Slate，主要操作使用 Indigo。
- 删除重复说明、装饰标签和中点分隔符。保留预览标识、错误和必要操作提示。
- 全部文字统一为 12、14、20px。用字重、颜色和间距区分层级。
- 统一按钮、卡片、输入框的圆角和间距。调整图标与文字对齐。
- 将主题按钮移到页面标题区。删除重复的顶部导航。
- 设置开关成功后直接更新状态。失败时仍显示错误。
- 抽屉进入用 250ms，退出用 200ms。按钮反馈用 150ms。
- 开关使用轻微回弹。减少动态效果时停用动画。
- 修复长设备名撑宽小窗口的问题。抽屉内容滚动时，底部按钮保持可见。

颜色用途遵循 [Radix 色阶规范](https://www.radix-ui.com/colors/docs/palette-composition/understanding-the-scale)。
1–2 用于背景，3–5 用于控件状态，6–8 用于边框，9–10 用于实色，11–12 用于文字。

## 涉及文件

| 范围 | 文件 |
| --- | --- |
| 配色、字号、布局和动画 | `src/styles.css`、`src/components/ui/button.tsx` |
| 页面与提示 | `src/App.tsx`、`src/PromptApp.tsx`、`src/components/Devices.tsx`、`Automation.tsx`、`Settings.tsx`、`shared.tsx` |
| 抽屉文案与退出动画 | `src/components/DeviceSheet.tsx`、`DolbySheet.tsx`、`BackupSheet.tsx`、`MaintenanceSheet.tsx`、`UpgradeSheet.tsx` |
| 图标、示例和窗口标题 | `public/mark.svg`、`src/data/mock.ts`、`index.html`、`src-tauri/tauri.conf.json`、`src-tauri/src/main.rs` |
| 依赖 | `package.json`、`package-lock.json`，固定 `@radix-ui/colors` 为 3.0.0 |
| 受影响的已有测试 | `src/App.test.tsx`、`OperationsApp.test.tsx`、`PromptApp.test.tsx`、`DolbyApply.test.tsx` |
| 说明与交接 | 本文、`THIRD-PARTY.md`、两级 `README.md`、`VERSION-0.12.1.md`、根目录 `HANDOFF.MD` |

Dolby 抽屉仍先等待取消和恢复，再关闭。失败消息先传给主面板。
音频操作、设备匹配和 `null` 的含义保持原样。
工作区原有的版本及打包改动不属于本轮 UI 调整。

## 已完成验证

- TypeScript 类型检查通过。
- 14 个测试文件、91 项测试通过。
- 前端桌面模式构建通过。产物放在 `staging/ui0121-web/`。
- 浏览器检查了设备、自动切换、应用设置的深浅主题。
- 检查了键盘焦点、关闭抽屉后的焦点恢复。
- 检查了设备和 Dolby 抽屉的小窗口滚动及底部按钮。
- 检查了 320×300 提示窗口。文字未小于 12px，按钮可见。
- 抽查浏览器控制台，无错误和警告。

布局检查包括 1100×740、980×680。
另检查 784×544、653×453、490×340 的缩小视口。
它们对应最小窗口下 125%、150%、200% 的可用布局空间。
长名称场景没有横向溢出。可见字号只有 12、14、20px。
这些检查不代替 Windows 原生 DPI 验收。

本轮没有重新编译原生桌面外壳，也没有运行实机音频测试。
减少动态效果已检查 CSS 规则，未切换系统设置实测。
浏览器使用模拟数据，没有连接真实后台或读写活动配置。

## 开销

新增依赖只提供静态配色样式。没有新增动画库、后台服务或轮询。
本轮 CSS 与 JS 的 gzip 合计为 142.68 kB。
相对本项目上一轮构建增加 1.19 kB。
这只是静态文件大小，不代表进程内存或帧率测量。

## 本地证据

- 测试日志：`staging/ui0121-tests.log`。
- 构建日志：`staging/ui0121-build.log`。
- 布局数据：`staging/ui0121-review/layout-checks.json`。
- 深浅截图：`staging/ui0121-review/devices-dark.jpg`、`devices-light.jpg`。
- 其他截图：同目录的 `automation-light.jpg`、`settings-light.jpg`、`settings-dark.jpg`。
- 小窗口截图：同目录的 `devices-small-long.jpg`、`prompt-small.jpg`。

路径均相对项目根目录。构建和截图只用于本地验收。
