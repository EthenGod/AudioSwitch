# Release 待上传文件

当前开发版本为 **v0.12.1**，仍在添加新内容，暂不生成 Release 附件。v0.12.0 按测试版保留，原附件不替换。当前实现与验证见 [v0.12.1 开发记录](../frontend/VERSION-0.12.1.md)。

## 发行说明规范

以后从 [发行说明模板](RELEASE-NOTES-TEMPLATE.md) 复制，保存为 `RELEASE-NOTES-v版本号.md`。替换所有 `{{VERSION}}` 和内容占位符，删除编辑提示注释后，再粘贴到 GitHub Release。

- **尽量短，用大白话。** 正文以约 200–500 字为目标；只写用户能感知的变化，一条一句。不要堆技术名词、实现细节、提交编号、测试数量或开发过程；详细证据留在交付／验证记录中。
- **固定顺序：下载入口 → 更新内容 → 修复内容 → 已知问题（若有）。** 更新内容通常 2–4 条，修复通常 1–3 条；没有专项修复就如实写明，不用“优化若干问题”凑数。没有已确认的问题时删掉已知问题整节，不写“没有任何问题”。影响使用的兼容限制和必要升级提醒不能省略；未实测的关键链路只简短说明，不能冒充已通过。
- **优先引导下载 `AudioSwitch.exe`。** 顶部放醒目的蓝色图片按钮和普通文字备用链接，写明“下载到可写文件夹，保留文件名，双击运行”。完整包和源码作为次要入口，不让初次使用的人面对一堆同等显眼的下载选项。
- **按钮指向本版附件。** 使用 `https://github.com/EthenGod/AudioSwitch/releases/download/v版本号/AudioSwitch.exe`，不要用 `latest/download`，避免旧版说明下载到另一版。图片采用 [Shields.io 静态徽章](https://shields.io/badges/static-badge)，用 [GitHub Markdown 图片链接](https://docs.github.com/en/get-started/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax) 包成可点击按钮；不使用脚本、表单或自定义网页按钮。
- **下载指引必须符合附件实际能力。** v0.12.1 起，单 EXE 内含完整桌面包，首次运行展开到旁边的 `AudioSwitch` 文件夹并启动；展开后需保留整个文件夹。完整 ZIP 解压后运行其中的 `AudioSwitch.exe`。v0.12.0 的独立 EXE 能力不同，旧版说明保留原有提示。
- **发布前核对：** 版本号、附件名和按钮目标一致；附件上传完成后在 GitHub 预览按钮并实际点击核对；普通文字链接在图片加载失败时也可用。未发布草稿的附件链接尚不能公开下载。只改说明时不重新构建、不替换已验证的程序包、不自动发布。

## 打包与上传

同步 C#、前端、Tauri、Cargo 和锁文件中的应用版本。用户确认本版内容完成并要求生成附件后，在项目根目录运行：

```powershell
.\release.ps1
```

构建及测试通过后生成：

```text
releases/
  v下一版本/
    AudioSwitch.exe
    AudioSwitch-Desktop-v下一版本-win-x64.zip
```

只生成便捷展开 EXE 和完整 Desktop ZIP，不生成过渡版。`-Desktop` 仅保留为旧命令的兼容参数，输出相同。开发期间验证使用 `release.ps1 -OutputDirectory staging/自选验证目录`，不往 `releases/` 放开发附件。

EXE 内嵌同批完整 ZIP，展开的 14 个文件应与 ZIP 逐字节一致，外层 EXE 和包内主程序本身则不同。外层程序复用完整且同版本或更高版本的 `AudioSwitch` 子目录，不覆盖旧版或损坏目录；展开工作目录保留。旧 v0.11 客户端需手动下载到新文件夹，不再提供过渡升级路线。

两个 EXE 都内嵌声间 Logo，支持 16–256 像素文件图标；无需上传 ICO 文件。源码中的 `assets/AudioSwitch.ico` 必须随构建脚本一同提交，图标在计算程序完整性校验值之前嵌入。

同版本目录已存在时，脚本停止，不覆盖、不清理。`-OutputDirectory` 可指定另一个输出根目录，其下仍按版本创建子目录。历史包不会移动或删除。

获得相应授权后，发布前提交并推送与程序完全对应的所有源码（含新增文件、构建脚本及 `vendor/svcl/`），再创建匹配版本标签和 Release。上传该版本目录里的两个程序附件。发行说明保存在版本目录外，复制正文即可，不作为程序附件上传。源码通过 GitHub 自动生成的 `Source code (zip)` / `Source code (tar.gz)` 提供，不需要额外上传。

此目录用于保存上传原件。试运行时请把 EXE 复制到其他可写目录，以免展开文件混入待上传目录。
