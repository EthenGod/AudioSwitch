# 声间 · 第一阶段 UI 原型

React + TypeScript + Tailwind + shadcn（Base UI）。仅模拟数据，本阶段不包含 Tauri、Rust 或音频后台连接。

## 启动

在仓库根目录执行：

```powershell
.\frontend\dev.ps1 -Command install
.\frontend\dev.ps1 -Command dev
```

打开 http://127.0.0.1:5173 。`dev.ps1` 优先选择已安装的现代 Node，也能使用 Codex 自带的 Node；不会安装或替换系统 Node。可加 `-NodePath '完整路径\node.exe'`。要求 Node 22.12+；本机已使用独立 Node 24 验证。旧 Node 18 不适合本工程。

如终端默认就是受支持的 Node，也可进入本目录后运行 `npm install`、`npm run dev`。不要使用会先清空依赖目录的 `npm ci`，也不要运行批量清理命令。

```powershell
.\frontend\dev.ps1 -Command typecheck
.\frontend\dev.ps1 -Command test
.\frontend\dev.ps1 -Command build
.\frontend\dev.ps1 -Command preview
```

生产预览地址是 http://127.0.0.1:4173 。预览服务只监听本机，端口占用时直接报错，不结束其他进程。开发／预览服务在对应终端按 Ctrl+C 退出；关闭浏览器标签不会结束开发服务。这与下一阶段 Tauri 桌面窗口的生命周期是两回事。

## 页面与旧功能对应

| 原有功能 | 原型位置 | 当前可验证内容 |
| --- | --- | --- |
| 主面板设备列表、输出／输入筛选 | 声音设备 | 紧凑列表、搜索、默认角色、模拟切换 |
| 设备设置 | 每行设置按钮、摘要右侧按钮 | 抽屉草稿、音量、空间音效、白名单；仅保存／取消 |
| 设备优先级对话框 | 自动切换 | 输入／输出独立排序，上下移动；保留离线设备 |
| 接入询问、通话联动、优先级 | 自动切换 | 模拟开关；通话联动关闭后切换不写角色 2 |
| 深色模式、游戏模式、开机自启 | 应用设置 | 只改变本次预览状态 |
| 自动更新开关 | 应用设置 | 模拟开关，无更新检查或下载 |
| 导入／导出、检查更新、文件检查 | 应用设置 → 备份与维护 | 可点击说明，明确“暂未接入”，不执行操作 |
| Dolby 编辑器、设备接入提示窗 | 抽屉占位／后续阶段 | 未迁移，不执行 Dolby 或系统调用 |

## 模拟数据边界

- `src/data/types.ts` 定义异步 `AudioGateway`，页面组件不直接读写设备。`PreviewGateway` 只为原型额外提供场景切换。
- `src/data/mock.ts` 是唯一数据实现，所有状态存在内存。没有 fetch、WebSocket、命名管道、Tauri invoke、注册表、文件或 localStorage/sessionStorage 读写。
- ID、输入／输出、三个默认角色和预设 `null` 含义参照现有 C# 模型。`Volume: null` 表示不写音量，`0` 是零音量；`SpatialFormat: null` 表示不写空间音效，空字符串表示关闭。Windows Sonic 的 GUID 为明确标注的示例值，不能用于真实后台。
- `Online`、`Connection`、`Kind` 为展示数据，按输入／输出分组的 `DeviceOrder` 为原型视图结构，不宣称与现有 IPC 完全同构。后续适配器必须按真实后台模型转换；未实现的 Dolby 字段不能被前端覆盖或丢弃。
- “刷新示例”重新读取当前内存状态，不丢失修改。浏览器刷新会重置全部预览；切换底部预览场景会重置示例数据并保留当前主题。
- 场景包括常规（含离线设备）、空设备、同名设备、长名称、延迟加载、连接错误。错误页“重试”恢复常规场景。

## 浏览器验收

`scripts/verify-browser.js` 是 Playwright CLI 可重跑的交互与截图脚本，不是浏览器端应用代码。先运行生产预览，再用独立会话打开本地页面并读取 snapshot：

```powershell
npx --package @playwright/cli playwright-cli -s=audio-ui open http://127.0.0.1:4173 --browser=msedge
npx --package @playwright/cli playwright-cli -s=audio-ui snapshot
npx --package @playwright/cli playwright-cli -s=audio-ui run-code --filename scripts/verify-browser.js
npx --package @playwright/cli playwright-cli -s=audio-ui close
```

截图在 `output/playwright/`；缩放使用 CSS 视口与像素密度模拟，不代替 Windows 多显示器跨 DPI 实测。构建、测试和截图均不启动现有 AudioSwitch 后台。当前结果见 [VALIDATION.md](VALIDATION.md)。

`dist/`、`node_modules/`、截图与浏览器会话输出均被忽略。构建显式关闭输出目录清空，旧构建文件保留；此目录用于本地预览，暂不作为正式发布包。

## 后续边界

第一阶段验收后，再单独确认 Tauri 桌面外壳与只读命名管道适配。后续依次接入主面板操作、其余界面、发布与升级兼容。没有自动提交、发布或合并步骤。
