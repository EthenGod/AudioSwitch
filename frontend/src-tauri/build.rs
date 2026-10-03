fn main() {
    tauri_build::try_build(
        tauri_build::Attributes::new().app_manifest(
            tauri_build::AppManifest::new().commands(&["read_snapshot"]),
        ),
    ).expect("Tauri build configuration failed");
}
