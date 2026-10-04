use serde_json::Value;
use std::{io, time::Duration};
use tokio::{io::{AsyncBufReadExt, AsyncRead, AsyncReadExt, AsyncWriteExt, BufReader}, net::windows::named_pipe::ClientOptions, time::{sleep, timeout}};
use windows_sys::Win32::{
    Foundation::{CloseHandle, LocalFree, ERROR_PIPE_BUSY},
    Security::{Authorization::ConvertSidToStringSidW, GetTokenInformation, TokenUser, TOKEN_QUERY, TOKEN_USER},
    System::{RemoteDesktop::ProcessIdToSessionId, Threading::{GetCurrentProcess, GetCurrentProcessId, OpenProcessToken}},
};

const MAX_REPLY: usize = 4 * 1024 * 1024;
const REQUEST: &[u8] = b"{\"Action\":\"snapshot\"}\n";

/// Match src/Ipc.cs Wire.Identity exactly; never accept a path from JavaScript.
fn pipe_name() -> Result<String, String> {
    unsafe {
        let mut token = std::ptr::null_mut();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token) == 0 {
            return Err("无法读取当前 Windows 用户。".into());
        }
        let result = (|| {
            let mut length = 0;
            GetTokenInformation(token, TokenUser, std::ptr::null_mut(), 0, &mut length);
            if length == 0 { return Err("无法读取当前用户标识。".into()); }
            // usize provides TOKEN_USER alignment; the SID lives within this allocation.
            let mut buffer = vec![0usize; (length as usize).div_ceil(std::mem::size_of::<usize>())];
            if GetTokenInformation(token, TokenUser, buffer.as_mut_ptr().cast(), length, &mut length) == 0 {
                return Err("无法读取当前用户标识。".into());
            }
            let user = &*buffer.as_ptr().cast::<TOKEN_USER>();
            let mut text = std::ptr::null_mut();
            if ConvertSidToStringSidW(user.User.Sid, &mut text) == 0 {
                return Err("无法转换当前用户标识。".into());
            }
            let mut count = 0;
            while *text.add(count) != 0 { count += 1; }
            let sid = String::from_utf16_lossy(std::slice::from_raw_parts(text, count));
            LocalFree(text.cast());
            let mut session = 0;
            if ProcessIdToSessionId(GetCurrentProcessId(), &mut session) == 0 {
                return Err("无法读取当前 Windows 会话。".into());
            }
            Ok(format!(r"\\.\pipe\AudioSwitch-{sid}-{session}"))
        })();
        CloseHandle(token);
        result
    }
}

async fn read_reply(reader: impl AsyncRead + Unpin, limit: usize) -> Result<Value, String> {
    let mut bytes = Vec::new();
    BufReader::new(reader.take((limit + 1) as u64)).read_until(b'\n', &mut bytes).await
        .map_err(|_| "后台连接已中断，请重试。".to_string())?;
    if bytes.len() > limit { return Err("后台响应过大，已停止读取。".into()); }
    if bytes.last() != Some(&b'\n') { return Err("后台连接已中断，未收到完整状态。".into()); }
    let bytes = bytes.strip_prefix(&[0xef, 0xbb, 0xbf]).unwrap_or(&bytes);
    serde_json::from_slice(bytes).map_err(|_| "后台响应格式无效，请确认后台版本后重试。".into())
}

async fn exchange_request(path: &str, request: &[u8], connect_wait: Duration, reply_wait: Duration) -> Result<Value, String> {
    // ClientOptions defaults to SECURITY_IDENTIFICATION, preventing server impersonation.
    let mut pipe = timeout(connect_wait, async {
        loop {
            match ClientOptions::new().open(path) {
                Ok(pipe) => return Ok(pipe),
                Err(error) if error.raw_os_error() == Some(ERROR_PIPE_BUSY as i32) => sleep(Duration::from_millis(50)).await,
                Err(error) if error.kind() == io::ErrorKind::NotFound => return Err("未连接到声间后台。请先打开原版声间，再点击重试。".to_string()),
                Err(_) => return Err("无法连接声间后台，请确认它运行在当前 Windows 用户和会话中。".to_string()),
            }
        }
    }).await.map_err(|_| "后台暂忙，请稍后重试。".to_string())??;
    // Timeout drops the pending overlapped I/O and pipe handle. No detached worker remains.
    timeout(reply_wait, async {
        pipe.write_all(request).await.map_err(|_| "后台连接已中断，请重试。".to_string())?;
        read_reply(pipe, MAX_REPLY).await
    }).await.map_err(|_| "后台响应超时，请稍后重试。".to_string())?
}

pub async fn read_snapshot() -> Result<Value, String> {
    exchange_request(&pipe_name()?, REQUEST, Duration::from_millis(1800), Duration::from_secs(4)).await
}

pub async fn send(request: &Value, mutation: bool) -> Result<Value, String> {
    let mut bytes = serde_json::to_vec(request).map_err(|_| "无法编码后台请求。".to_string())?;
    if bytes.len() > 1024 * 1024 { return Err("请求过大。".into()); }
    bytes.push(b'\n');
    exchange_request(&pipe_name()?, &bytes, Duration::from_millis(1800), Duration::from_secs(if mutation { 20 } else { 4 })).await
}

#[cfg(test)]
async fn exchange(path: &str, connect_wait: Duration, reply_wait: Duration) -> Result<Value, String> {
    exchange_request(path, REQUEST, connect_wait, reply_wait).await
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::net::windows::named_pipe::ServerOptions;
    use std::sync::atomic::{AtomicUsize, Ordering};
    static COUNTER: AtomicUsize = AtomicUsize::new(0);
    fn test_path() -> String {
        format!(r"\\.\pipe\AudioSwitch-panel-test-{}-{}", std::process::id(), COUNTER.fetch_add(1, Ordering::Relaxed))
    }
    #[tokio::test]
    async fn utf8_reply_and_fixed_readonly_request() {
        let path = test_path();
        let mut server = ServerOptions::new().first_pipe_instance(true).create(&path).unwrap();
        let serve = tokio::spawn(async move {
            server.connect().await.unwrap();
            let mut line = String::new();
            BufReader::new(&mut server).read_line(&mut line).await.unwrap();
            assert_eq!(line.as_bytes(), REQUEST);
            server.write_all("\u{feff}{\"Error\":\"中文错误：设备已断开\"}\r\n".as_bytes()).await.unwrap();
        });
        let result = exchange(&path, Duration::from_secs(1), Duration::from_secs(1)).await.unwrap();
        assert_eq!(result["Error"], "中文错误：设备已断开");
        serve.await.unwrap();
    }
    #[tokio::test]
    async fn basic_save_crosses_pipe_as_utf8_with_explicit_save_only() {
        let path = test_path();
        let mut server = ServerOptions::new().first_pipe_instance(true).create(&path).unwrap();
        let action: crate::actions::Action = serde_json::from_value(serde_json::json!({"kind":"saveBasic","id":"中文端点",
            "profile":{"Volume":0,"SpatialFormat":null},"expectedProfile":null,"rule":1,"expectedRule":0})).unwrap();
        let request = action.request(&serde_json::json!({"PanelApiVersion":1})).unwrap();
        let mut bytes = serde_json::to_vec(&request).unwrap(); bytes.push(b'\n');
        let serve = tokio::spawn(async move {
            server.connect().await.unwrap();
            let mut line = String::new();
            BufReader::new(&mut server).read_line(&mut line).await.unwrap();
            let request: Value = serde_json::from_str(&line).unwrap();
            assert_eq!(request["Action"], "saveBasicDeviceSettings");
            assert_eq!(request["DeviceId"], "中文端点"); assert_eq!(request["Value"], false);
            assert_eq!(request["Profile"]["Volume"], 0); assert!(request["Profile"]["SpatialFormat"].is_null());
            assert!(request["Profile"].get("Dolby").is_none());
            server.write_all("{\"OperationError\":\"预设冲突，未保存\",\"PreferencesSaved\":false}\n".as_bytes()).await.unwrap();
        });
        let result = exchange_request(&path, &bytes, Duration::from_secs(1), Duration::from_secs(1)).await.unwrap();
        assert_eq!(result["OperationError"], "预设冲突，未保存");
        serve.await.unwrap();
    }
    #[tokio::test]
    async fn bounds_and_invalid_replies() {
        assert!(read_reply(&b"xxxxxxxxxx\n"[..], 5).await.unwrap_err().contains("过大"));
        assert!(read_reply(&b"{}"[..], 20).await.unwrap_err().contains("完整"));
        assert!(read_reply(&b"\xff\n"[..], 20).await.unwrap_err().contains("格式"));
    }
    #[tokio::test]
    async fn absent_backend_is_reported_without_starting_it() {
        assert!(exchange(&test_path(), Duration::from_millis(100), Duration::from_millis(100)).await.unwrap_err().contains("未连接"));
    }
    #[tokio::test]
    async fn stalled_reply_times_out() {
        let path = test_path();
        let server = ServerOptions::new().first_pipe_instance(true).create(&path).unwrap();
        let serve = tokio::spawn(async move {
            server.connect().await.unwrap();
            sleep(Duration::from_millis(200)).await;
        });
        assert!(exchange(&path, Duration::from_secs(1), Duration::from_millis(50)).await.unwrap_err().contains("超时"));
        serve.await.unwrap();
    }
    #[test]
    fn current_user_and_session_are_part_of_identity() {
        let name = pipe_name().unwrap();
        assert!(name.starts_with(r"\\.\pipe\AudioSwitch-S-1-"));
        assert!(name.rsplit('-').next().unwrap().parse::<u32>().is_ok());
    }
}
