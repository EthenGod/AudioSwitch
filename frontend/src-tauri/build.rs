fn main() {
    tauri_build::try_build(
        tauri_build::Attributes::new().app_manifest(
            tauri_build::AppManifest::new().commands(&["read_snapshot", "read_device_settings", "panel_action", "export_backup", "choose_import", "confirm_import", "discard_import", "start_maintenance", "read_maintenance", "cancel_maintenance"]),
        ),
    ).expect("Tauri build configuration failed");
}
