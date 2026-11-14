# 翻译指南 / Translation guide

主面板、设备提示、托盘菜单、原生窗口和程序消息共用此目录。翻译是 UTF-8 JSON，不需要修改 C#、Rust 或 React。中文为基准；未完成的词条回退到中文。

## 添加或修正翻译

1. 修正现有翻译时，只改 `en.json` 或 `ja.json` 的值。不要改左侧的中文键。
2. 添加语言时，在 `frontend/` 运行 `npm run locale:add -- fr Français`。命令创建空词条，并注册语言的原生名称。已有文件不会被覆盖。
3. 翻译新文件的全部值。可以参考 `zh-CN.json` 和 `en.json`。
4. 运行 `npm run locale:check`。检查缺失、重复、多余的词条和占位符。
5. 运行 `npm run dev`，打开 `/preview.html?lang=fr`。设置、抽屉和提示均使用模拟数据。设备提示用 `/preview.html?view=prompt&lang=fr`，首次选择用 `/preview.html?first-run`。
6. 提交语言文件和 `languages.json` 的改动，并附深浅色截图。不要提交构建产物或本机配置。

`languages.json` 中的 `id` 是稳定语言标识，`name` 是原生名称，`match` 是小写系统语言前缀。`en` 会匹配 `en-US`、`en-GB`；区域变体可用 `pt-BR` 等 ID，并将 `match` 设为 `["pt-br"]`。按用户的系统语言顺序匹配，先精确 ID，再匹配前缀。没有匹配项时使用中文。不要给两个语言注册相同前缀。

## 文案规则

- 保留 `{0}`、`{1}` 等占位符。可以换位置，不能改编号或遗漏。不要拼接词序；完整句子用一个词条。
- 左侧中文是查找键，不是显示值。少量键带语境，例如 `关闭开关` 的中文值是 `关闭`，英文是 `Off`；关闭窗口的 `关闭` 则是 `Close`。
- 按钮用短文案。不要增加解释、营销文字或中点分隔符。错误要准确说明结果，不能把失败或未确认的恢复译成成功。
- 不翻译设备名、用户别名、端点 ID、路径、文件名、协议字段或用户提供的 Release 内容。`Dolby`、`GitHub`、`Windows` 等产品名称保留。
- 词条是纯文本。换行用 `\n`；不要插入 HTML、脚本或外部图片。
- 在 1100×740 和 980×680 下查看长文案、下拉框、底部按钮。文字应可换行，不能遮挡操作。新语言可能需要字体适配；当前布局已适配简体中文、英语和日语。

## 开发约定

- 新文案先加到 `zh-CN.json`，再补其他语言。React 使用 `t(source, ...args)`；C# 使用 `L.Text(source, ...args)`；Rust 使用 `localization::text` 或 `format`。
- 翻译函数在渲染或生成消息时调用。不要在模块加载时缓存翻译后的标题，也不要用翻译后的文本做 React key、状态判断或文件校验。
- 文件检查用 `EntryStatuses` 和 `PassedFiles` 判断结果，不能从“通过”或其译文反推结果。
- 构建会从注册表自动嵌入词条。新增语言不需要修改三个运行时的语言列表。运行时不联网取翻译，也不新增轮询或常驻任务。
- `Preferences.Language` 未确认时为空。首次启动按系统语言推荐，确认保存后才关闭选择框。设置页可随时更改；旧备份没有语言字段时保留当前选择。
- 新语言完成后运行 `npm run typecheck`、`npm test` 和 `npm run build:desktop`。维护者再运行根目录 `build.ps1 -Test -OutputDirectory staging` 与 `frontend/desktop.ps1 -Command test`。

## For community translators

All application surfaces share these catalogues. To add a language, run `npm run locale:add -- fr Français` from `frontend/`. Translate the values in `locales/fr.json`; keep the Chinese keys unchanged. The command also registers the language in `languages.json`. Never overwrite another language or commit local settings and build output.

Keep every numbered placeholder such as `{0}`. You may move placeholders to fit the sentence. Translate whole messages, not fragments. Use short, clear labels. Do not add HTML or translate device names, file paths, IDs or protocol values. The key `关闭开关` means **Off**; `关闭` means **Close**.

Run `npm run locale:check` before submitting. It reports missing and duplicate messages, unknown keys, and mismatched placeholders. Empty translations fall back to Chinese during development, but must be completed for submission. Run `npm run dev` and visit `/preview.html?lang=fr` to check the main panel. Use `/preview.html?view=prompt&lang=fr` for the device prompt. These previews never change audio or local settings. Check both themes and narrow windows, then include screenshots with your contribution.

Language IDs follow tags such as `en`, `ja` or `pt-BR`. `name` is the language's own name. The lowercase `match` list controls system-language matching: `["en"]` matches `en-US` and `en-GB`. User language preferences are tried in order; exact IDs take precedence over prefixes. Unsupported languages fall back to Chinese. Registration automatically includes your catalogue in all builds.
