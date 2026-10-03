# 声间 · 新前端与只读桌面面板

React + TypeScript + Tailwind + shadcn（Base UI）。浏览器中使用模拟数据；Tauri 2 桌面窗口只读取现有后台的真实状态，不执行切换或保存。

## 桌面版（第二阶段）

先按平时的方式启动原版声间后台，再在仓库根目录执行：

```powershell
.\frontend\desktop.ps1 -Command build
.\staging\ui-target\release\audio-switch-panel.exe
```

已构建时直接双击上述 EXE 即可。它是独立只读面板，未替换 `bin/AudioSwitch.exe`，没有加入旧更新包。只读窗口不会替你启动后台，因为原后台启动时可能应用原有优先级和音效规则。

本机已准备 Node 24.19.0、Rust 1.99.0 MSVC、微软 C++ Build Tools / Windows SDK 和 WebView2。Rust 位于 `staging/ui-toolchain/`，脚本只临时设置当前进程的环境变量，不更改全局 PATH。其他机器需自行安装 [Tauri Windows 前置工具](https://v2.tauri.app/start/prerequisites/)。

```powershell
.\frontend\desktop.ps1 -Command dev    # 联动 Vite 的桌面开发模式
.\frontend\desktop.ps1 -Command test   # 只读通信测试，不要求真实后台
.\frontend\desktop.ps1 -Command check
```

桌面窗口打开、点击“刷新状态”或重新获得焦点时读取一次后台缓存；没有周期轮询。连接失败时移除过期显示并提供重试，窗口持续前台时需手动刷新。设备切换、后台开关、预设保存和排序均禁用；深浅主题仅改变本次窗口。关闭窗口会退出面板与所属 WebView2 进程；开发模式下 Vite/Cargo 开发工具需在终端单独退出。

数据映射在 `src/data/desktop.ts`，原生端仅注册无参数 `read_snapshot`，固定发送 `{"Action":"snapshot"}`。没有任意请求、文件读写或启动进程的网页权限。详细边界、实测结果和内存见 [STAGE-2.md](STAGE-2.md)。

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

生产预览地址是 http://127.0.0.1:4173 。预览服务只监听本机，端口占用时直接报错，不结束其他进程。开发／预览服务在对应终端按 Ctrl+C 退出；关闭浏览器标签不会结束开发服务。这与 Tauri 桌面窗口的生命周期是两回事。

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
- 浏览器模式选用 `src/data/mock.ts`，所有状态存在内存，不连接真实后台；Tauri 模式选用单独的只读适配器。
- ID、输入／输出、三个默认角色和预设 `null` 含义参照现有 C# 模型。`Volume: null` 表示不写音量，`0` 是零音量；`SpatialFormat: null` 表示不写空间音效，空字符串表示关闭。Windows Sonic 的 GUID 为明确标注的示例值，不能用于真实后台。
- `Online`、`Connection`、`Kind` 为展示数据，按输入／输出分组的 `DeviceOrder` 为视图结构。桌面适配器将后台的端点列表转换为分组 ID 列表；离线顺序、同名设备、三个默认角色和 null 均单独处理。只读阶段不发送任何预设，因此不会覆盖 Dolby 字段；下一阶段写入前需扩充完整契约。
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

第二阶段验收后再接入主面板真实操作；其余界面和发布兼容分别验收。`dist-desktop/` 使用稳定资源文件名，避免把历史浏览器产物一起嵌入；构建不批量清空输出。没有自动提交、发布或合并步骤。
