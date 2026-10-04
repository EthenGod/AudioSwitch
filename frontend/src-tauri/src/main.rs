#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod pipe;
mod actions;
use serde_json::{json, Value};
use std::sync::atomic::{AtomicBool, Ordering};

#[derive(Default)]
struct Bridge { gate: tokio::sync::Mutex<()>, uncertain: AtomicBool }
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
    match pipe::send(&request, true).await {
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
fn main() {
    tauri::Builder::default()
        .manage(Bridge::default())
        .invoke_handler(tauri::generate_handler![read_snapshot, read_device_settings, panel_action])
        .run(tauri::generate_context!())
        .expect("无法启动声间面板");
}
