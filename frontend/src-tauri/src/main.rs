#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod pipe;
mod actions;
mod backup;
mod maintenance;
use tauri::Manager;
use serde_json::{json, Value};
use std::sync::atomic::{AtomicBool, Ordering};

#[derive(Default)]
struct Bridge { gate: tokio::sync::Mutex<()>, uncertain: AtomicBool, import: tokio::sync::Mutex<Option<backup::PendingImport>> }
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct BridgeError { message: String, requires_refresh: bool }
impl From<String> for BridgeError {
    fn from(message: String) -> Self { Self { message, requires_refresh: false } }
}

#[tauri::command]
async fn read_snapshot(bridge: tauri::State<'_, Bridge>) -> Result<Value, BridgeError> {
    let _guard = bridge.gate.try_lock().map_err(|_| BridgeError::from("正在处理上一个操作，请稍后刷新。".to_string()))?;
    let result = pipe::read_snapshot().await?;
    // A successful reply proves the single-threaded backend finished earlier requests.
    if result["State"].is_object() { bridge.uncertain.store(false, Ordering::SeqCst); }
    Ok(result)
}
#[tauri::command]
async fn read_device_settings(id: String, bridge: tauri::State<'_, Bridge>) -> Result<Value, BridgeError> {
    let _guard = bridge.gate.try_lock().map_err(|_| BridgeError::from("正在处理上一个操作。".to_string()))?;
    if id.is_empty() || id.len() > 2048 { return Err("无效的设备标识。".to_string().into()); }
    Ok(pipe::send(&json!({"Action":"deviceSettings", "DeviceId":id}), false).await?)
}
#[tauri::command]
async fn panel_action(action: actions::Action, bridge: tauri::State<'_, Bridge>) -> Result<Value, BridgeError> {
    let _guard = bridge.gate.try_lock().map_err(|_| BridgeError::from("正在处理上一个操作，请勿重复提交。".to_string()))?;
    if bridge.uncertain.load(Ordering::SeqCst) {
        return Err(BridgeError { message:"上次操作结果尚未确认，请先刷新状态。".into(), requires_refresh:true });
    }
    let snapshot = pipe::read_snapshot().await?;
    let request = action.request(&snapshot)?;
    send_mutation(&request, &bridge).await
}
async fn send_mutation(request: &Value, bridge: &Bridge) -> Result<Value, BridgeError> {
    match pipe::send(request, true).await {
        Ok(reply) => {
            if !reply["State"].is_object() || !reply["Preferences"].is_object() || reply.get("OperationError").is_none() {
                bridge.uncertain.store(true, Ordering::SeqCst);
                return Err(BridgeError { message:"后台未确认完整结果，请刷新后检查。".into(), requires_refresh:true });
            }
            Ok(reply)
        },
        Err(error) => {
            bridge.uncertain.store(true, Ordering::SeqCst);
            Err(BridgeError { message:format!("{error} 操作可能已经执行，请先刷新确认，勿重复提交。"), requires_refresh:true })
        }
    }
}

#[tauri::command]
async fn export_backup(window: tauri::WebviewWindow, bridge: tauri::State<'_, Bridge>) -> Result<Option<Value>, BridgeError> {
    let _guard = bridge.gate.try_lock().map_err(|_| BridgeError::from("正在处理上一个操作。".to_string()))?;
    backup::require_supported(&pipe::read_snapshot().await?)?;
    let reply = pipe::send(&json!({"Action":"exportSettings"}), false).await?;
    backup::check_reply(&reply)?;
    let configuration = reply["ConfigurationJson"].as_str().ok_or_else(|| BridgeError::from("后台没有返回备份内容。".to_string()))?.to_owned();
    let owner = window.hwnd().map_err(|e| BridgeError::from(e.to_string()))?.0 as isize;
    tauri::async_runtime::spawn_blocking(move || -> Result<Option<Value>, String> {
        let Some(path) = backup::choose(owner, true)? else { return Ok(None) };
        let active = std::path::PathBuf::from(std::env::var_os("LOCALAPPDATA").ok_or("无法确定活动配置位置。")?).join("AudioSwitch/settings.json");
        backup::write(&path, &configuration, &active)?;
        Ok(Some(json!({"Path":path.to_string_lossy()})))
    }).await.map_err(|e| BridgeError::from(e.to_string()))?.map_err(Into::into)
}
#[tauri::command]
async fn choose_import(window: tauri::WebviewWindow, bridge: tauri::State<'_, Bridge>) -> Result<Option<Value>, BridgeError> {
    let _guard = bridge.gate.try_lock().map_err(|_| BridgeError::from("正在处理上一个操作。".to_string()))?;
    *bridge.import.lock().await = None;
    backup::require_supported(&pipe::read_snapshot().await?)?;
    let owner = window.hwnd().map_err(|e| BridgeError::from(e.to_string()))?.0 as isize;
    let selected = tauri::async_runtime::spawn_blocking(move || -> Result<_, String> {
        let Some(path) = backup::choose(owner, false)? else { return Ok(None) };
        let text = backup::read(&path)?; Ok(Some((path, text)))
    }).await.map_err(|e| BridgeError::from(e.to_string()))??;
    let Some((path, text)) = selected else { return Ok(None) };
    let reply = pipe::send(&json!({"Action":"previewImport", "ConfigurationJson":text}), false).await?;
    let (pending, summary) = backup::PendingImport::prepare(&reply, &path)?;
    *bridge.import.lock().await = Some(pending);
    Ok(Some(summary))
}
#[tauri::command]
async fn discard_import(token: String, bridge: tauri::State<'_, Bridge>) -> Result<(), BridgeError> {
    let mut pending = bridge.import.lock().await;
    if pending.as_ref().is_some_and(|entry| entry.token == token) { *pending = None; }
    Ok(())
}
#[tauri::command]
async fn confirm_import(token: String, bridge: tauri::State<'_, Bridge>) -> Result<Value, BridgeError> {
    let _guard = bridge.gate.try_lock().map_err(|_| BridgeError::from("正在处理上一个操作，请勿重复提交。".to_string()))?;
    if bridge.uncertain.load(Ordering::SeqCst) { return Err(BridgeError { message:"请先刷新状态确认上次结果，再重新选择备份。".into(), requires_refresh:true }); }
    let mut entry = bridge.import.lock().await;
    if !entry.as_ref().is_some_and(|item| item.token == token) { return Err("备份预览已失效，请重新选择文件。".to_string().into()); }
    let request = entry.take().unwrap().request();
    // Consume before sending: a double click or failed response cannot re-import.
    let reply = send_mutation(&request, &bridge).await?;
    if reply["OperationError"].is_null() && (reply["PreferencesSaved"] != true || reply["BackupPath"].as_str().is_none_or(str::is_empty)) {
        bridge.uncertain.store(true, Ordering::SeqCst);
        return Err(BridgeError { message:"后台未确认完整导入结果，请刷新检查，勿重复导入。".into(), requires_refresh:true });
    }
    Ok(reply)
}
#[tauri::command]
async fn start_maintenance(kind: maintenance::Kind, bridge: tauri::State<'_, Bridge>, jobs: tauri::State<'_, maintenance::Maintenance>) -> Result<Value, BridgeError> {
    let _guard = bridge.gate.try_lock().map_err(|_| BridgeError::from("正在处理上一个操作。".to_string()))?;
    let path = maintenance::backend_path(&pipe::read_snapshot().await?)?;
    Ok(jobs.start(path, kind).await?)
}
#[tauri::command]
fn read_maintenance(token: String, jobs: tauri::State<'_, maintenance::Maintenance>) -> Result<Value, BridgeError> { Ok(jobs.read(&token)?) }
#[tauri::command]
async fn cancel_maintenance(token: String, jobs: tauri::State<'_, maintenance::Maintenance>) -> Result<Value, BridgeError> { Ok(jobs.cancel(&token).await?) }
fn main() {
    tauri::Builder::default()
        .manage(Bridge::default())
        .manage(maintenance::Maintenance::default())
        .on_window_event(|window, event| { if matches!(event, tauri::WindowEvent::Destroyed) { window.state::<maintenance::Maintenance>().cancel_all(); } })
        .invoke_handler(tauri::generate_handler![read_snapshot, read_device_settings, panel_action, export_backup, choose_import, confirm_import, discard_import, start_maintenance, read_maintenance, cancel_maintenance])
        .run(tauri::generate_context!())
        .expect("无法启动声间面板");
}
