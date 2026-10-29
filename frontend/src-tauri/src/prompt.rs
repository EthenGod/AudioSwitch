use serde_json::{json, Value};
use std::sync::Mutex;
use std::os::windows::io::{FromRawHandle, AsRawHandle, OwnedHandle};
use windows_sys::Win32::System::Threading::{OpenProcess, WaitForSingleObject, PROCESS_SYNCHRONIZE};
use windows_sys::Win32::Foundation::WAIT_TIMEOUT;

pub struct Session { owner: Mutex<Option<(u32, OwnedHandle)>> }
impl Default for Session { fn default() -> Self { Self { owner: Mutex::new(None) } } }
impl Session {
    pub fn bind(&self, pid: u32) -> Result<(), String> {
        let mut owner = self.owner.lock().map_err(|_| "无法读取后台状态。")?;
        if let Some((original, handle)) = owner.as_ref() {
            if *original != pid || unsafe { WaitForSingleObject(handle.as_raw_handle(), 0) } != WAIT_TIMEOUT {
                return Err("所属后台已退出，请重新打开界面。".into());
            }
        } else {
            let handle = unsafe { OpenProcess(PROCESS_SYNCHRONIZE, 0, pid) };
            if handle.is_null() { return Err("所属后台已退出或无法访问。".into()); }
            *owner = Some((pid, unsafe { OwnedHandle::from_raw_handle(handle) }));
        }
        Ok(())
    }
    pub fn exited(&self) -> bool {
        self.owner.lock().map(|owner| owner.as_ref().is_some_and(|(_, h)| unsafe { WaitForSingleObject(h.as_raw_handle(), 0) } != WAIT_TIMEOUT)).unwrap_or(true)
    }
}

#[derive(serde::Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", deny_unknown_fields)]
pub enum Action { Selected { token: String, id: String }, Previous { token: String }, Current { token: String } }

pub fn panel_opened(reply: &Value) -> Result<(), String> {
    let error = if reply.get("OperationError").is_some() { reply["OperationError"].as_str() } else { reply["Error"].as_str() };
    if let Some(error) = error { return Err(error.to_string()); }
    if reply["FrontendPid"].as_u64().and_then(|v| u32::try_from(v).ok()).filter(|v| *v > 0).is_none() {
        return Err("后台尚未确认面板已打开，请重试。".into());
    }
    Ok(())
}
impl Action {
    pub fn request(&self, snapshot: &Value) -> Result<Value, String> {
        if snapshot["PanelApiVersion"].as_u64().unwrap_or(0) < 1 { return Err("当前后台仅支持查看，请更新后台后再操作。".into()); }
        let token = match self { Self::Selected { token, .. } | Self::Previous { token } | Self::Current { token } => token };
        let pending = snapshot["Pending"].as_array().and_then(|rows| rows.iter().find(|p| p["Token"].as_str() == Some(token)))
            .ok_or("设备状态已变化，请刷新后重新选择。")?;
        let flow = pending["Flow"].as_u64().filter(|f| *f <= 1).ok_or("设备方向无效。")?;
        let devices = snapshot["State"]["Devices"].as_array().ok_or("设备列表不完整。")?;
        let online = |id: &str| devices.iter().any(|d| d["Id"] == id && d["Flow"] == flow);
        match self {
            Self::Selected { id, .. } => {
                if id.is_empty() || id.len() > 2048 || !online(id) { return Err("设备已断开或不属于此提示，请重新选择。".into()); }
                Ok(json!({"Action":"alternative", "Token":token, "DeviceId":id}))
            },
            Self::Previous { .. } => {
                if pending["DisconnectedDevices"].as_array().is_some_and(|d| !d.is_empty()) { return Err("旧设备已经断开。".into()); }
                let count = if snapshot["Preferences"]["IncludeCommunications"] == true { 3 } else { 2 };
                let ids: Vec<_> = (0..count).filter_map(|role| pending["PreviousDefaults"][format!("{flow}:{role}")].as_str()).filter(|id| !id.is_empty()).collect();
                if ids.is_empty() || ids.iter().any(|id| !online(id)) { return Err("旧设备已经断开，请重新选择。".into()); }
                Ok(json!({"Action":"old", "Token":token}))
            },
            Self::Current { .. } => Ok(json!({"Action":"later", "Token":token})),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn panel_open_requires_confirmed_process_and_preserves_launch_error() {
        assert!(panel_opened(&json!({"FrontendPid":0})).is_err());
        assert!(panel_opened(&json!({"FrontendPid":u64::MAX})).is_err());
        assert_eq!(panel_opened(&json!({"FrontendPid":42,"OperationError":"文件校验失败"})), Err("文件校验失败".into()));
        assert!(panel_opened(&json!({"FrontendPid":42,"OperationError":null,"Error":"unrelated audio read failure"})).is_ok());
    }
    #[test] fn bound_owner_cannot_be_replaced_or_reused() {
        let session = Session::default();
        assert!(!session.exited());
        assert!(session.bind(0).is_err());
        session.bind(std::process::id()).unwrap();
        session.bind(std::process::id()).unwrap();
        assert!(!session.exited());
        assert!(session.bind(u32::MAX).is_err());
    }
    fn snapshot() -> Value { json!({"PanelApiVersion":3,"Preferences":{"IncludeCommunications":false},"Pending":[{"Token":"t","Flow":0,"PreviousDefaults":{"0:0":"a","0:1":"a","0:2":"gone"},"DisconnectedDevices":[]}],"State":{"Devices":[{"Id":"a","Flow":0},{"Id":"b","Flow":1}]}}) }
    #[test] fn selection_checks_token_flow_and_online_id() {
        for (token, id) in [("old", "a"), ("t", "b"), ("t", "gone")] { assert!(Action::Selected {token:token.into(),id:id.into()}.request(&snapshot()).is_err()); }
        assert_eq!(Action::Selected {token:"t".into(),id:"a".into()}.request(&snapshot()).unwrap()["Action"], "alternative");
    }
    #[test] fn previous_respects_communications_and_disconnection() {
        let action = Action::Previous {token:"t".into()}; let mut s = snapshot(); assert!(action.request(&s).is_ok());
        s["Preferences"]["IncludeCommunications"] = json!(true); assert!(action.request(&s).is_err());
        s["Preferences"]["IncludeCommunications"] = json!(false); s["Pending"][0]["DisconnectedDevices"] = json!([{"Id":"gone"}]); assert!(action.request(&s).is_err());
    }
    #[test] fn current_only_dismisses_and_unknown_commands_rejected() {
        assert_eq!(Action::Current {token:"t".into()}.request(&snapshot()).unwrap(), json!({"Action":"later","Token":"t"}));
        assert!(serde_json::from_value::<Action>(json!({"kind":"current","token":"t","Action":"switch"})).is_err());
    }
}
