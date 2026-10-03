#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod pipe;

#[tauri::command]
async fn read_snapshot(gate: tauri::State<'_, tokio::sync::Mutex<()>>) -> Result<serde_json::Value, String> {
    // Reject duplicate requests instead of building an unbounded IPC queue.
    let _guard = gate.try_lock().map_err(|_| "正在读取设备状态，请稍后重试。".to_string())?;
    pipe::read_snapshot().await
}

fn main() {
    tauri::Builder::default()
        .manage(tokio::sync::Mutex::new(()))
        .invoke_handler(tauri::generate_handler![read_snapshot])
        // Default Tauri lifecycle exits on last-window close. No tray or hide handler.
        .run(tauri::generate_context!())
        .expect("无法启动声间只读面板");
}
