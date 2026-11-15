//! Shared, embedded community catalogues. No file access on the UI/request path.
use std::{collections::HashMap, sync::{OnceLock, RwLock}};
use serde::Deserialize;

include!(concat!(env!("OUT_DIR"), "/locale_resources.rs"));
#[derive(Deserialize)]
struct Language { id: String, r#match: Vec<String> }
fn languages() -> &'static Vec<Language> {
    static LANGUAGES: OnceLock<Vec<Language>> = OnceLock::new();
    LANGUAGES.get_or_init(|| serde_json::from_str(LANGUAGE_MANIFEST).expect("validated language manifest"))
}
fn selection() -> &'static RwLock<String> {
    static LANGUAGE: OnceLock<RwLock<String>> = OnceLock::new();
    LANGUAGE.get_or_init(|| RwLock::new("zh-CN".into()))
}
pub fn supported(id: &str) -> bool { languages().iter().any(|language| language.id == id) }
pub const SYSTEM_LANGUAGE: &str = "system";
fn resolve_language(saved: Option<&str>, system: &str) -> String {
    let id = match saved { None | Some(SYSTEM_LANGUAGE) => system, Some(id) => id };
    if supported(id) { id.into() } else { "zh-CN".into() }
}
pub fn match_language(preferred: &[String]) -> String {
    for value in preferred {
        let tag = value.replace('_', "-").to_lowercase();
        if let Some(language) = languages().iter().find(|language| language.id.eq_ignore_ascii_case(&tag)) { return language.id.clone(); }
        if let Some(language) = languages().iter().find(|language| language.r#match.iter().any(|prefix| tag == *prefix || tag.starts_with(&format!("{prefix}-")))) { return language.id.clone(); }
    }
    "zh-CN".into()
}
pub fn set_language(id: &str) {
    let id = if supported(id) { id } else { "zh-CN" };
    if *selection().read().unwrap() != id { *selection().write().unwrap() = id.into(); }
}
pub fn initialize() {
    // The backend sends the authoritative choice with every snapshot. This read
    // only covers errors that occur before the first successful connection.
    let saved = std::env::var_os("LOCALAPPDATA").and_then(|directory| {
        let path = std::path::Path::new(&directory).join("AudioSwitch/settings.json");
        if std::fs::metadata(&path).ok()?.len() > 1024 * 1024 { return None; }
        let value: serde_json::Value = serde_json::from_slice(&std::fs::read(path).ok()?).ok()?;
        value.get("Settings").unwrap_or(&value).get("Language")?.as_str().map(str::to_owned)
    });
    let system = if saved.is_none() || saved.as_deref() == Some(SYSTEM_LANGUAGE) { system_language() } else { String::new() };
    set_language(&resolve_language(saved.as_deref(), &system));
}
fn system_language() -> String {
    use windows_sys::Win32::Globalization::{GetUserPreferredUILanguages, MUI_LANGUAGE_NAME};
    let (mut count, mut length) = (0, 0);
    unsafe {
        if GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &mut count, std::ptr::null_mut(), &mut length) != 0 && (1..=32768).contains(&length) {
            let mut buffer = vec![0u16; length as usize];
            if GetUserPreferredUILanguages(MUI_LANGUAGE_NAME, &mut count, buffer.as_mut_ptr(), &mut length) != 0 {
                return match_language(&buffer.split(|&c| c == 0).filter(|part| !part.is_empty()).map(String::from_utf16_lossy).collect::<Vec<_>>());
            }
        }
    }
    "zh-CN".into()
}
pub fn translate(language: &str, source: &str) -> String {
    static CATALOGUES: OnceLock<RwLock<HashMap<String, HashMap<String, String>>>> = OnceLock::new();
    let catalogues = CATALOGUES.get_or_init(|| RwLock::new(HashMap::new()));
    if !catalogues.read().unwrap().contains_key(language) {
        let raw = LOCALE_RESOURCES.iter().find(|(id, _)| *id == language).map(|(_, raw)| *raw).unwrap_or("{}");
        let parsed = serde_json::from_str(raw).expect("validated translation catalogue");
        catalogues.write().unwrap().insert(language.into(), parsed);
    }
    let value = catalogues.read().unwrap().get(language).and_then(|messages| messages.get(source)).filter(|value| !value.is_empty()).cloned();
    value.unwrap_or_else(|| if language == "zh-CN" { source.into() } else { translate("zh-CN", source) })
}
pub fn text(source: &str) -> String { translate(&selection().read().unwrap(), source) }
pub fn format(source: &str, args: &[&str]) -> String {
    let message = text(source);
    let mut output = String::with_capacity(message.len());
    let mut rest = message.as_str();
    while let Some(start) = rest.find('{') {
        output.push_str(&rest[..start]);
        rest = &rest[start..];
        if let Some(end) = rest.find('}') {
            if let Ok(index) = rest[1..end].parse::<usize>() {
                if let Some(value) = args.get(index) { output.push_str(value); rest = &rest[end + 1..]; continue; }
            }
        }
        output.push('{'); rest = &rest[1..];
    }
    output.push_str(rest); output
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn saved_system_choice_uses_detected_language_but_explicit_choice_does_not() {
        assert_eq!(resolve_language(Some(SYSTEM_LANGUAGE), "ja"), "ja");
        assert_eq!(resolve_language(Some(SYSTEM_LANGUAGE), "en"), "en");
        assert_eq!(resolve_language(Some(SYSTEM_LANGUAGE), "unknown"), "zh-CN");
        assert_eq!(resolve_language(None, "ja"), "ja");
        assert_eq!(resolve_language(Some("en"), "ja"), "en");
        assert_eq!(resolve_language(Some("unknown"), "ja"), "zh-CN");
    }
    #[test] fn locale_matching_and_fallback() {
        assert_eq!(match_language(&["ja-JP".into()]), "ja");
        assert_eq!(match_language(&["zz-ZZ".into(), "en-GB".into()]), "en");
        assert_eq!(match_language(&["zz-ZZ".into()]), "zh-CN");
        assert_eq!(translate("unknown", "设备设置"), "设备设置");
        assert_eq!(translate("en", "关闭"), "Close");
        assert_ne!(translate("ja", "设备设置"), "设备设置");
    }
}
