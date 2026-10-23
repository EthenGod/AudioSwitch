# UI 重构发布验收表

更新：2026-10-24。**当前结论：A 已通过；B 按用户决定收尾，具体硬件覆盖仍未逐项确认；C 本地迁移和失败恢复已通过，v0.12.0 双包已生成并校验。剩余匹配源码的提交、发布授权及线上连续迁移，不再追加逐项边角验收。**

当前交付见 [v0.12.0 附件、摘要及构建记录](../releases/DELIVERY-v0.12.0.md)：778 项后台检查、91 项前端测试、生产构建及两种正式包校验通过；新面板版本文字已查看，9 个模拟宿主／界面进程退出，日常 bin 和真实配置未变。5.4i 仍保留为之前核心操作和迁移实测对应的历史包，不把其实际音频测试算作 v0.12.0 重测。

最新实测见 [A 核心真实操作结果](LIVE-ACCEPTANCE.md#a-核心真实操作结果2026-10-22)：新面板仅保存、确认导入／自动备份恢复、输出与输入往返、音量／空间音效及 Dolby 正常应用均已取得独立读回。期间两个恢复脚本问题已处理，历史失败记录保留，最后恢复核对全部通过；7 个界面进程和测试后台均退出。没有修改产品代码、重建既有包、替换 bin、提交、合并或发布。真实托盘菜单点击未执行，合并至 B 的一次日常观察。

## 收尾标准（取代后文逐项扩展的推进顺序）

用户要求合理检查、避免无休止验收。后文保留为证据和覆盖范围记录；其中“待验”不再全部视为正式交付前必须逐项人工执行。未测项目仍标未测，不等于通过。

剩余只按以下三关推进，不再增加 5.4l、5.4m 等细碎阶段：

| 关卡 | 一次完成的范围 | 结束条件 |
| --- | --- | --- |
| A 核心真实操作（已完成） | 仅保存、确认导入及备份恢复、输出与输入切换、基础音效与受支持 Dolby 应用、进程退出；[实测结果](LIVE-ACCEPTANCE.md#a-核心真实操作结果2026-10-22) | 结果与独立读回一致，原配置及音频恢复核验通过。已通过项不再重复；通话角色关闭状态、写入中硬件故障不冒充本轮覆盖 |
| B 日常使用（用户决定收尾） | 用户部分试用暂无问题，并明确“按照没问题继续推进”；[记录](DAILY-USE.md)保留未逐项确认的范围，不要求重做 | 作为用户接受当前试用结果继续推进，不冒充所有硬件、睡眠或长期稳定性均已实测 |
| C 迁移与发布（本地演练及 v0.12.0 打包通过） | [实测结果](MIGRATION-ACCEPTANCE.md)与[交付包](../releases/DELIVERY-v0.12.0.md)已准备；下一步在明确授权后提交对应源码、发布三个附件，再走一次真实 GitHub 连续迁移 | 本地程序和配置完整、可回退；提交／标签须对应完整构建输入。在线迁移成功后才宣布迁移闭环完成，本地交付不冒充已发布 |

复测规则：已有通过记录继续有效。仅相关代码改变、出现实际故障或最终包必要校验时复测；修复后补一个能复现问题的用例和受影响的回归，不重开全套验收。核心功能错误、数据损坏、误写音频、无法恢复或升级失败必须处理；一般文案或不影响使用的排版问题记入后续改进。

目录 ACL、导入占用、活动配置路径别名的原生提示、缺少 WebView2 的新机器、非日常跨屏 DPI 等列为补充覆盖，除非出现具体问题，不再为每个项目单独安排验收轮次。已有防护和自动回归保留，不删测试、不削弱校验；不能因此声称这些环境均已实测。

2026-10-21 执行前准备记录：工作区起初干净，提交为 `3bf9de2`；优先级及通话角色联动开启，自动更新关闭、游戏模式开启，8 个设备预设、3 个 Dolby 预设。当时仅收敛清单；随后用户明确允许真实操作，A 每次继续前均重新取基线，最新结果见上方，不能继续将该授权标成待确认。

最新本地验证包为 `staging/release-54i/v0.11.1/`，构建自提交 `c14dc1557c17bae3a89694099d666e3ff654ef6c`，来源与摘要见下方 5.4i 记录。旧包均保留，不混用它们的摘要与实测记录。5.4d 真实后台实测对应旧 5.4c 包；5.4g／5.4h 使用 5.4f-final 面板和只读入口；5.4i 只做新构建及包校验，不把历史窗口测试算作新包实测。

5.4d 已按用户确认后的 [真实后台联调清单](LIVE-ACCEPTANCE.md) 执行。真实后台、面板打开／关闭／重开正常，前后默认设备、音量、空间音效、Dolby、配置和自启登记一致，无需恢复写入。

5.4d 的授权仅用于当次启动规则及必要恢复；5.4g 继续遵守无音频写入限制，没有确认导入、安装更新、发布、提交或合并。表中的“隔离”指假后台／临时目录／模拟设备，不等于真实日常链路；真实短时测试也不代表剩余完整链路通过。

## 验收范围

| 项目 | 已有证据 | 发布前仍需完成 |
| --- | --- | --- |
| 页面与基本交互 | 第一阶段与 5.3c 的浏览器测试；深浅色、键盘和等效缩放记录；5.4g 原生文件选择框取消、中文路径导出与读取；5.4h 损坏 JSON、非法 UTF-8、超大文件拒绝与重选恢复通过；5.4j Windows 文件层保护及重试；5.4k 实际面板导出占用错误／解锁重试、原生只读提示／取消恢复通过 | Windows 原生缩放、跨显示器 DPI；活动配置覆盖保护的原生界面反馈；目录 ACL 拒绝访问及导入占用错误仍未做桌面实测 |
| 真实只读数据 | 4.4c 设备／Dolby 读取；5.4d 真实 TrayHost 显示 14 台设备（10 台在线），三个页面显示正常，前后读值与配置一致；5.4f 新包的主面板／提示 WebView2 文档标题已实测正确（模拟后台） | 持续运行的完整联调；断开、重新连接与错误提示人工确认 |
| 窗口与进程 | 5.4a 隔离主面板／提示行为检查；5.4d 真实后台打开／关闭／重开主面板，7 个界面进程释放；退出后产品进程为 0 | 真实托盘点击、实际设备提示及长时间稳定性；本次短时内存采样见联调清单 |
| 保存、导入与音频操作 | 第三／四阶段的逻辑、模拟 IPC、失败和取消回归；5.4g 真实配置导出；A 已完成新面板仅保存、确认导入／自动备份恢复、默认角色切换、音量／空间音效／Dolby 正常应用及原值恢复 | 真实写入途中故障／驱动异常恢复未刻意注入，沿用自动回归证据；不为正常路径重复开验收 |
| 安装、修复及回滚 | 5.3d 假进程安装助手 800 项（含普通回归）；5.4a 实际 Desktop 文件安装与备份、14 文件逐字节一致及只读完整性校验 | 实际生产程序退出、替换、重启、同版本完整修复和失败恢复；重启可能触发既有音效规则 |
| 旧版迁移路线 | 双格式资产选择、同版本迁移规则及本地双包验证 | 旧正式版通过 GitHub 获取过渡版，再获取 Desktop 的连续迁移；当前未发布任何新版本 |
| WebView2 | 5.3d 注册检测、缺失／权限异常注入；本机实际创建 Desktop 窗口通过；5.4b 助手最后预检失败的跨进程反馈和重试回归 | 缺少 Runtime 的独立 Windows 环境：提示、手动安装、重试；不卸载日常机器的组件 |
| 硬件与系统事件 | 纯逻辑／事件合并与失败恢复回归 | USB／蓝牙插拔、睡眠唤醒、通话角色行为、Dolby 支持设备实测 |
| 发布内容与版本 | 旧 8 文件／Desktop 14 文件包、完整许可与摘要；3 个附件一致 | 协调递增前后端版本、确认完整对应源码并经用户批准发布；本地 0.11.1 验证产物不得覆盖旧正式版 |

## 5.4a 实包证据

以下 ZIP 和内存记录对应 5.4a 的历史构建。5.4b 改动了更新助手；这些旧 ZIP **不含该修复**。5.4c 已重新构建完整包，后续本地验收使用 `staging/release-54c/v0.11.1/`，详见本文末尾。历史窗口／内存记录不冒充新包实测；旧包和旧日志均保留。

- 输入为 `staging/release-53d/v0.11.1/AudioSwitch-Desktop-v0.11.1-win-x64.zip`，SHA-256 `182b706b907a26b56ddeb8043808c1299c7bef9e543daa02519415a11c13ac72`。本轮重新用生产更新解压器验证和解压，两份 ZIP 都通过。
- `staging/t54a/AudioSwitch.Tests.exe --desktop-package=...`：774 项通过，包含 768 项普通回归及 6 项实包事务验证。实际 Desktop 的说明、许可和清单原样保留；只在临时目录安装、备份和运行 `--check-files --quiet`，不执行音频后台。
- 通过同一个正式窗口启动器，从解压目录启动真实面板和提示。5 项窗口行为检查通过；第一次关闭主面板及随后关闭全部窗口均额外检查 WebView2 子进程退出。模拟入口拒绝音频和配置写请求。
- 另以独立等待进程作为所有者，分别验证主面板和提示跟随所有者退出；每次 7 个界面／WebView2 进程全部退出，活动配置哈希一致。

## 隔离环境内存记录

下表来自 `frontend/output/desktop/stage54a-normal.json`，未启用浏览器调试端口。测试入口使用模拟设备，并附带一个 conhost 测试控制台，二者不等于真实托盘后台。打开后等待约 5 秒取单次样本；不是压力测试或性能上限。工作集为各进程之和，可能重复计入共享页，不能当作唯一物理内存用量。

| 状态 | 进程数（含测试入口和控制台） | 工作集合计 MiB | 私有内存合计 MiB |
| --- | ---: | ---: | ---: |
| 未打开界面 | 2 | 41.4 | 28.3 |
| 仅主面板 | 9 | 425.7 | 231.3 |
| 主面板和提示同时打开 | 11 | 530.0 | 289.7 |
| 两个窗口都关闭 | 2 | 46.5 | 31.5 |
| 测试入口结束 | 0 | — | — |

关闭窗口后，面板和 WebView2 进程全部释放；测试入口自身私有内存比初始样本高约 3.2 MiB，不能写成“所有内存完全回到初始值”。打开时仍有明显 WebView2 开销；正式后台及长期变化另测，不据此宣布轻量性能完全达标。

日志：`staging/stage54a-build.log`、`stage54a-archives.log`、`stage54a-package-test.log`。窗口原始记录：`frontend/output/desktop/stage54a-normal.json`、`stage54a-desktop.json`（调试端口开启的对照）、`stage54a-panel-owner.json`、`stage54a-prompt-owner.json`。实包事务结果：`staging/t54a/desktop-real-5e5fcbb5/result.json`。所有输出均保留在忽略目录，不提交。

后续先按表中剩余范围选择具体验收项；涉及真实后台启动、配置替换或音频写入时，需先落实备份、恢复及当时授权。没有完成这些项目之前，不标记整个第五阶段完成，不替换日常版本。

## 5.4b 更新交接修复

安装助手最终检测到 WebView2 不可用时，现在在 ready 通知、备份和文件替换之前退出，并用专用结果通知发起方。发起方显示安装／修复组件和重试说明，恢复自动缓存的未尝试状态。普通失败和超时保持失败处理。重试采用独立助手与任务文件，原固定交接标记保留，避免重新解压正在交接的 payload；不清理任何旧尝试。

测试专用子进程注入 Runtime 缺失，执行与产品相同的预检和等待函数，验证中文提示、自动重试资格及下一次成功交接；不是卸载系统组件的实测。普通回归 776 项，安装进程组合专项 816 项（包含普通回归，不能相加），覆盖原迁移、升级、修复及自动安装。实际安装助手只运行假后台；真实 TrayHost 和音频写入未执行。

证据：`staging/stage54b-build.log`、`stage54b-final-tests.log`、`stage54b-process.log`、`stage54b-final.json`。最终编译完成后的第一次测试在原有临时配置文件替换处发生一次 IOException；失败日志另存 `stage54b-build-replace-failure.log`，未更改配置实现或删除测试文件，重跑普通及组合专项均通过，具体占用原因未确定。新后台只读 `--check-files --quiet` 返回 0；活动配置哈希一致、测试进程已退出。源代码改动仅更新交接及相关回归，未改音频核心或前端界面。

## 5.4c 当前本地完整包（2026-10-17）

后续本地验收使用 `staging/release-54c/v0.11.1/`，包含 5.4b 的更新交接修复。构建产品源码来自提交 `73e00ae39b2cd21e2823d4e012548486ae4db298`；开始时工作区干净，仅为本轮补写验收文档，未修改产品源码。编译、组包和校验均在新目录进行，5.3d 旧附件摘要保持不变。

| 附件 | SHA-256 |
| --- | --- |
| AudioSwitch-v0.11.1-win-x64.zip（旧格式过渡包） | `3825d75aa7378af1a4c31e86d48467f9e7349cdd78bb1cf92082ec165833731b` |
| AudioSwitch-Desktop-v0.11.1-win-x64.zip（Desktop 完整包） | `88f884b678c8f34d1a57f2907665274c6d3dfd8c9e3d161fd18cee2e03382f0f` |
| AudioSwitch.exe（同一后台） | `880ae31fa914475018c941a5a06a5985e36048355b5576ba7298e6e799ab05bb` |

- 本轮构建的普通回归 776 项通过，包含新的组件预检交接用例；前端类型检查、桌面网页和 Tauri release 构建通过，许可重新归集。
- 生产解压器接受旧 8 文件／Desktop 14 文件 ZIP；包内只读完整性检查通过。两份 ZIP、独立后台与本轮新编译的后台摘要一致。
- 新包隔离安装专项 782 项通过（含 776 项普通回归，不能相加）：14 文件安装后与来源逐个一致，旧程序备份及活动／隔离用户配置保留。只执行只读检查，不启动生产 TrayHost。
- 最终核对真实配置未变，项目测试进程为 0，产品源码未在构建期间变化。页面逻辑未改，不重复运行此前的窗口／缩放测试，也不把历史截图或内存结果当作本轮测量。

源码及路径记录：`staging/stage54c-source.json`、`stage54c-paths.json`；最终摘要与边界核对：`stage54c-final.json`。构建日志：`stage54c-package.log`，完整后台回归位于 `staging/ui-candidate-300babf7107843998b41cd6d4ff8f6bf/backend-build.log`。实包专项：`stage54c-real-package.log`，事务结果位于 `staging/tdc7e0916/desktop-real-ac3422e8/result.json`。组包清单位于 `staging/desktop-release-80fe6864a3b44081b0e4b2a2ad466960/verification.json`。

这是仍沿用 0.11.1 的本地验证包，不是新的正式版本；不可用来覆盖现有 Release。没有替换日常 bin、发布、提交或合并。表中实际网络迁移、新机器 Runtime、生产后台与音频／硬件实测仍待验，本项结束不代表可以正式替换日常版本。

## 5.4f 当前本地完整包（2026-10-18）

最终目录：`staging/release-54f-final/v0.11.1/`。基准提交为 `ee492e89aba77855ff05929d3bbb656ec814ad79`，包含尚未提交的 index.html、App、PromptApp 和 Settings 文案修正，不能宣称仅从该提交构建。实际文件摘要与完整前端差异保存在 `staging/stage54f-final-source.json`、`stage54f-final-frontend.patch`。最初包 `staging/release-54f/v0.11.1/` 未包含本轮补充的关于按钮／兼容提示修正，仅作历史记录；5.4c 包也保留。

| 附件 | SHA-256 |
| --- | --- |
| AudioSwitch-v0.11.1-win-x64.zip（8 文件） | `4b06fbaad3c08a38959cbf5620d556ec45d8cb7dd411af3861e867518c36f75a` |
| AudioSwitch-Desktop-v0.11.1-win-x64.zip（14 文件） | `370cabe6a85f04ab0a23b90bdbdad792b67e2ac44212a30f11fa05bee47788e5` |
| AudioSwitch.exe | `bf359442184edcb14914245ec09b71c5ddd1f64dc58e77a128db0c29072ea36f` |

本轮最终 776 项后端普通检查、91 项前端测试通过；TypeScript、桌面网页和 Tauri release 构建通过，归集 17 个前端运行依赖、shadcn、247 个 Rust 依赖及标准库／WebView2 Loader 许可。两份 ZIP 通过生产解压器及包内只读文件检查，独立 EXE 与两份 ZIP 中后台一致。没有重跑未改动的安装事务或有副作用实机测试，也不把历史项目计入本轮。

实际运行最终 Desktop 包的原生面板及设备提示，后端为既有模拟入口，无真实 AudioService。两个 WebView2 文档标题分别为“声间 · 设备面板”“声间 · 设备提示”；关于按钮为“关于声间”，设置页为“桌面版”。原生无障碍树与设置文案存于 `staging/stage54f-final-main-ax.txt`、`stage54f-final-prompt-ax.txt`、`stage54f-final-settings-ax.txt`。旧后台／操作结果不确定的提示分支通过源码及现有回归核对，未在本轮原生窗口中人工触发。

入口正常退出后，记录中的 11 个入口／窗口相关进程全部退出，配置哈希一致。会话记录为 `frontend/output/desktop/stage54f-final-native.json`。此前首次包的 native 记录及源码记录保留，不能混作最终包证据。

完整构建日志：`staging/stage54f-final-package.log`；后台日志：`staging/ui-candidate-dbd48bb465c641c7a2cefa0a8bd5bd7d/backend-build.log`；前端测试：`staging/stage54f-frontend-tests.log`；附件明细：`staging/desktop-release-c38a6e0fb9ca4ba092790acdd086d9b2/verification.json`；最终核对：`staging/stage54f-final-check.json`。解压目录：`staging/t47b6424f/release-verify-1b7e203b12324683b6645db5dfc44ea8/desktop`。

本包仅用于本地验证，仍是 0.11.1；未递增版本、替换日常程序或上传。标题原生验收项已关闭，真实保存、导入恢复、主动音频操作、硬件及连续升级安装等仍待单独验收。

## 5.4g 原生备份只读验收（2026-10-18）

使用 5.4f-final 解压后的实际面板，后台是本轮 `staging/t54g/AudioSwitch.Tests.exe --panel-readonly-host`；只增加生产 `PanelImport.Preview` 纯解析能力，其余写请求仍被拒绝。产品源码未改，前端包未重建。当前源码基准为 `fec78c4`，本轮未提交改动仅在测试工具和文档。

- 原生导出选择按 Escape 取消后，返回可用抽屉，没有成功提示；再次导出中文路径成功。6032 字节 JSON 与活动配置逐值严格一致（含数组顺序）。
- 原生导入选择取消后没有摘要；选择刚导出的文件得到 14 台设备／4 台离线、8 项设备预设、3 项 Dolby 预设、8 项规则。取消摘要后重新打开，没有上次摘要及确认按钮。
- 只读入口审计仅有 5 次 snapshot、2 次 exportSettings、1 次 previewImport，没有写操作请求。前后配置、默认角色、音量、空间音效一致，读错误为 0；未读取 Dolby 当前驱动参数。
- 正常关闭面板释放所记录的 7 个 UI／WebView2 进程，只读入口正常退出。没有 TrayHost 自动规则、配置恢复、确认导入或音频写入。
- 普通 C# 回归 776 项通过。没有修改生产 UI/Rust，不重新计入前端或 Rust 的历史测试。

证据：`staging/stage54g-build.log`；会话 `staging/stage54g-desktop-readonly.json`；最终文件位于 `staging/t54g/`：`audit-summary.json`、`content-check.json`、`final-process-check.json`、`ui-processes.json`、`export-cancel-ax.txt`、`export-success-ax.txt`、`import-picker-cancel-ax.txt`、`import-preview-ax.txt`、`import-reopened-ax.txt`。完整音频审计为 `readonly-session-b3d2be306fec45d6ae480449dbf2ea3d.json`。导出的真实备份和设备记录仅留在忽略的 staging，不加入源码。

初次受限环境面板退出、跨上下文连接不可用，不作为产品失败或通过证据；该会话已经正常结束并保留记录。改为同一用户桌面上下文后通过上述验收。真实“仅保存”、确认导入／完整备份恢复、文件错误分支及实际安装仍按上表待验。

## 5.4h 异常备份与提示修正（2026-10-18）

实际 Desktop 面板通过原生文件选择框拒绝损坏 JSON、非法 UTF-8 和 1048577 字节文件，没有摘要或确认入口；按钮可继续使用。损坏 JSON 原先显示解析器细节及部分文件文本，本轮在 PanelImport.Preview 的解析失败边界改为重新选择备份的中文提示，不再保留包含文件文本的内部异常。PreferenceStore 的类型、范围、版本和大小校验未改。

提示修正后使用 `staging/t54h-build/AudioSwitch.Tests.exe` 只读入口复验：正常备份可在错误后重新预览，再选损坏文件会清除有效摘要和确认按钮。未确认导入、未运行 TrayHost。新构建普通 C# 回归 778 项通过；原有 5.4f-final 面板未改，既有双包尚不包含本次 C# 修正，下一次打包应从最新源码重新生成。

两次会话（修正前／后）的审计分别记录 snapshot 4 次、previewImport 1 次，以及 snapshot 4 次、previewImport 3 次；编码／大小错误在 Rust 本地读取时被拒绝，不发 previewImport。只包含这两类只读请求。各次前后配置、默认角色、音量与空间音效一致，读错误为 0；没有读取 Dolby 当前驱动参数。两次各记录 7 个 UI 进程，最终均退出，测试输入文件哈希不变。

证据：`staging/stage54h-build.log`；`staging/stage54h-readonly.json`／`stage54h-fixed-readonly.json`；`staging/t54h/` 内 `invalid-json-ax.txt`、`invalid-utf8-ax.txt`、`oversize-ax.txt`、`invalid-json-fixed-ax.txt`、`valid-after-error-ax.txt`、`error-after-valid-ax.txt`、`audit-before-fix.json`、`audit-fixed.json`、`final-check.json`。完整审计路径由上述摘要记录。源代码基准为 d4ad795 加本轮未提交改动。

本项不代替真实保存、确认导入／恢复、导出写入失败及活动配置覆盖保护、音频操作或安装验收。未更新日常 bin、版本号或发布附件，未提交／合并。

## 5.4i 当前本地完整包（2026-10-19）

目录：`staging/release-54i/v0.11.1/`。构建源码提交 `c14dc1557c17bae3a89694099d666e3ff654ef6c`，包含 5.4h 导入预览提示修正；构建前工作区干净，本轮只新增验收文档改动。构建输入哈希清单为 `staging/stage54i-source.json`，最终核对 `stage54i-final-check.json` 确认输入、旧包、日常 bin 和活动配置均未改变，没有项目进程残留。

| 附件 | 字节 | SHA-256 |
| --- | ---: | --- |
| AudioSwitch-v0.11.1-win-x64.zip（8 文件） | 389566 | `e6dc45a845627c348da5e0cedefda3c189b21b25148557b272fa73fcbbbb4b62` |
| AudioSwitch-Desktop-v0.11.1-win-x64.zip（14 文件） | 2724392 | `b74f1b7486b124bbd791aad06b7d1fea7bcc8ce2e8486aee4b1c1393ed591f49` |
| AudioSwitch.exe | 584192 | `72d9bd4b7f009e4e2770c696580156eea511233523887a85643ec17d5c556f4b` |

本轮普通 C# 回归 778 项、TypeScript 检查、桌面网页构建、Tauri release 构建通过；归集 17 个前端运行依赖及 shadcn、247 个 Rust 依赖与标准库／Loader 许可。候选清单校验通过；两份正式格式 ZIP 经生产解压器及包内只读 `--check-files --quiet` 检查通过，包内后台与独立 EXE 摘要一致。最终目录只有上述三个附件。

另在独立 Windows PowerShell 进程中加载本轮 EXE，仅调用 PanelImport.Preview 纯逻辑，损坏备份返回新版中文提示，异常链不含测试文件标记；没有调用 Main、TrayHost 或音频服务。此项证明新包包含修正，不替代原生界面及真实后台联调。本轮未重新运行前端／Rust 单元测试、窗口／内存测试、安装事务或音频测试。

日志：`staging/stage54i-build.log`、`stage54i-message.log`；完整普通回归日志：`staging/ui-candidate-4fbac9fe38304a9a83473114f0de4069/backend-build.log`；附件验证：`staging/desktop-release-aa7e3f51794a4de0942601cb9507ee15/verification.json`；生产解压目录：`staging/tb41675b9/release-verify-6a7924a2cda2421eaf8aaec1a575adbc`。Rust 曾输出用户目录 canonicalize 警告，最终构建及包校验成功；未通过更改全局环境消除警告。

仍为 0.11.1 本地验证产物，不能覆盖现有正式 Release。未更新 bin、配置或音频，未安装、提交、合并、发布。真实确认导入／恢复、主动音频操作、硬件、新机器 Runtime、连续升级安装等仍按表中范围待验。

## 5.4j Windows 备份文件失败与重试（2026-10-20）

本轮补充三个 Rust 回归，直接调用生产 backup::read/write，操作 staging/ui-target/backup-tests-* 内新建的临时文件。未启动 TrayHost、面板或原生文件框，不使用真实配置内容。

- 用 Windows share_mode(0) 持有文件句柄，真实触发读取及替换时的共享冲突。关闭句柄后确认原备份字节完整、失败临时文件保留且内容完整，再次导出成功；重试不删除旧失败文件。
- 将单个临时备份设置为 Windows 只读属性，验证替换失败且原备份保留；恢复该文件原属性后导出成功。此项不等于目录 ACL 拒绝访问测试。
- 通过大小写及包含子目录/../的路径选择活动配置，均在创建临时文件前拒绝；活动配置尚未创建时，同样保护其规范化父目录与文件名。所有活动配置路径均为测试夹具，未尝试覆盖真实 settings.json。

执行 frontend/desktop.ps1 -Command test：30 项通过、0 失败、3 项按既有标记忽略（Dolby 辅助进程、维护及升级进程专项），不计为通过。日志 staging/stage54j-rust-tests.log；配置与验收 ZIP 不变的记录 staging/stage54j-final.json。既有用户目录 canonicalize 警告仍出现，测试编译与执行成功。

仅在 backup.rs 的 cfg(test) 中增加测试，并更新本表、阶段记录和手动清单。未改产品逻辑、版本、依赖或音频核心，不需要重建交付包；staging/acceptance-2026-10-19/ 的 5.4i 验收版保持原字节。原生文件框操作、页面错误提示/按钮恢复、目录 ACL 及其他实机项目仍待验。未提交、合并或发布。

## 5.4k 原生导出错误与恢复（2026-10-20）

直接运行交付目录 staging/acceptance-2026-10-19/app/ 的 5.4i 面板，连接 staging/tb41675b9/AudioSwitch.Tests.exe --panel-readonly-host。该入口只读真实设备和配置，拒绝保存、切换及应用，不运行 TrayHost 自动规则；本轮未点击确认导入。

- 通过 Windows 保存框选择本轮临时文件“占用备份.json”。测试进程持有允许读写但不允许删除共享的句柄，实际原子替换失败；面板显示“导出未完成，原目标文件未被截断”，没有成功提示，取消／选择保存位置／关闭按钮重新可用。释放句柄后独立核对原夹具字节未变。
- 同一抽屉再次选择相同路径，确认替换后显示“备份已导出”；6032 字节 JSON 使用 Node assert.deepStrictEqual 与活动配置逐值核对一致。失败临时文件保留，不清理。
- 重新打开抽屉选择本轮“只读备份.json”，Windows 文件框明确提示文件只读并要求换名。确认提示、取消选择后返回可用抽屉，没有导出成功提示。原文件字节未变，结束时仅恢复该测试文件原可写属性。此拒绝发生在 Windows 文件选择框，不冒充 Rust 写入失败分支；该分支已由 5.4j 覆盖。
- 面板正常关闭后，记录的 7 个 UI／WebView2 进程全部退出，只读入口通过专用退出事件正常收尾。审计只有 snapshot 4 次、exportSettings 3 次；前后配置、默认角色、音量、空间音效一致，读错误为 0。未读取 Dolby 实时参数。

证据在 staging/t54k/：locked-export-ax.txt、retry-export-ax.txt、readonly-export-ax.txt、readonly-cancel-ax.txt、before-retry.txt、ui-processes.json、final-check.json；入口记录 staging/stage54k-desktop-readonly.json。导出的真实配置和审计只留在忽略目录，不加入源码。最初受限上下文的入口不能被桌面面板访问，已正常退出；改为同一用户桌面上下文完成上述流程，前一次会话不算产品失败或完整通过。

本轮仅更新验收文档，没有产品代码或构建变更，交付 ZIP 摘要不变；不重复运行未改动的单元测试，不替换日常 bin、不提交／合并／发布。目录 ACL、活动配置覆盖的桌面反馈、导入占用错误，以及真实写入和完整升级仍按上表待验。
