use serde_json::{json, Value};
use std::sync::Mutex;
use crate::dolby::{Profile, save_request};

#[derive(Default)]
pub struct Session { token: Mutex<Option<String>> }
impl Session {
    pub fn bind(&self, token: &str) { *self.token.lock().unwrap() = Some(token.into()); }
    pub fn require(&self, token: &str) -> Result<(), String> {
        if self.token.lock().unwrap().as_deref() != Some(token) { return Err("应用记录不属于此窗口，请检查当前音效。".into()); }
        Ok(())
    }
}
pub fn request(snapshot: &Value, id: &str, profile: &Profile, expected: &Option<Profile>, token: &str) -> Result<Value, String> {
    if snapshot["PanelApiVersion"].as_u64().unwrap_or(0) < 5 { return Err("当前后台不支持新的 Dolby 应用结果，请使用新版后台。".into()); }
    if token.len() != 32 || !token.bytes().all(|b| b.is_ascii_hexdigit()) { return Err("无效的应用标记。".into()); }
    if snapshot["DolbyApplying"] != false || snapshot["State"]["Defaults"]["0:1"] != id
        || !snapshot["State"]["Devices"].as_array().into_iter().flatten().any(|d| d["Id"] == id && d["Flow"] == 0) {
        return Err("请等待其他音效任务结束，并选择当前在线输出设备。".into());
    }
    let mut request = save_request(snapshot, id, &Some(profile.clone()), expected)?;
    request["Action"] = json!("saveAndApplyDolby"); request["Token"] = json!(token);
    // The web page cannot nominate a different owner or executable.
    request["OwnerPid"] = json!(std::process::id());
    Ok(request)
}
pub fn operation(reply: &Value, token: &str) -> Result<Value, String> {
    if let Some(error) = reply["OperationError"].as_str() { return Err(error.into()); }
    let job = &reply["DolbyOperation"];
    if job["Token"] != token || job["DeviceId"].as_str().is_none_or(str::is_empty) || job["Message"].as_str().is_none()
        || !matches!(job["Status"].as_str(), Some("running" | "cancelling" | "applied" | "warning" | "error" | "cancelled")) {
        return Err("后台未确认本次 Dolby 结果，请重新检查状态，勿重复应用。".into());
    }
    Ok(job.clone())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn restricts_apply_to_current_output_and_fixed_owner() {
        let mut snapshot = json!({"PanelApiVersion":5,"DolbyApplying":false,"State":{"Devices":[{"Id":"out","Flow":0}],"Defaults":{"0:1":"out"}}});
        let profile: Profile = serde_json::from_value(json!({"Enabled":false})).unwrap();
        let token = "0123456789abcdef0123456789abcdef";
        let value = request(&snapshot,"out",&profile,&None,token).unwrap();
        assert_eq!(value["OwnerPid"], std::process::id()); assert_eq!(value["DolbyProfile"]["Enabled"],false);
        assert_eq!(value["Value"],false); assert!(value.get("Profile").is_none());
        assert!(request(&snapshot,"other",&profile,&None,token).is_err());
        assert!(request(&snapshot,"out",&profile,&None,"x").is_err());
        snapshot["DolbyApplying"] = json!(true); assert!(request(&snapshot,"out",&profile,&None,token).is_err());
        snapshot["DolbyApplying"] = json!(false); snapshot["PanelApiVersion"] = json!(4); assert!(request(&snapshot,"out",&profile,&None,token).is_err());
    }
    #[test]
    fn rejects_foreign_and_ambiguous_results() {
        let session = Session::default(); session.bind("a"); assert!(session.require("b").is_err());
        let mut reply = json!({"DolbyOperation":{"Token":"a","DeviceId":"out","Status":"cancelling","Message":"正在恢复检查"}});
        assert_eq!(operation(&reply,"a").unwrap()["Status"],"cancelling");
        assert!(operation(&reply,"b").is_err()); reply["DolbyOperation"]["Status"] = json!("unknown"); assert!(operation(&reply,"a").is_err());
    }
}
