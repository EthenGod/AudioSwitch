use std::{fs::{self, File, OpenOptions}, io::{Read, Write}, path::{Path, PathBuf}, sync::atomic::{AtomicU64, Ordering}};
use windows::{core::w, Win32::{Foundation::HWND, System::Com::{CoCreateInstance, CoInitializeEx, CoTaskMemFree, CoUninitialize, CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED}, UI::Shell::{Common::COMDLG_FILTERSPEC, FileOpenDialog, FileSaveDialog, IFileDialog, FOS_DONTADDTORECENT, FOS_FILEMUSTEXIST, FOS_FORCEFILESYSTEM, FOS_OVERWRITEPROMPT, FOS_PATHMUSTEXIST, FOS_STRICTFILETYPES, SIGDN_FILESYSPATH}}};
use serde_json::{json, Value};

const MAX_BYTES: usize = 1024 * 1024;
static NEXT: AtomicU64 = AtomicU64::new(1);
pub struct PendingImport { pub token: String, configuration: String, revision: String }
impl PendingImport {
    pub fn prepare(reply: &Value, path: &Path) -> Result<(Self, Value), String> {
        check_reply(reply)?;
        let configuration = reply["ConfigurationJson"].as_str().filter(|s| !s.is_empty() && s.len() <= MAX_BYTES).ok_or("后台未返回完整备份内容。")?.to_owned();
        let revision = reply["ConfigurationRevision"].as_str().filter(|s| s.len() == 64 && s.bytes().all(|b| b.is_ascii_hexdigit())).ok_or("后台未返回配置版本，请重新选择备份。")?.to_owned();
        let mut summary = reply["ImportPreview"].as_object().cloned().ok_or("后台未返回备份摘要。")?;
        for key in ["Devices", "Profiles", "Rules", "DolbyProfiles", "OfflineDevices"] {
            if summary.get(key).and_then(Value::as_u64).is_none() { return Err("备份摘要不完整。".into()); }
        }
        let token = NEXT.fetch_add(1, Ordering::Relaxed).to_string();
        summary.insert("Token".into(), json!(token));
        summary.insert("FileName".into(), json!(path.file_name().unwrap_or_default().to_string_lossy()));
        Ok((Self { token, configuration, revision }, Value::Object(summary)))
    }
    pub fn request(self) -> Value { json!({"Action":"importPreparedSettings", "ConfigurationJson":self.configuration, "ExpectedConfigurationRevision":self.revision}) }
}
pub fn check_reply(reply: &Value) -> Result<(), String> {
    for field in ["OperationError", "Error"] {
        if let Some(error) = reply[field].as_str().filter(|s| !s.is_empty()) { return Err(error.into()); }
    }
    Ok(())
}
pub fn require_supported(snapshot: &Value) -> Result<(), String> {
    if snapshot["PanelApiVersion"].as_u64().unwrap_or(0) < 2 { return Err("当前后台暂不支持导入导出，请使用新版后台。".into()); }
    Ok(())
}

struct ComApartment;
impl Drop for ComApartment { fn drop(&mut self) { unsafe { CoUninitialize(); } } }
/// Called on a blocking worker. Only the native chooser supplies a path.
pub fn choose(owner: isize, save: bool) -> Result<Option<PathBuf>, String> {
    unsafe {
        CoInitializeEx(None, COINIT_APARTMENTTHREADED).ok().map_err(|e| e.to_string())?;
        let _com = ComApartment;
        let result = (|| -> windows::core::Result<Option<PathBuf>> {
            let dialog: IFileDialog = CoCreateInstance(if save { &FileSaveDialog } else { &FileOpenDialog }, None, CLSCTX_INPROC_SERVER)?;
            let flags = FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_DONTADDTORECENT | FOS_STRICTFILETYPES
                | if save { FOS_OVERWRITEPROMPT } else { FOS_FILEMUSTEXIST };
            dialog.SetOptions(dialog.GetOptions()? | flags)?;
            dialog.SetFileTypes(&[COMDLG_FILTERSPEC { pszName: w!("声间配置备份 (*.json)"), pszSpec: w!("*.json") }])?;
            dialog.SetDefaultExtension(w!("json"))?;
            dialog.SetTitle(if save { w!("导出声间备份") } else { w!("选择声间备份") })?;
            if save { dialog.SetFileName(w!("AudioSwitch-backup.json"))?; }
            match dialog.Show(Some(HWND(owner as *mut _))) {
                Err(e) if e.code().0 as u32 == 0x800704c7 => return Ok(None),
                result => result?,
            }
            let name = dialog.GetResult()?.GetDisplayName(SIGDN_FILESYSPATH)?;
            let text = name.to_string();
            CoTaskMemFree(Some(name.0.cast()));
            Ok(Some(PathBuf::from(text?)))
        })();
        result.map_err(|e| format!("无法选择备份文件：{e}"))
    }
}
pub fn read(path: &Path) -> Result<String, String> {
    let mut bytes = Vec::new();
    File::open(path).and_then(|file| file.take((MAX_BYTES + 1) as u64).read_to_end(&mut bytes)).map_err(|e| format!("无法读取备份：{e}"))?;
    if bytes.len() > MAX_BYTES { return Err("配置文件最大支持 1 MiB。".into()); }
    let bytes = bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(&bytes);
    std::str::from_utf8(bytes).map(str::to_owned).map_err(|_| "备份不是有效的 UTF-8 文件。".into())
}
fn resolved(path: &Path) -> std::io::Result<PathBuf> {
    if path.exists() { fs::canonicalize(path) }
    else { Ok(fs::canonicalize(path.parent().ok_or_else(|| std::io::Error::other("missing parent"))?)?.join(path.file_name().ok_or_else(|| std::io::Error::other("missing filename"))?)) }
}
pub fn write(path: &Path, configuration: &str, active: &Path) -> Result<(), String> {
    if configuration.is_empty() || configuration.len() > MAX_BYTES { return Err("后台备份内容为空或超过 1 MiB。".into()); }
    // Canonicalize existing files and parents, including junction/symlink aliases.
    let target = resolved(path).map_err(|e| format!("无法访问导出位置：{e}"))?;
    let protected = resolved(active).map_err(|e| format!("无法确认活动配置位置：{e}"))?;
    if target.to_string_lossy().eq_ignore_ascii_case(&protected.to_string_lossy()) { return Err("不能将备份覆盖到正在使用的 settings.json，请选择其他文件名。".into()); }
    let temp = target.with_file_name(format!(".AudioSwitch-export-{}-{}.tmp", std::process::id(), NEXT.fetch_add(1, Ordering::Relaxed)));
    // Never truncate an existing destination. Failed temporary files are retained.
    (|| -> std::io::Result<()> {
        let mut file = OpenOptions::new().write(true).create_new(true).open(&temp)?;
        file.write_all(configuration.as_bytes())?;
        file.sync_all()?;
        drop(file);
        fs::rename(&temp, &target)
    })().map_err(|e| format!("导出未完成，原目标文件未被截断：{e}"))
}

#[cfg(test)]
mod tests {
    use super::*;
    fn directory() -> PathBuf {
        let root = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../staging/ui-target").join(format!("backup-tests-{}-{}", std::process::id(), NEXT.fetch_add(1, Ordering::Relaxed)));
        fs::create_dir_all(&root).unwrap(); root
    }
    #[test] fn bounded_utf8_and_atomic_export() {
        let root = directory(); let active = root.join("settings.json"); let output = root.join("备份.json");
        fs::write(&active, "original").unwrap(); fs::write(&output, "older backup").unwrap();
        write(&output, "{\"中文\":null}", &active).unwrap();
        assert_eq!(read(&output).unwrap(), "{\"中文\":null}");
        assert!(write(&active, "overwrite", &active).is_err());
        assert_eq!(fs::read_to_string(&active).unwrap(), "original");
        assert!(write(&output, &"x".repeat(MAX_BYTES + 1), &active).is_err());
        assert_eq!(read(&output).unwrap(), "{\"中文\":null}");
        fs::write(&output, [0xef, 0xbb, 0xbf, b'{', b'}']).unwrap(); assert_eq!(read(&output).unwrap(), "{}");
        fs::write(&output, [0xff]).unwrap(); assert!(read(&output).is_err());
        fs::write(&output, vec![b'x'; MAX_BYTES + 1]).unwrap(); assert!(read(&output).is_err());
    }
    #[test] fn preview_retains_full_payload_only_in_native_state() {
        let full = "{\"Dolby\":{\"Eq\":[1,2,3]},\"Volume\":null}";
        let (pending, preview) = PendingImport::prepare(&json!({"ConfigurationJson":full,"ConfigurationRevision":"A".repeat(64),"ImportPreview":{"Devices":2,"Profiles":1,"Rules":0,"DolbyProfiles":1,"OfflineDevices":1}}), Path::new("sample.json")).unwrap();
        assert!(preview.get("ConfigurationJson").is_none());
        let request = pending.request(); assert_eq!(request["ConfigurationJson"], full); assert_eq!(request["Action"], "importPreparedSettings");
        assert!(require_supported(&json!({"PanelApiVersion":1})).is_err());
        assert!(PendingImport::prepare(&json!({"Error":"invalid"}), Path::new("sample.json")).is_err());
    }
}
