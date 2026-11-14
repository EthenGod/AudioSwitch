fn main() {
    let directory = std::path::Path::new("../../locales").canonicalize().expect("locales directory");
    let manifest = std::fs::read_to_string(directory.join("languages.json")).expect("language manifest");
    let languages: serde_json::Value = serde_json::from_str(&manifest).expect("language manifest JSON");
    let mut generated = format!("const LANGUAGE_MANIFEST: &str = {:?};\nconst LOCALE_RESOURCES: &[(&str, &str)] = &[\n", manifest);
    println!("cargo:rerun-if-changed={}", directory.display());
    for language in languages.as_array().expect("language array") {
        let id = language["id"].as_str().expect("language id");
        assert!(id.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'-'));
        let path = directory.join(format!("{id}.json"));
        generated.push_str(&format!("({:?}, include_str!({:?})),\n", id, path));
    }
    generated.push_str("];\n");
    std::fs::write(std::path::Path::new(&std::env::var("OUT_DIR").unwrap()).join("locale_resources.rs"), generated).unwrap();
    tauri_build::try_build(
        tauri_build::Attributes::new().app_manifest(
            tauri_build::AppManifest::new().commands(&["open_release_link", "panel_ready", "resize_prompt", "start_upgrade", "read_upgrade", "upgrade_action", "cancel_upgrade", "read_snapshot", "read_device_settings", "panel_action", "export_backup", "choose_import", "confirm_import", "discard_import", "start_maintenance", "read_maintenance", "cancel_maintenance", "read_prompt_snapshot", "prompt_action", "close_prompt", "open_prompt_panel", "save_dolby", "start_dolby_read", "read_dolby", "cancel_dolby_read", "start_dolby_apply", "read_dolby_apply", "cancel_dolby_apply"]),
        ),
    ).expect("Tauri build configuration failed");
}
