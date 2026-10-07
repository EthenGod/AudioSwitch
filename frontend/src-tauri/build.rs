fn main() {
    tauri_build::try_build(
        tauri_build::Attributes::new().app_manifest(
            tauri_build::AppManifest::new().commands(&["read_snapshot", "read_device_settings", "panel_action", "export_backup", "choose_import", "confirm_import", "discard_import", "start_maintenance", "read_maintenance", "cancel_maintenance", "read_prompt_snapshot", "prompt_action", "close_prompt", "open_prompt_panel", "save_dolby", "start_dolby_read", "read_dolby", "cancel_dolby_read", "start_dolby_apply", "read_dolby_apply", "cancel_dolby_apply"]),
        ),
    ).expect("Tauri build configuration failed");
}
