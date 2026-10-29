use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::{path::PathBuf, process::Stdio, sync::{Arc, Mutex, atomic::{AtomicBool, AtomicU64, Ordering}}, time::Duration};
use tokio::{io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader}, process::{Child, Command}, sync::{mpsc, Notify}, time::{sleep, timeout, Instant}};

#[derive(Clone, Copy, Deserialize, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Kind { Update, Repair }
impl Kind { fn text(self) -> &'static str { match self { Self::Update => "update", Self::Repair => "repair" } } }
#[derive(Clone, Copy, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Action { Download, Install }
#[derive(Clone)]
struct Job {
    token: String, view: Arc<Mutex<Value>>, commands: mpsc::Sender<Action>, cancel: Arc<Notify>,
    running: Arc<AtomicBool>, committed: Arc<AtomicBool>,
}
#[derive(Default)]
pub struct Upgrade { job: Mutex<Option<Job>>, next: AtomicU64, closed: AtomicBool }
impl Upgrade {
    pub async fn start(&self, path: PathBuf, kind: Kind) -> Result<Value, String> {
        self.start_worker(path, kind, "--panel-upgrade").await
    }
    async fn start_worker(&self, path: PathBuf, kind: Kind, argument: &str) -> Result<Value, String> {
        if self.closed.load(Ordering::SeqCst) { return Err("面板已经关闭。".into()); }
        if self.job.lock().unwrap().as_ref().is_some_and(|j| j.running.load(Ordering::SeqCst) || j.committed.load(Ordering::SeqCst)) {
            return Err("已有维护任务；若已确认安装，请查看安装结果，勿重复提交。".into());
        }
        let mut child = Command::new(&path).arg(argument).current_dir(path.parent().ok_or("后台目录无效。")?)
            .creation_flags(0x08000000).stdin(Stdio::piped()).stdout(Stdio::piped()).stderr(Stdio::null())
            // Once installation is accepted the helper must survive the panel closing.
            .kill_on_drop(false).spawn().map_err(|e| format!("无法启动维护任务：{e}"))?;
        let mut input = child.stdin.take().ok_or("维护输入不可用。")?;
        let output = child.stdout.take().ok_or("维护输出不可用。")?;
        if let Err(e) = input.write_all(format!("{}\n", kind.text()).as_bytes()).await {
            drop(input); let _ = stop(&mut child).await; return Err(e.to_string());
        }
        let token = self.next.fetch_add(1, Ordering::SeqCst).to_string();
        let initial = json!({"Token":token,"Kind":kind,"Status":"checking","Message":"正在检查…","CanDownload":false,"Bytes":0,"Total":0});
        let (tx, mut rx) = mpsc::channel(1);
        let job = Job { token, view:Arc::new(Mutex::new(initial.clone())), commands:tx, cancel:Arc::new(Notify::new()), running:Arc::new(AtomicBool::new(true)), committed:Arc::new(AtomicBool::new(false)) };
        *self.job.lock().unwrap() = Some(job.clone());
        if self.closed.load(Ordering::SeqCst) { job.cancel.notify_one(); }
        tauri::async_runtime::spawn(async move {
            let mut output = BufReader::new(output);
            let mut deadline = Instant::now() + Duration::from_secs(185);
            let result: Result<Value, String> = async {
                let mut line = Vec::new();
                loop {
                    let mut limited = (&mut output).take((1024 * 1024 + 1 - line.len()) as u64);
                    tokio::select! {
                        _ = job.cancel.notified() => {
                            // Recheck after waking: install may have been queued while select was waiting.
                            if !job.committed.load(Ordering::SeqCst) { return Err("cancelled".into()); }
                        },
                        _ = tokio::time::sleep_until(deadline) => return Err("维护任务超时。".into()),
                        command = rx.recv() => {
                            let action = command.ok_or("维护窗口已关闭。")?;
                            input.write_all(match action { Action::Download => b"download\n", Action::Install => b"install\n" }).await.map_err(|e| e.to_string())?;
                            deadline = Instant::now() + Duration::from_secs(if matches!(action, Action::Install) { 60 } else { 185 });
                        },
                        count = limited.read_until(b'\n', &mut line) => {
                            let count = count.map_err(|e| e.to_string())?;
                            if count == 0 || line.len() > 1024 * 1024 || line.last() != Some(&b'\n') { return Err("维护结果不完整或过大。".into()); }
                            let mut value = decode(&line, kind)?;
                            line.clear();
                            value["Token"] = json!(job.token);
                            let status = value["Status"].as_str().unwrap().to_string();
                            if terminal(&status) {
                                let code = timeout(Duration::from_secs(5), child.wait()).await.map_err(|_| "尚未确认维护进程退出。")?.map_err(|e| e.to_string())?;
                                if !code.success() { return Err("维护进程异常退出。".into()); }
                                return Ok(value);
                            }
                            if matches!(status.as_str(), "available" | "repairable" | "ready") { deadline = Instant::now() + Duration::from_secs(600); }
                            *job.view.lock().unwrap() = value;
                        }
                    }
                }
            }.await;
            drop(input);
            let mut value = match result {
                Ok(value) => value,
                Err(error) => {
                    let committed = job.committed.load(Ordering::SeqCst);
                    let stopped = if committed { Ok(()) } else { stop(&mut child).await };
                    let cancelled = error == "cancelled" && stopped.is_ok() && !committed;
                    json!({"Kind":kind,"Status":if cancelled {"cancelled"} else {"error"},"CanDownload":false,"Bytes":0,"Total":0,
                        "Message":if committed { "安装交接结果尚未确认，请查看程序和安装结果，勿重复提交。".to_string() }
                        else { stopped.err().unwrap_or_else(|| if cancelled { "任务已停止；缓存会保留，没有安装或重启程序。".into() } else {error}) }})
                }
            };
            value["Token"] = json!(job.token);
            *job.view.lock().unwrap() = value;
            job.running.store(false, Ordering::SeqCst);
        });
        Ok(initial)
    }
    fn find(&self, token: &str) -> Result<Job, String> { self.job.lock().unwrap().as_ref().filter(|j| j.token == token).cloned().ok_or("维护任务已失效，请重新检查。".into()) }
    pub fn read(&self, token: &str) -> Result<Value, String> { Ok(self.find(token)?.view.lock().unwrap().clone()) }
    pub fn action(&self, token: &str, action: Action) -> Result<Value, String> {
        let job = self.find(token)?;
        let mut view = job.view.lock().unwrap();
        if !job.running.load(Ordering::SeqCst) || job.committed.load(Ordering::SeqCst) || self.closed.load(Ordering::SeqCst) { return Err("任务已结束或已提交，请勿重复操作。".into()); }
        let status = view["Status"].as_str().unwrap_or("");
        let next = match action {
            Action::Download if matches!(status, "available" | "repairable") && view["CanDownload"] == true => "downloading",
            Action::Install if status == "ready" => "installing",
            _ => return Err("当前步骤不能执行此操作。".into()),
        };
        if matches!(action, Action::Install) { job.committed.store(true, Ordering::SeqCst); }
        job.commands.try_send(action).map_err(|_| "操作尚未确认，请勿重复提交。")?;
        view["Status"] = json!(next); view["CanDownload"] = json!(false);
        view["Message"] = json!(if next == "installing" { "正在交给安装助手，请等待。" } else { "正在下载并校验…" });
        Ok(view.clone())
    }
    pub async fn cancel(&self, token: &str) -> Result<Value, String> {
        let job = self.find(token)?;
        if job.committed.load(Ordering::SeqCst) { return Err("安装已经提交，不能取消或重复提交。".into()); }
        job.cancel.notify_one();
        timeout(Duration::from_secs(8), async { while job.running.load(Ordering::SeqCst) { sleep(Duration::from_millis(25)).await; } }).await.map_err(|_| "尚未确认任务停止，请稍后重试。")?;
        self.read(token)
    }
    pub fn close(&self) { self.closed.store(true, Ordering::SeqCst); if let Some(j) = self.job.lock().unwrap().as_ref() { j.cancel.notify_one(); } }
}
impl Drop for Upgrade { fn drop(&mut self) { self.close(); } }
async fn stop(child: &mut Child) -> Result<(), String> {
    if timeout(Duration::from_secs(3), child.wait()).await.is_ok_and(|r| r.is_ok()) { return Ok(()); }
    child.kill().await.map_err(|e| format!("无法确认任务停止：{e}"))
}
fn terminal(status: &str) -> bool { matches!(status, "current" | "passed" | "unavailable" | "cancelled" | "error" | "handoff") }
fn decode(bytes: &[u8], kind: Kind) -> Result<Value, String> {
    let value: Value = serde_json::from_slice(bytes).map_err(|_| "维护结果无法读取。")?;
    let status = value["Status"].as_str().unwrap_or("");
    if value["Kind"] != kind.text() || value["Message"].as_str().is_none_or(str::is_empty) || value["CanDownload"].as_bool().is_none()
        || value["Bytes"].as_u64().is_none() || value["Total"].as_u64().is_none()
        || (value["CanDownload"] == true && !matches!(status, "available" | "repairable"))
        || !(terminal(status) || matches!(status, "checking" | "available" | "repairable" | "downloading" | "ready" | "installing")) {
        return Err("维护结果不完整。".into());
    }
    Ok(value)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rejects_malformed_or_mismatched_status() {
        let base = json!({"Kind":"update","Status":"ready","Message":"中文结果","CanDownload":false,"Bytes":5,"Total":10});
        assert!(decode(base.to_string().as_bytes(), Kind::Update).is_ok());
        assert!(decode(base.to_string().as_bytes(), Kind::Repair).is_err());
        for (key, bad) in [("Status", json!("installed")), ("Message", json!(null)), ("Bytes", json!(-1)), ("Total", json!("10"))] {
            let mut value = base.clone(); value[key] = bad; assert!(decode(value.to_string().as_bytes(), Kind::Update).is_err());
        }
    }
    async fn wait_for(manager: &Upgrade, token: &str, status: &str) -> Value {
        timeout(Duration::from_secs(8), async {
            loop { let value = manager.read(token).unwrap(); if value["Status"] == status { return value; } sleep(Duration::from_millis(20)).await; }
        }).await.unwrap_or_else(|_| panic!("expected {status}, got {}", manager.read(token).unwrap()))
    }
    #[tokio::test]
    #[ignore = "requires the isolated staging test executable; never starts the real backend"]
    async fn isolated_upgrade_worker_protocol() {
        let path = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../staging/AudioSwitch.Tests.exe").canonicalize().unwrap();
        for mode in ["--upgrade-test-worker", "--upgrade-cancel-worker", "--upgrade-error-worker"] {
            let manager = Upgrade::default();
            let start = manager.start_worker(path.clone(), Kind::Update, mode).await.unwrap();
            let token = start["Token"].as_str().unwrap();
            assert!(manager.action(token, Action::Install).is_err());
            assert!(manager.read("stale").is_err());
            wait_for(&manager, token, "available").await;
            manager.action(token, Action::Download).unwrap();
            assert!(manager.action(token, Action::Download).is_err());
            if mode.contains("cancel") {
                assert_eq!(manager.cancel(token).await.unwrap()["Status"], "cancelled");
                assert!(!manager.find(token).unwrap().running.load(Ordering::SeqCst));
            } else {
                wait_for(&manager, token, "ready").await;
                manager.action(token, Action::Install).unwrap();
                assert!(manager.action(token, Action::Install).is_err());
                assert!(manager.cancel(token).await.is_err());
                manager.close(); // Closing must not kill an accepted installation.
                let result = wait_for(&manager, token, if mode.contains("error") { "error" } else { "handoff" }).await;
                assert!(result["Message"].as_str().unwrap().contains(if mode.contains("error") { "勿重复提交" } else { "尚未确认安装完成" }));
                assert!(manager.start_worker(path.clone(), Kind::Update, mode).await.is_err());
            }
        }
        let manager = Upgrade::default();
        let start = manager.start_worker(path, Kind::Repair, "--upgrade-test-worker").await.unwrap();
        let token = start["Token"].as_str().unwrap();
        wait_for(&manager, token, "repairable").await;
        manager.close(); wait_for(&manager, token, "cancelled").await;
    }
}
