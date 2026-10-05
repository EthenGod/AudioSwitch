# 声间 · 新前端与桌面面板

React + TypeScript + Tailwind + shadcn（Base UI）。浏览器使用内存模拟数据；Tauri 2 桌面窗口通过现有 C# 后台读取状态并执行主面板操作。

## 桌面版（第四阶段分项迁移）

在仓库根目录构建，不覆盖日常使用的 `bin/`：

```powershell
.\build.ps1 -Test -OutputDirectory staging
.\frontend\desktop.ps1 -Command build
```

试用真实操作时，先从托盘退出原版后台，再手动打开 `staging/AudioSwitch.exe`，然后打开 `staging/ui-target/release/audio-switch-panel.exe`。测试后台启动会沿用原有配置、启动优先级和音效规则；不要同时运行两份后台。试用结束后可退出测试后台，按原方式打开 `bin/AudioSwitch.exe`。本阶段不替换日常程序、不加入旧更新包。面板不会替你启动后台。

连接旧后台时自动保持只读；需要新后台返回面板接口版本才能开放操作。当前已接入设备切换、基础预设与白名单保存、排序、优先级／询问／通话联动、主题、游戏模式、自动更新开关和开机自启。开机自启登记的是当前运行后台的路径；在 `staging` 试用期间不要把测试路径误作日常自启路径。

“仅保存”不切换设备、不写音量或音效、不取消 Dolby 任务；由后台保留最新 Dolby 对象。预设被其他窗口修改时会拒绝覆盖。音量／空间音效读取失败或设备离线时保留原字段，仍可编辑白名单规则。优先级开启和排序沿用原有立即选择设备的行为，界面会说明。

窗口打开、手动刷新或重新获得焦点时读取缓存；不周期轮询，编辑草稿期间也不因焦点变化刷新。操作使用后台返回的实际结果，不提前显示成功。超时或断线后不自动重试，先刷新确认实际状态。Dolby 是异步任务，路由成功不代表 Dolby 已应用完成；可刷新查看后台提示。

本机使用独立 Node 24.19.0、Rust 1.99.0 MSVC、微软 C++ Build Tools / Windows SDK 和 WebView2。Rust 位于 `staging/ui-toolchain/`，脚本仅临时设置当前进程环境，不改全局 PATH。其他机器前置条件见 [Tauri Windows 文档](https://v2.tauri.app/start/prerequisites/)。

```powershell
.\frontend\desktop.ps1 -Command dev
.\frontend\desktop.ps1 -Command test   # 隔离命名管道和参数测试，不连接真实后台
.\frontend\desktop.ps1 -Command check
```

Rust 只注册状态查询、主面板操作、四个备份命令和维护任务的开始／读取／取消；操作采用固定白名单，没有任意文件、进程或 IPC 请求权限。备份文件必须由原生选择框确定；导出来自后台完整配置，导入先预览再确认，确认期间有其他配置修改会拒绝覆盖。关闭窗口释放面板及其 WebView2 进程；开发模式的 Vite/Cargo 工具需在终端单独退出。

接口版本 3 的后台支持只读维护检查。Rust 根据实际后台路径启动 `--panel-maintenance` 短时辅助进程，复用现有 C# 正式版查询和必要文件校验；完成或取消后退出。网页不能传入程序、路径、URL 或安装命令。检查运行时每 400 ms 读取的是 Rust 内存中的任务结果，不轮询音频后台。关闭抽屉等待取消确认，关闭面板也通知辅助进程停止。新面板的下载、安装和修复尚未开放，待第五阶段发布兼容；现有旧版维护流程保持不变。

主面板实机结果和剩余人工项目见 [STAGE-3.md](STAGE-3.md)，导入／导出及后续迁移见 [STAGE-4.md](STAGE-4.md)。[STAGE-2.md](STAGE-2.md) 保留只读及内存实测记录。

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
| 主面板设备列表、输出／输入筛选 | 声音设备 | 紧凑列表、搜索、默认角色、桌面真实切换／浏览器模拟切换 |
| 设备设置 | 每行设置按钮、摘要右侧按钮 | 抽屉草稿、音量、空间音效、白名单；仅保存／取消 |
| 设备优先级对话框 | 自动切换 | 输入／输出独立排序，上下移动；保留离线设备 |
| 接入询问、通话联动、优先级 | 自动切换 | 桌面真实开关；通话联动关闭后切换不写角色 2 |
| 深色模式、游戏模式、开机自启 | 应用设置 | 桌面保存真实偏好／浏览器仅改变预览状态 |
| 自动更新开关 | 应用设置 | 桌面保存后台更新偏好；浏览器模拟 |
| 导入／导出 | 应用设置 → 备份与维护 | 后台版本 2 支持原生文件选择、完整导出、预览确认导入；浏览器仅模拟 |
| 检查更新、文件检查 | 应用设置 → 备份与维护 | 后台版本 3 支持真实只读检查、结果、取消和重试；浏览器模拟，不安装或修复 |
| Dolby 编辑器、设备接入提示窗 | 抽屉占位／后续阶段 | 未迁移，不执行 Dolby 或系统调用 |

## 模拟数据边界

- `src/data/types.ts` 定义异步 `AudioGateway`，页面组件不直接读写设备。`PreviewGateway` 只为原型额外提供场景切换。
- 浏览器模式选用 `src/data/mock.ts`，所有状态存在内存，不连接真实后台；Tauri 模式选用单独的桌面适配器。
- ID、输入／输出、三个默认角色和预设 `null` 含义参照现有 C# 模型。`Volume: null` 表示不写音量，`0` 是零音量；`SpatialFormat: null` 表示不写空间音效，空字符串表示关闭。Windows Sonic 的 GUID 为明确标注的示例值，不能用于真实后台。
- `Online`、`Connection`、`Kind` 为展示数据，按输入／输出分组的 `DeviceOrder` 为视图结构。桌面适配器将后台的端点列表转换为分组 ID 列表；离线顺序、同名设备、三个默认角色和 null 均单独处理。基础保存使用独立窄接口，仅发送音量、空间音效、规则及编辑前基线；后台合并最新 Dolby 字段。
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

设备提示可单独预览：`http://127.0.0.1:4173/?view=prompt`（加 `&light` 切浅色，`&disconnected` 看断开）。真实桌面入口为 `staging/ui-target/release/audio-switch-panel.exe --prompt`，可传 `--owner=后台PID`；仅连接已运行后台。普通关闭保留待办，“保持当前选择”仅处理当前提示，待办清空或所属进程退出则退出窗口。无后台或结果不确定时提供手动刷新，不重复发送写入。

`scripts/verify-prompt-browser.js` 验证模拟提示和缩放；`scripts/verify-prompt-owner.ps1` 用临时等待进程验证所属进程退出后的窗口释放，不启动音频后台。提示的实际插拔、音频操作与跨显示器验收见根目录 MANUAL-TESTS.md；日常自动弹窗、面板实例复用仍在第五阶段统一接入。

第四阶段按导入／导出、维护检查、设备提示、Dolby 编辑器分别验收；发布兼容另行处理。`dist-desktop/` 使用稳定资源文件名，避免把历史浏览器产物一起嵌入；构建不批量清空输出。没有自动提交、发布或合并步骤。

桌面隔离验收脚本 `scripts/verify-operations.js` 必须在专用测试面板中运行：它临时拦截所有 IPC，验证生产界面的写操作反馈，结束后恢复通信并重载。截图文件名含 `fixture`，不代表真实音频实测。原 `verify-desktop.js` 仅用于旧后台只读兼容验收。
