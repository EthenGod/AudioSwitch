use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{path::PathBuf, process::Stdio, sync::{Arc, Mutex, atomic::{AtomicBool, AtomicU64, Ordering}}, time::Duration};
use tokio::{io::{AsyncReadExt, AsyncWriteExt}, process::{Child, Command}, sync::Notify, time::{sleep, timeout}};

#[derive(Clone, Copy, Deserialize, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Kind { Update, Files }
impl Kind { fn text(self) -> &'static str { match self { Self::Update => "update", Self::Files => "files" } } }
#[derive(Clone)]
struct Job { token: String, view: Arc<Mutex<Value>>, cancel: Arc<Notify>, running: Arc<AtomicBool> }
#[derive(Default)]
pub struct Maintenance { job: Mutex<Option<Job>>, next: AtomicU64, closed: AtomicBool }

pub fn backend_path(snapshot: &Value) -> Result<PathBuf, String> {
    if snapshot["PanelApiVersion"].as_u64().unwrap_or(0) < 3 { return Err("当前后台不支持新面板检查，请使用新版后台。".into()); }
    let path = PathBuf::from(snapshot["BackendExecutablePath"].as_str().ok_or("无法确认后台程序位置。")?);
    if !path.is_absolute() || !path.file_name().is_some_and(|name| name.to_string_lossy().eq_ignore_ascii_case("AudioSwitch.exe")) || !path.is_file() {
        return Err("后台程序位置无效，请重新打开后台后再试。".into());
    }
    Ok(path)
}
impl Maintenance {
    pub async fn start(&self, path: PathBuf, kind: Kind) -> Result<Value, String> {
        if self.closed.load(Ordering::SeqCst) { return Err("面板已经关闭，检查不会启动。".into()); }
        // Caller holds the bridge gate across preflight and registration.
        if self.job.lock().unwrap().as_ref().is_some_and(|job| job.running.load(Ordering::SeqCst)) { return Err("正在检查，请等待完成或先取消。".into()); }
        let mut child = Command::new(&path).arg("--panel-maintenance").current_dir(path.parent().ok_or("无效的后台目录。")?)
            .creation_flags(0x08000000).stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null()).kill_on_drop(true)
            .spawn().map_err(|e| format!("无法启动检查：{e}"))?;
        let mut input = child.stdin.take().ok_or("检查进程没有输入通道。")?;
        let mut output = child.stdout.take().ok_or("检查进程没有输出通道。")?;
        input.write_all(format!("{}\n", kind.text()).as_bytes()).await.map_err(|e| format!("无法发送检查请求：{e}"))?;
        let token = self.next.fetch_add(1, Ordering::SeqCst).to_string();
        let initial = json!({"Token":token,"Kind":kind,"Status":"running","Message":if matches!(kind, Kind::Update) {"正在检查正式版本…"} else {"正在检查后台运行文件…"}});
        let job = Job { token, view: Arc::new(Mutex::new(initial.clone())), cancel: Arc::new(Notify::new()), running: Arc::new(AtomicBool::new(true)) };
        {
            let mut slot = self.job.lock().unwrap();
            if self.closed.load(Ordering::SeqCst) { return Err("面板已经关闭，检查已停止。".into()); }
            *slot = Some(job.clone());
        }
        tauri::async_runtime::spawn(async move {
            let mut bytes = Vec::new();
            // Result is published only after the child has actually exited.
            let result = tokio::select! {
                _ = job.cancel.notified() => Err("cancelled".to_string()),
                result = timeout(Duration::from_secs(35), async {
                    (&mut output).take(1024 * 1024 + 1).read_to_end(&mut bytes).await.map_err(|e| e.to_string())?;
                    if bytes.len() > 1024 * 1024 { return Err("检查结果过大。".to_string()); }
                    let status = child.wait().await.map_err(|e| e.to_string())?;
                    if !status.success() { return Err("检查进程异常退出，请重试。".into()); }
                    decode(&bytes, kind)
                }) => result.unwrap_or_else(|_| Err("检查超时，已停止任务，请重试。".into())),
            };
            let mut value = match result {
                Ok(value) => value,
                Err(error) => {
                    let _ = input.write_all(b"cancel\n").await;
                    drop(input);
                    let stopped = stop_child(&mut child).await;
                    let cancelled = error == "cancelled" && stopped.is_ok();
                    json!({"Kind":kind,"Status":if cancelled {"cancelled"} else {"error"},"Message":
                        stopped.err().unwrap_or_else(|| if cancelled {"检查已取消，没有修改文件或配置。".into()} else {error})})
                }
            };
            value["Token"] = json!(job.token);
            *job.view.lock().unwrap() = value;
            job.running.store(false, Ordering::SeqCst);
        });
        Ok(initial)
    }
    fn find(&self, token: &str) -> Result<Job, String> {
        self.job.lock().unwrap().as_ref().filter(|job| job.token == token).cloned().ok_or("本次检查已失效，请重新开始。".into())
    }
    pub fn read(&self, token: &str) -> Result<Value, String> { Ok(self.find(token)?.view.lock().unwrap().clone()) }
    pub async fn cancel(&self, token: &str) -> Result<Value, String> {
        let job = self.find(token)?;
        job.cancel.notify_one();
        timeout(Duration::from_secs(5), async { while job.running.load(Ordering::SeqCst) { sleep(Duration::from_millis(25)).await; } }).await
            .map_err(|_| "尚未确认检查进程退出，请稍后再取消。".to_string())?;
        self.read(token)
    }
    pub fn cancel_all(&self) {
        self.closed.store(true, Ordering::SeqCst);
        if let Some(job) = self.job.lock().unwrap().as_ref() { job.cancel.notify_one(); }
    }
}
impl Drop for Maintenance { fn drop(&mut self) { self.cancel_all(); } }
async fn stop_child(child: &mut Child) -> Result<(), String> {
    if timeout(Duration::from_secs(2), child.wait()).await.is_ok_and(|result| result.is_ok()) { return Ok(()); }
    // Exact owned child handle only; this helper performs read-only work.
    child.kill().await.map_err(|e| format!("无法确认检查进程已停止：{e}"))
}
fn decode(bytes: &[u8], kind: Kind) -> Result<Value, String> {
    let bytes = bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(bytes);
    let value: Value = serde_json::from_slice(bytes).map_err(|_| "检查进程未返回完整有效结果。")?;
    if value["Kind"] != kind.text() || value["Message"].as_str().is_none_or(str::is_empty) {
        return Err("检查结果不完整，请重试。".into());
    }
    let status = value["Status"].as_str().unwrap_or("");
    let allowed = match kind { Kind::Update => ["available", "current", "ahead", "unavailable", "error", "cancelled"].as_slice(), Kind::Files => ["passed", "failed", "error", "cancelled"].as_slice() };
    if !allowed.contains(&status) { return Err("检查状态无法识别，请重试。".into()); }
    if ["available", "current", "ahead"].contains(&status)
        && (value["CurrentVersion"].as_str().is_none_or(str::is_empty) || value["LatestVersion"].as_str().is_none_or(str::is_empty)) {
        return Err("版本结果不完整，请重试。".into());
    }
    if ["passed", "failed"].contains(&status) && (value["Directory"].as_str().is_none_or(str::is_empty)
        || !value["Entries"].as_array().is_some_and(|items| !items.is_empty() && items.iter().all(Value::is_string))) {
        return Err("文件检查结果不完整，请重试。".into());
    }
    Ok(value)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn rejects_unsupported_backend_paths_and_commands() {
        assert!(serde_json::from_str::<Kind>("\"repair\"").is_err());
        assert!(serde_json::from_str::<Kind>("\"install\"").is_err());
        assert!(backend_path(&json!({"PanelApiVersion":2,"BackendExecutablePath":"C:\\Windows\\cmd.exe"})).is_err());
        assert!(backend_path(&json!({"PanelApiVersion":3,"BackendExecutablePath":"AudioSwitch.exe"})).is_err());
    }
    #[test] fn validates_utf8_results_instead_of_reporting_partial_success() {
        assert!(decode("{\"Kind\":\"files\",\"Status\":\"passed\",\"Message\":\"检查通过\",\"Directory\":\"test\",\"Entries\":[\"通过\"]}".as_bytes(), Kind::Files).is_ok());
        assert!(decode(b"{", Kind::Files).is_err());
        assert!(decode(b"{\"Kind\":\"files\",\"Status\":\"available\",\"Message\":\"wrong\"}", Kind::Files).is_err());
        assert!(decode(b"{\"Kind\":\"update\",\"Status\":\"current\",\"Message\":\"wrong\"}", Kind::Files).is_err());
    }
    #[tokio::test] async fn stale_token_cannot_cancel_another_job() {
        let manager = Maintenance::default(); let job = Job { token:"current".into(),view:Arc::new(Mutex::new(json!({}))),cancel:Arc::new(Notify::new()),running:Arc::new(AtomicBool::new(false)) };
        *manager.job.lock().unwrap() = Some(job.clone());
        assert!(manager.cancel("stale").await.is_err());
        assert!(timeout(Duration::from_millis(10), job.cancel.notified()).await.is_err());
    }
    #[tokio::test] async fn closing_window_prevents_late_preflight_from_spawning() {
        let manager = Maintenance::default(); manager.cancel_all();
        let error = manager.start(PathBuf::from("missing.exe"), Kind::Files).await.unwrap_err();
        assert!(error.contains("已经关闭"));
    }
    #[tokio::test]
    #[ignore = "Requires a freshly built staging/AudioSwitch.exe; read-only worker verification"]
    async fn staging_worker_completes_and_cancels_without_a_backend() {
        let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../staging/AudioSwitch.exe").canonicalize().unwrap();
        let manager = Maintenance::default();
        let task = manager.start(path.clone(), Kind::Files).await.unwrap();
        let token = task["Token"].as_str().unwrap();
        let result = timeout(Duration::from_secs(8), async {
            loop { let result = manager.read(token).unwrap(); if result["Status"] != "running" { break result; } sleep(Duration::from_millis(30)).await; }
        }).await.unwrap();
        assert_eq!(result["Status"], "passed", "{result}");
        let task = manager.start(path, Kind::Update).await.unwrap();
        let cancelled = manager.cancel(task["Token"].as_str().unwrap()).await.unwrap();
        assert_eq!(cancelled["Status"], "cancelled", "{cancelled}");
        assert!(manager.read(token).is_err());
    }
}
