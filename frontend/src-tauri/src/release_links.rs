pub fn validate(value: &str) -> Result<String, String> {
    let url = tauri::Url::parse(value).map_err(|_| "链接无效。")?;
    if value.len() > 4096 || value.chars().any(char::is_control) || !matches!(url.scheme(), "http" | "https")
        || url.host_str().is_none() || !url.username().is_empty() || url.password().is_some() {
        return Err("仅支持网页链接。".into());
    }
    Ok(url.to_string())
}
pub fn open(value: &str) -> Result<(), String> {
    let url: Vec<u16> = validate(value)?.encode_utf16().chain(Some(0)).collect();
    let result = unsafe { windows::Win32::UI::Shell::ShellExecuteW(None, windows::core::w!("open"), windows::core::PCWSTR(url.as_ptr()), None, None, windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL) };
    if result.0 as isize <= 32 { Err("无法打开默认浏览器。".into()) } else { Ok(()) }
}
#[cfg(test)]
mod tests {
    #[test] fn accepts_only_web_links() {
        assert!(super::validate("https://github.com/EthenGod/AudioSwitch/releases").is_ok());
        for url in ["file:///C:/a.exe", "javascript:alert(1)", "data:text/html,x", "ms-settings:test", "https://user:password@example.com", "https://example.com/\0x", "../file"] {
            assert!(super::validate(url).is_err(), "{url}");
        }
    }
}
