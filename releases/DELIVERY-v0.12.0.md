# v0.12.0 本地交付记录

2026-10-24：三个待发布附件已生成并通过校验，**尚未提交源码、打标签、推送或发布**。日常 `bin/` 和真实配置未改变。线上最新正式版只读查询仍为 [v0.11.1](https://github.com/EthenGod/AudioSwitch/releases/tag/v0.11.1)。

## 附件

待上传目录为 `releases/v0.12.0/`，里面只有这三个文件；本说明和发行说明放在目录外。

| 文件 | 字节 | SHA-256 |
| --- | ---: | --- |
| `AudioSwitch-Desktop-v0.12.0-win-x64.zip` | 2723509 | `d3b0cdda3b621d452def156def2b4c081bca11d341bd0d491f0388270838a033` |
| `AudioSwitch-v0.12.0-win-x64.zip` | 389563 | `504a166ce52b325b50afd39a1557e63d550cb4254abf08ff8539bfebf395ae3f` |
| `AudioSwitch.exe` | 584192 | `8b883ac965da9ab5432226ffcaee31129e3f3c259a830a14c62ead2ef7c24a4d` |

直接使用新界面选 **Desktop ZIP**，完整解压后运行 `AudioSwitch.exe`。旧名 ZIP 是过渡版，单 EXE 也是过渡后台。发布时两个 ZIP 必须同时保留，不能把 Desktop 包改名覆盖旧包。

## 源码与构建

- 分支：`refactor/ui`；构建基点 `291bf083d1c3e488f761bba773d1b6cb782d0e48` **加当前未提交改动**，不能只给这个旧提交打 v0.12.0 标签。
- 本轮产品改动仅为 C#／npm／Tauri／Cargo 应用版本及对应锁文件同步到 0.12.0、侧栏和设置页共用前端版本、浏览器更新示例文案同步。没有更改音频核心或更新事务逻辑，也没有升级依赖。
- 必须纳入源码的新增文件包括 `frontend/src/version.ts`；迁移和交付文档也需一并保留。提交时不要纳入 staging、bin、个人配置或构建附件。
- 执行 `release.ps1 -Desktop`，使用独立 Node 24.22.1 和项目内 Rust 环境；没有改全局运行时。构建过程的 216 个输入文件摘要前后相同，留在 `staging/release012-8601c614/before.json`；该目录还保留 `source.patch`、新增版本源文件副本及 `final.json`。
- 完整打包记录：`staging/desktop-release-7182afbbc0ea4a38b98d4757302afec5/verification.json`。最终附件来自此次新构建，没有对旧包仅改名。

## 本轮验证

- 778 项 C# 普通检查、14 个测试文件中的 91 项前端测试通过；TypeScript、Vite 和 Tauri release 构建成功。
- 生产解压器分别校验 8 文件过渡包和 14 文件 Desktop 包；解出的 `--check-files --quiet` 通过；两份 ZIP 中的后台与独立 EXE 摘要一致。完整第三方许可已归集。
- 使用新包真实面板连接模拟宿主，检查侧栏和设置页的 v0.12.0 文本及窗口截图；没有启动正式音频后台。结束后 9 个宿主／界面相关进程全部退出，真实配置摘要保持一致。记录：`frontend/output/desktop/release012-version.json`。
- 日常 bin 文件及真实配置与构建前摘要一致。没有重跑音频写入或迁移演练；先前核心操作和本地迁移证据继续保留在[迁移实测](../frontend/MIGRATION-ACCEPTANCE.md)，不冒充新版本的重复实机测试。

## 发布前最后一步

1. 用户确认源码提交／推送与正式发布范围；不自动合并分支。
2. 提交并推送与这组附件匹配的完整源码，确认构建输入未变，再将 v0.12.0 标签指向对应提交；如果产品源码变化，重新构建受影响的交付包。
3. 使用[发行说明](RELEASE-NOTES-v0.12.0.md)，上传上述三个附件；核对 GitHub 给出的 SHA-256 摘要和源码入口。正式发布前移除发行说明的“待发布稿”状态提示，不删未验证范围。
4. 发布后完成一次旧正式版 → 过渡版 → Desktop 的真实 GitHub 连续升级。该步骤目前未执行，不以本地包校验代替。
