//! Short-lived, loopback-only transport for the existing panel commands.
use super::*;
use std::{collections::HashMap, io::{BufRead, Read, Write}, net::{TcpListener, TcpStream}, sync::{Arc, Mutex, atomic::AtomicUsize}, time::{Duration, Instant}};

const MAX_BODY: usize = 2 * 1024 * 1024;
const SECURITY: &str = "Cache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'\r\n";

struct Lifetime { clients: usize, deadline: Instant, keep_open: bool }
impl Lifetime {
    fn new() -> Self { Self { clients: 0, deadline: Instant::now() + Duration::from_secs(60), keep_open: false } }
    fn keep(&mut self, value: bool) { self.keep_open = value; if !value { self.deadline = Instant::now() + Duration::from_secs(8); } }
    fn opened(&mut self) { self.deadline = Instant::now() + Duration::from_secs(60); }
    fn joined(&mut self) { self.clients += 1; }
    fn left(&mut self) { self.clients -= 1; if self.clients == 0 { self.deadline = Instant::now() + Duration::from_secs(8); } }
    fn expired(&self) -> bool { !self.keep_open && self.clients == 0 && Instant::now() >= self.deadline }
}
struct Host { authority: String, token: String, lifetime: Mutex<Lifetime>, connections: AtomicUsize, stop: AtomicBool }
struct Connection(Arc<Host>);
impl Drop for Connection { fn drop(&mut self) { self.0.connections.fetch_sub(1, Ordering::SeqCst); } }
struct Client(Arc<Host>);
impl Drop for Client { fn drop(&mut self) { self.0.lifetime.lock().unwrap().left(); } }

pub fn start(app: tauri::AppHandle) -> Result<(), Box<dyn std::error::Error>> {
    let listener = TcpListener::bind((std::net::Ipv4Addr::LOCALHOST, 0))?;
    listener.set_nonblocking(true)?;
    // OS-generated GUIDs avoid predictable credentials. The token never goes into a query string.
    let token = unsafe { format!("{:?}{:?}", windows::Win32::System::Com::CoCreateGuid()?, windows::Win32::System::Com::CoCreateGuid()?) }.replace('-', "");
    let host = Arc::new(Host { authority: listener.local_addr()?.to_string(), token, lifetime: Mutex::new(Lifetime::new()), connections: AtomicUsize::new(0), stop: AtomicBool::new(false) });
    host.lifetime.lock().unwrap().keep(std::env::args().any(|arg| arg == "--keep-browser"));
    // Do not shorten the initial wait when using the default on-demand mode.
    host.lifetime.lock().unwrap().opened();
    if !std::env::args().any(|arg| arg == "--browser-idle") { open(&host)?; }
    let commands = host.clone();
    std::thread::spawn(move || {
        for line in std::io::stdin().lock().lines() {
            match line.as_deref() {
                Ok("open") => { commands.lifetime.lock().unwrap().opened(); let _ = open(&commands); },
                Ok("keep") => commands.lifetime.lock().unwrap().keep(true),
                Ok("release") => commands.lifetime.lock().unwrap().keep(false),
                Ok("close") | Err(_) => break,
                _ => (),
            }
        }
        commands.stop.store(true, Ordering::SeqCst);
    });
    std::thread::spawn(move || {
        while !host.stop.load(Ordering::SeqCst) && !app.state::<prompt::Session>().exited() && !host.lifetime.lock().unwrap().expired() {
            match listener.accept() {
                Ok((stream, peer)) if peer.ip().is_loopback() => {
                    if host.connections.fetch_add(1, Ordering::SeqCst) >= 32 { host.connections.fetch_sub(1, Ordering::SeqCst); continue; }
                    let connection = Connection(host.clone()); let app = app.clone();
                    std::thread::spawn(move || { let _ = serve(stream, &connection.0, &app); });
                },
                Ok(_) => (),
                Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => std::thread::sleep(Duration::from_millis(250)),
                Err(_) => break,
            }
        }
        drop(listener);
        app.state::<upgrade::Upgrade>().close();
        app.state::<maintenance::Maintenance>().cancel_all();
        app.state::<dolby::Reader>().close();
        app.exit(0);
    });
    Ok(())
}

fn open(host: &Host) -> Result<(), Box<dyn std::error::Error>> {
    let url = format!("http://{}/?browser#{}", host.authority, host.token);
    let url: Vec<u16> = url.encode_utf16().chain(Some(0)).collect();
    let result = unsafe { windows::Win32::UI::Shell::ShellExecuteW(None, windows::core::w!("open"), windows::core::PCWSTR(url.as_ptr()), None, None, windows::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL) };
    if result.0 as isize <= 32 { return Err("无法打开默认浏览器，请检查 Windows 默认应用设置。".into()); }
    Ok(())
}

struct Request { method: String, path: String, headers: HashMap<String, String>, body: Vec<u8> }
fn read_request(stream: &mut TcpStream) -> std::io::Result<Request> {
    let invalid = || std::io::Error::new(std::io::ErrorKind::InvalidData, "invalid request");
    let mut head = Vec::new();
    while !head.ends_with(b"\r\n\r\n") {
        if head.len() >= 8192 { return Err(invalid()); }
        let mut byte = [0]; stream.read_exact(&mut byte)?; head.push(byte[0]);
    }
    let head = std::str::from_utf8(&head).map_err(|_| invalid())?;
    let mut lines = head.split("\r\n");
    let mut first = lines.next().unwrap_or("").split(' ');
    let method = first.next().ok_or_else(invalid)?.to_owned();
    let path = first.next().ok_or_else(invalid)?.to_owned();
    if first.next() != Some("HTTP/1.1") || first.next().is_some() { return Err(invalid()); }
    let mut headers = HashMap::new();
    for line in lines.take_while(|line| !line.is_empty()) {
        let (key, value) = line.split_once(':').ok_or_else(invalid)?;
        if key.is_empty() || !key.bytes().all(|c| c.is_ascii_alphanumeric() || c == b'-') { return Err(invalid()); }
        if headers.insert(key.to_ascii_lowercase(), value.trim().to_owned()).is_some() { return Err(invalid()); }
    }
    if headers.contains_key("transfer-encoding") || headers.contains_key("expect") { return Err(invalid()); }
    let length = match headers.get("content-length") { Some(v) if !v.is_empty() && v.bytes().all(|b| b.is_ascii_digit()) => v.parse::<usize>().map_err(|_| invalid())?, Some(_) => return Err(invalid()), None => 0 };
    if length > MAX_BODY || (method != "POST" && length != 0) { return Err(invalid()); }
    let mut body = vec![0; length]; stream.read_exact(&mut body)?;
    Ok(Request { method, path, headers, body })
}
fn authorized(request: &Request, host: &Host) -> bool {
    let origin = format!("http://{}", host.authority);
    request.headers.get("host") == Some(&host.authority)
        && request.headers.get("x-audioswitch-token") == Some(&host.token)
        && request.headers.get("origin").is_none_or(|v| v == &origin)
        && (request.method != "POST" || request.headers.get("origin") == Some(&origin))
        && request.headers.get("sec-fetch-site").is_none_or(|v| v == "same-origin")
}
fn respond(stream: &mut TcpStream, status: &str, kind: &str, body: &[u8]) -> std::io::Result<()> {
    write!(stream, "HTTP/1.1 {status}\r\nContent-Type: {kind}\r\nContent-Length: {}\r\nConnection: close\r\n{SECURITY}\r\n", body.len())?;
    stream.write_all(body)
}
fn asset(path: &str) -> Option<(&'static str, &'static [u8])> {
    match path {
        "/" | "/?browser" => Some(("text/html; charset=utf-8", include_bytes!("../../dist-desktop/index.html"))),
        "/assets/app.js" => Some(("text/javascript; charset=utf-8", include_bytes!("../../dist-desktop/assets/app.js"))),
        "/assets/index.css" => Some(("text/css; charset=utf-8", include_bytes!("../../dist-desktop/assets/index.css"))),
        "/mark.svg" => Some(("image/svg+xml", include_bytes!("../../public/mark.svg"))),
        _ => None,
    }
}
fn serve(mut stream: TcpStream, host: &Arc<Host>, app: &tauri::AppHandle) -> std::io::Result<()> {
    stream.set_read_timeout(Some(Duration::from_secs(10)))?;
    stream.set_write_timeout(Some(Duration::from_secs(5)))?;
    let request = match read_request(&mut stream) { Ok(r) => r, Err(_) => return respond(&mut stream, "400 Bad Request", "text/plain", b"Bad request") };
    if request.headers.get("host") != Some(&host.authority) { return respond(&mut stream, "403 Forbidden", "text/plain", b"Forbidden"); }
    if request.method == "GET" {
        if let Some((kind, bytes)) = asset(&request.path) { return respond(&mut stream, "200 OK", kind, bytes); }
    }
    if !authorized(&request, host) { return respond(&mut stream, "403 Forbidden", "text/plain", b"Forbidden"); }
    if request.method == "GET" && request.path == "/session" {
        host.lifetime.lock().unwrap().joined(); let _client = Client(host.clone());
        write!(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n{SECURITY}\r\n")?;
        stream.set_read_timeout(Some(Duration::from_secs(2)))?;
        loop {
            stream.write_all(b"3\r\n:\n\n\r\n")?;
            let mut byte = [0];
            match stream.read(&mut byte) {
                Err(e) if matches!(e.kind(), std::io::ErrorKind::TimedOut | std::io::ErrorKind::WouldBlock) => (),
                _ => return Ok(()),
            }
            if host.stop.load(Ordering::SeqCst) { return Ok(()); }
        }
    }
    if request.method != "POST" || request.path != "/rpc" || request.headers.get("content-type").map(String::as_str) != Some("application/json") {
        return respond(&mut stream, "404 Not Found", "text/plain", b"Not found");
    }
    let result = match serde_json::from_slice::<Rpc>(&request.body) {
        Ok(rpc) => tauri::async_runtime::block_on(dispatch(app, rpc)),
        Err(_) => Err("请求格式无效。".to_string().into()),
    };
    let body = match result { Ok(value) => json!({"ok":true,"value":value}), Err(error) => json!({"ok":false,"error":error}) };
    respond(&mut stream, "200 OK", "application/json; charset=utf-8", body.to_string().as_bytes())
}

#[derive(serde::Deserialize)]
#[serde(deny_unknown_fields)]
struct Rpc { command: String, args: Value }
fn argument<T: serde::de::DeserializeOwned>(args: &Value, name: &str) -> Result<T, BridgeError> {
    serde_json::from_value(args.get(name).cloned().unwrap_or(Value::Null)).map_err(|_| "请求参数无效。".to_string().into())
}
async fn dispatch(app: &tauri::AppHandle, rpc: Rpc) -> Result<Value, BridgeError> {
    let args = &rpc.args;
    if !args.is_object() { return Err("请求参数无效。".to_string().into()); }
    // Only the existing panel commands are exposed. No generic IPC, shell or path access.
    match rpc.command.as_str() {
        "read_snapshot" => read_snapshot(app.state(), app.state()).await,
        "read_device_settings" => read_device_settings(argument(args, "id")?, app.state()).await,
        "panel_action" => panel_action(argument(args, "action")?, app.state()).await,
        "export_backup" => export_backup(app.clone(), app.state()).await.map(|v| v.unwrap_or(Value::Null)),
        "choose_import" => choose_import(app.clone(), app.state()).await.map(|v| v.unwrap_or(Value::Null)),
        "confirm_import" => confirm_import(argument(args, "token")?, app.state()).await,
        "discard_import" => discard_import(argument(args, "token")?, app.state()).await.map(|_| Value::Null),
        "start_maintenance" => start_maintenance(argument(args, "kind")?, app.state(), app.state()).await,
        "read_maintenance" => read_maintenance(argument(args, "token")?, app.state()),
        "cancel_maintenance" => cancel_maintenance(argument(args, "token")?, app.state()).await,
        "start_upgrade" => start_upgrade(argument(args, "kind")?, app.state(), app.state()).await,
        "read_upgrade" => read_upgrade(argument(args, "token")?, app.state()),
        "upgrade_action" => upgrade_action(argument(args, "token")?, argument(args, "action")?, app.state()),
        "cancel_upgrade" => cancel_upgrade(argument(args, "token")?, app.state()).await,
        "save_dolby" => save_dolby(argument(args, "id")?, argument(args, "profile")?, argument(args, "expected")?, app.state()).await,
        "start_dolby_read" => start_dolby_read(argument(args, "id")?, app.state(), app.state()).await,
        "read_dolby" => read_dolby(argument(args, "token")?, app.state()),
        "cancel_dolby_read" => cancel_dolby_read(argument(args, "token")?, app.state()).await,
        "start_dolby_apply" => start_dolby_apply(argument(args, "id")?, argument(args, "profile")?, argument(args, "expected")?, argument(args, "token")?, app.state(), app.state()).await,
        "read_dolby_apply" => read_dolby_apply(argument(args, "token")?, app.state()).await,
        "cancel_dolby_apply" => cancel_dolby_apply(argument(args, "token")?, app.state()).await,
        _ => Err("此操作不可用。".to_string().into()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn os_secret_matches_browser_format() {
        let token = unsafe { format!("{:?}{:?}", windows::Win32::System::Com::CoCreateGuid().unwrap(), windows::Win32::System::Com::CoCreateGuid().unwrap()) }.replace('-', "");
        assert_eq!(token.len(), 64); assert!(token.bytes().all(|b| b.is_ascii_hexdigit()));
    }
    #[test] fn bounded_http_rejects_ambiguous_lengths_and_chunked_requests() {
        fn parse(bytes: &[u8]) -> std::io::Result<Request> {
            let listener = TcpListener::bind("127.0.0.1:0").unwrap();
            let mut client = TcpStream::connect(listener.local_addr().unwrap()).unwrap();
            client.write_all(bytes).unwrap(); client.shutdown(std::net::Shutdown::Write).unwrap();
            let (mut server, _) = listener.accept().unwrap(); read_request(&mut server)
        }
        let valid = parse(b"POST /rpc HTTP/1.1\r\nHost: 127.0.0.1:1\r\nContent-Length: 2\r\n\r\n{}").unwrap();
        assert_eq!(valid.body, b"{}");
        for extra in ["Content-Length: 1\r\nContent-Length: 1", "Content-Length: 3000000", "Content-Length: -1", "Transfer-Encoding: chunked", "Host: duplicate", "Content-Length: 3"] {
            assert!(parse(format!("POST /rpc HTTP/1.1\r\nHost: localhost\r\n{extra}\r\n\r\n{{}}").as_bytes()).is_err());
        }
    }
    fn host() -> Host { Host { authority: "127.0.0.1:1234".into(), token: "secret".into(), lifetime: Mutex::new(Lifetime::new()), connections: AtomicUsize::new(0), stop: AtomicBool::new(false) } }
    #[test] fn rejects_foreign_origins_hosts_missing_or_wrong_tokens() {
        let host = host();
        let mut request = Request { method: "POST".into(), path: "/rpc".into(), headers: [("host", "127.0.0.1:1234"), ("origin", "http://127.0.0.1:1234"), ("x-audioswitch-token", "secret")].into_iter().map(|(k,v)| (k.into(),v.into())).collect(), body: vec![] };
        assert!(authorized(&request, &host));
        for (key, bad) in [("host", "evil.test:1234"), ("origin", "https://evil.test"), ("x-audioswitch-token", "wrong"), ("sec-fetch-site", "cross-site")] {
            let old = request.headers.insert(key.into(), bad.into()); assert!(!authorized(&request, &host));
            if let Some(old) = old { request.headers.insert(key.into(), old); } else { request.headers.remove(key); }
        }
        request.headers.remove("origin"); assert!(!authorized(&request, &host));
        request.method = "GET".into(); assert!(authorized(&request, &host));
        request.headers.remove("x-audioswitch-token"); assert!(!authorized(&request, &host));
    }
    #[test] fn last_tab_controls_expiry_and_reload_can_rejoin() {
        let mut life = Lifetime::new(); life.joined(); life.joined(); life.left();
        life.deadline = Instant::now(); assert!(!life.expired()); life.left(); assert!(!life.expired());
        life.deadline = Instant::now(); assert!(life.expired()); life.joined(); assert!(!life.expired());
        life.left(); life.opened(); assert!(life.deadline > Instant::now() + Duration::from_secs(30));
    }
    #[test] fn retained_port_expires_only_after_release_and_last_client() {
        let mut life = Lifetime::new(); life.keep(true); life.deadline = Instant::now();
        assert!(!life.expired()); life.joined(); life.left(); life.deadline = Instant::now(); assert!(!life.expired());
        life.keep(false); assert!(!life.expired()); life.deadline = Instant::now(); assert!(life.expired());
        life.joined(); life.keep(false); life.deadline = Instant::now(); assert!(!life.expired());
        life.left(); assert!(!life.expired()); life.deadline = Instant::now(); assert!(life.expired());
    }
    #[test] fn assets_are_fixed_and_rpc_rejects_extra_envelope_fields() {
        for path in ["/../settings.json", "/%2e%2e/config", "/assets/old.js", "/rpc?token=secret"] { assert!(asset(path).is_none()); }
        assert!(asset("/?browser").is_some());
        assert!(serde_json::from_value::<Rpc>(json!({"command":"read_snapshot", "args":{}, "path":"C:/settings.json"})).is_err());
    }
}
