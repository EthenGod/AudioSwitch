use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{sync::{Arc, Mutex, atomic::{AtomicBool, AtomicU64, Ordering}}, process::Stdio, time::Duration};
use std::os::windows::io::{AsRawHandle, FromRawHandle, OwnedHandle};
use tokio::{io::{AsyncReadExt, AsyncWriteExt}, process::Command, sync::Notify, time::{timeout, sleep}};
use windows_sys::Win32::System::JobObjects::*;

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "PascalCase", deny_unknown_fields)]
pub struct Profile {
    pub main_profile: Option<u8>, pub sub_profile: Option<u8>,
    pub enabled: Option<bool>, pub surround: Option<bool>, pub dialog: Option<bool>, pub leveler: Option<bool>, pub auto_switch: Option<bool>,
    pub surround_strength: Option<f64>, pub dialog_strength: Option<f64>, pub leveler_strength: Option<f64>,
    pub ieq: Option<u8>, pub eq: Option<Vec<i32>>,
}
impl Profile {
    pub fn validate(&self) -> Result<(), String> {
        if !matches!(self.main_profile, None | Some(0) | Some(4))
            || (self.main_profile == Some(4) && !matches!(self.sub_profile, Some(4) | Some(5)))
            || (self.sub_profile.is_some() && self.main_profile != Some(4))
            || (self.ieq.is_some() && (self.main_profile != Some(0) || !matches!(self.ieq, Some(0) | Some(2))))
            || self.eq.as_ref().is_some_and(|eq| self.main_profile != Some(4) || eq.len() != 20 || eq.iter().any(|v| *v < -192 || *v > 192))
            || [self.surround_strength,self.dialog_strength,self.leveler_strength].iter().flatten().any(|v| !v.is_finite() || *v < 0.0 || *v > 1.0) {
            return Err("Dolby 方案参数无效，请检查预设、强度与均衡器。".into());
        }
        Ok(())
    }
}
pub fn require_device(snapshot: &Value, id: &str, current: bool) -> Result<(), String> {
    if snapshot["PanelApiVersion"].as_u64().unwrap_or(0) < 4 { return Err("当前后台不支持新 Dolby 编辑器，请使用新版后台。".into()); }
    if id.is_empty() || id.len() > 2048 { return Err("无效的设备标识。".into()); }
    let online = snapshot["State"]["Devices"].as_array().into_iter().flatten();
    let known = snapshot["Preferences"]["DeviceOrder"].as_array().into_iter().flatten();
    if !online.chain(known).any(|d| d["Id"] == id && d["Flow"] == 0) { return Err("请选择已记录的输出设备。".into()); }
    if current && (snapshot["State"]["Defaults"]["0:1"] != id || snapshot["DolbyApplying"] == true) { return Err("请等待音效应用完成，并选择当前输出设备再读取。".into()); }
    Ok(())
}
pub fn save_request(snapshot: &Value, id: &str, profile: &Option<Profile>, expected: &Option<Profile>) -> Result<Value, String> {
    require_device(snapshot, id, false)?;
    if let Some(p) = profile { p.validate()?; } if let Some(p) = expected { p.validate()?; }
    Ok(json!({"Action":"saveDolbyProfile","DeviceId":id,"DolbyProfile":profile,"ExpectedDolbyProfile":expected,"Value":false}))
}
fn read_request(id: &str) -> Value { json!({"DeviceId":id,"Profile":null,"OwnerPid":std::process::id()}) }
#[derive(Clone)]
struct ReadJob { token: String, view: Arc<Mutex<Value>>, cancel: Arc<Notify>, running: Arc<AtomicBool>, #[cfg(test)] pid: u32 }
#[derive(Default)]
pub struct Reader { job: Mutex<Option<ReadJob>>, next: AtomicU64, closed: AtomicBool }
impl Reader {
    pub async fn start(&self, snapshot: &Value, id: &str) -> Result<Value, String> {
        require_device(snapshot, id, true)?;
        if self.closed.load(Ordering::SeqCst) { return Err("编辑器已经关闭。".into()); }
        if self.job.lock().unwrap().as_ref().is_some_and(|j| j.running.load(Ordering::SeqCst)) { return Err("正在读取 Dolby，请等待或取消。".into()); }
        let path = crate::maintenance::backend_path(snapshot)?;
        // Only the original capture path is exposed: Profile is always null.
        let raw = unsafe { CreateJobObjectW(std::ptr::null(), std::ptr::null()) };
        if raw.is_null() { return Err("无法准备只读辅助进程。".into()); }
        let owner = unsafe { OwnedHandle::from_raw_handle(raw) };
        let mut limits: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = unsafe { std::mem::zeroed() };
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if unsafe { SetInformationJobObject(owner.as_raw_handle(), JobObjectExtendedLimitInformation, &limits as *const _ as *const _, std::mem::size_of_val(&limits) as u32) } == 0 { return Err("无法设置辅助进程退出保护。".into()); }
        let mut child = Command::new(&path).arg("--dolby-worker").current_dir(path.parent().ok_or("后台目录无效。")?)
            .creation_flags(0x08000000).stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null()).kill_on_drop(true).spawn().map_err(|e| e.to_string())?;
        if unsafe { AssignProcessToJobObject(owner.as_raw_handle(), child.raw_handle().ok_or("无法确认辅助进程。")?) } == 0 { return Err("无法绑定辅助进程退出保护。".into()); }
        let mut input = child.stdin.take().ok_or("无法读取辅助进程输入。")?;
        input.write_all(format!("{}\n", read_request(id)).as_bytes()).await.map_err(|e| e.to_string())?;
        drop(input);
        let mut output = child.stdout.take().ok_or("无法读取辅助进程结果。")?;
        let token = self.next.fetch_add(1, Ordering::SeqCst).to_string();
        let initial = json!({"Token":token,"Status":"running","Message":"正在读取当前 Dolby…"});
        let job = ReadJob { token, view:Arc::new(Mutex::new(initial.clone())), cancel:Arc::new(Notify::new()), running:Arc::new(AtomicBool::new(true)), #[cfg(test)] pid:child.id().unwrap() };
        { let mut slot = self.job.lock().unwrap(); if self.closed.load(Ordering::SeqCst) { return Err("编辑器已关闭，读取已停止。".into()); } *slot = Some(job.clone()); }
        tauri::async_runtime::spawn(async move {
            let result: Result<Value, String> = tokio::select! {
                _ = job.cancel.notified() => Err("cancelled".into()),
                result = timeout(Duration::from_secs(15), async {
                    let mut bytes = Vec::new(); (&mut output).take(65537).read_to_end(&mut bytes).await.map_err(|e| e.to_string())?;
                    if bytes.len() > 65536 { return Err("Dolby 返回结果过大。".into()); }
                    if !child.wait().await.map_err(|e| e.to_string())?.success() { return Err("Dolby 读取进程异常退出。".into()); }
                    decode(&bytes)
                }) => result.unwrap_or_else(|_| Err("读取超时，原草稿已保留。".into())),
            };
            let mut value = match result {
                Ok(value) => value,
                Err(error) => {
                    // This child can only capture; killing it cannot interrupt an audio write.
                    let stopped = child.kill().await;
                    json!({"Status":if error == "cancelled" && stopped.is_ok() {"cancelled"} else {"error"},"Message": stopped.err().map(|e| format!("无法确认读取进程退出：{e}")).unwrap_or_else(|| if error == "cancelled" {"读取已取消，原草稿已保留。".into()} else {error})})
                }
            };
            drop(owner);
            value["Token"] = json!(job.token); *job.view.lock().unwrap() = value; job.running.store(false, Ordering::SeqCst);
        });
        Ok(initial)
    }
    fn find(&self, token: &str) -> Result<ReadJob, String> { self.job.lock().unwrap().as_ref().filter(|j| j.token == token).cloned().ok_or("读取任务已失效，请重新读取。".into()) }
    pub fn read(&self, token: &str) -> Result<Value, String> { Ok(self.find(token)?.view.lock().unwrap().clone()) }
    pub async fn cancel(&self, token: &str) -> Result<Value, String> {
        let job = self.find(token)?; job.cancel.notify_one();
        timeout(Duration::from_secs(3), async { while job.running.load(Ordering::SeqCst) { sleep(Duration::from_millis(25)).await; } }).await.map_err(|_| "尚未确认读取停止，请重试。".to_string())?;
        self.read(token)
    }
    pub fn close(&self) { self.closed.store(true, Ordering::SeqCst); if let Some(job) = self.job.lock().unwrap().as_ref() { job.cancel.notify_one(); } }
}
fn decode(bytes: &[u8]) -> Result<Value, String> {
    let value: Value = serde_json::from_slice(bytes.strip_prefix(&[0xef,0xbb,0xbf]).unwrap_or(bytes)).map_err(|_| "Dolby 未返回完整有效结果。")?;
    if let Some(error) = value["Error"].as_str() { return Err(error.into()); }
    let profile: Profile = serde_json::from_value(value["Profile"].clone()).map_err(|_| "Dolby 未返回有效方案，原草稿已保留。")?;
    profile.validate()?;
    Ok(json!({"Status":"ready","Message":"已填入当前方案；仅保存后才会更新设备预设。","Profile":profile}))
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn invalid_profiles_and_unknown_fields_rejected() {
        let mut p: Profile = serde_json::from_value(json!({})).unwrap();
        p.main_profile = Some(4); assert!(p.validate().is_err()); p.sub_profile=Some(4); p.eq=Some(vec![0;20]); assert!(p.validate().is_ok());
        p.eq=Some(vec![193;20]); assert!(p.validate().is_err());
        assert!(serde_json::from_value::<Profile>(json!({"Volume":10})).is_err());
    }
    #[test] fn capture_cannot_write_and_save_cannot_apply() {
        assert!(read_request("id")["Profile"].is_null());
        let snapshot = json!({"PanelApiVersion":4,"Preferences":{"DeviceOrder":[{"Id":"offline","Flow":0}]}});
        let request = save_request(&snapshot,"offline",&None,&None).unwrap();
        assert_eq!(request["Value"],false); assert_eq!(request["Action"],"saveDolbyProfile");
        assert!(require_device(&snapshot,"offline",true).is_err());
        assert!(require_device(&json!({"PanelApiVersion":3}),"offline",false).is_err());
    }
    #[test] fn invalid_capture_keeps_draft() { assert!(decode(br#"{"Profile":null}"#).is_err()); assert!(decode("{\"Error\":\"中文失败\"}".as_bytes()).unwrap_err().contains("中文失败")); }
    #[tokio::test]
    #[ignore = "Compile frontend/scripts/dolby-reader-fixture.cs into staging/dolby-reader-fixture/AudioSwitch.exe first; no audio access"]
    async fn isolated_capture_cancel_and_window_close() {
        use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE};
        use windows_sys::Win32::Foundation::WAIT_OBJECT_0;
        let path = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../staging/dolby-reader-fixture/AudioSwitch.exe").canonicalize().unwrap();
        for (index, id) in ["success", "failure", "waiting", "waiting"].into_iter().enumerate() {
            let snapshot = json!({"PanelApiVersion":4,"BackendExecutablePath":path,"State":{"Devices":[{"Id":id,"Flow":0}],"Defaults":{"0:1":id}}});
            let reader = Reader::default(); let initial=reader.start(&snapshot,id).await.unwrap(); let token=initial["Token"].as_str().unwrap();
            let job=reader.find(token).unwrap();
            let handle=unsafe { OpenProcess(PROCESS_SYNCHRONIZE,0,job.pid) }; assert!(!handle.is_null());
            let handle=unsafe { OwnedHandle::from_raw_handle(handle) };
            assert!(reader.cancel("stale").await.is_err());
            if id == "waiting" {
                if index == 3 { reader.close(); }
                else { reader.cancel(token).await.unwrap(); }
            }
            timeout(Duration::from_secs(4),async { while reader.read(token).unwrap()["Status"] == "running" { sleep(Duration::from_millis(20)).await; } }).await.unwrap();
            let result = reader.read(token).unwrap();
            assert_eq!(result["Status"], if id == "waiting" {"cancelled"} else if id == "failure" {"error"} else {"ready"});
            if id == "failure" { assert!(result["Message"].as_str().unwrap().contains("中文读取失败")); }
            assert_eq!(unsafe { WaitForSingleObject(handle.as_raw_handle(),0) },WAIT_OBJECT_0);
            reader.close(); assert!(reader.start(&snapshot,id).await.is_err());
        }
    }
}
