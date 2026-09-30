# Release 待上传文件

修改 `src/AppVersion.cs` 的版本号，在项目根目录运行：

```powershell
.\release.ps1
```

构建及测试通过后会生成：

```text
releases/
  v0.11.0/
    AudioSwitch-v0.11.0-win-x64.zip
    AudioSwitch.exe
  v下一版本/
    AudioSwitch-v下一版本-win-x64.zip
    AudioSwitch.exe
```

每个版本目录只放两个待上传文件。ZIP 是完整程序包，供常规下载、应用内更新和修复使用；EXE 是同一主程序，可直接下载到可写目录运行，首次运行会释放必要文件。

ZIP 内的 EXE 和独立 EXE 都内嵌声间 Logo，支持 16–256 像素文件图标；无需上传 ICO 文件。源码中的 `assets/AudioSwitch.ico` 必须随构建脚本一同提交，图标在计算程序完整性校验值之前嵌入。

同版本目录已存在时，脚本停止，不覆盖、不清理。`-OutputDirectory` 可指定另一个输出根目录，其下仍按 `v版本号` 分目录。历史 `artifacts/` 中的旧包不会被移动或删除。

发布前提交并推送与程序完全对应的所有源码（含新增文件、构建脚本及 `vendor/svcl/`），再创建匹配版本标签和 Release。只上传该版本目录里的 ZIP 和 EXE。源码通过 GitHub 自动生成的 `Source code (zip)` / `Source code (tar.gz)` 提供，并在发布说明中注明；这些源码入口不需要额外上传。

此目录用于保存上传原件。试运行时请把 EXE 复制到其他可写目录，以免首次运行释放的配套文件混入待上传目录。
