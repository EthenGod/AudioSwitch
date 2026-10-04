use serde::Deserialize;
use serde_json::{json, Value};

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "PascalCase", deny_unknown_fields)]
pub struct BasicProfile { pub volume: Option<u8>, pub spatial_format: Option<String> }

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum Preference { DarkMode, GameMode, AskOnConnect, IncludeCommunications, UseDevicePriority, AutoUpdateEnabled }

#[derive(Debug, Deserialize)]
#[serde(tag = "kind", rename_all = "camelCase", rename_all_fields = "camelCase", deny_unknown_fields)]
pub enum Action {
    Switch { id: String },
    SaveBasic { id: String, profile: BasicProfile, expected_profile: Option<BasicProfile>, rule: u8, expected_rule: u8 },
    Preference { key: Preference, value: bool },
    Reorder { flow: u8, ids: Vec<String> },
    Startup { value: bool, expected_command: Option<String> },
}

fn id(value: &str) -> Result<(), String> {
    if value.is_empty() || value.len() > 2048 || value.contains(['\0', '\r', '\n']) { Err("无效的设备标识。".into()) } else { Ok(()) }
}
fn profile(value: &BasicProfile) -> Result<Value, String> {
    if value.volume.is_some_and(|v| v > 100) { return Err("音量必须在 0 到 100 之间。".into()); }
    if value.spatial_format.as_ref().is_some_and(|v| v.len() > 128 || v.contains(['\0', '\r', '\n'])) { return Err("空间音效格式无效。".into()); }
    Ok(json!({ "Volume": value.volume, "SpatialFormat": value.spatial_format }))
}

impl Action {
    pub fn request(&self, snapshot: &Value) -> Result<Value, String> {
        if snapshot["PanelApiVersion"].as_u64().unwrap_or(0) < 1 {
            return Err("当前后台仅支持只读预览，请退出原后台后使用本阶段构建的后台。".into());
        }
        Ok(match self {
            Self::Switch { id: device } => { id(device)?; json!({"Action":"switch", "DeviceId": device}) },
            Self::SaveBasic { id: device, profile: next, expected_profile, rule, expected_rule } => {
                id(device)?;
                if *rule > 2 || *expected_rule > 2 { return Err("白名单规则无效。".into()); }
                json!({"Action":"saveBasicDeviceSettings", "DeviceId":device, "Profile":profile(next)?,
                    "ExpectedProfile":expected_profile.as_ref().map(profile).transpose()?, "DeviceRule":rule, "ExpectedDeviceRule":expected_rule, "Value":false})
            },
            Self::Preference { key, value } => json!({"Action":match key {
                Preference::DarkMode => "darkMode", Preference::GameMode => "gameMode", Preference::AskOnConnect => "ask",
                Preference::IncludeCommunications => "communications", Preference::UseDevicePriority => "priority", Preference::AutoUpdateEnabled => "automaticUpdates",
            }, "Value":value}),
            Self::Reorder { flow, ids } => {
                if *flow > 1 || ids.len() > 4096 { return Err("设备排序无效。".into()); }
                let mut unique = std::collections::HashSet::new();
                for item in ids { id(item)?; if !unique.insert(item) { return Err("设备排序有重复项。".into()); } }
                json!({"Action":"deviceOrder", "Flow":flow, "DeviceOrder":ids})
            },
            Self::Startup { value, expected_command } => {
                let path = snapshot["BackendExecutablePath"].as_str().filter(|s| !s.is_empty()).ok_or("无法确认后台程序位置。")?;
                if snapshot["Startup"]["Available"] != true { return Err("当前无法修改自启，请查看后台自启说明。".into()); }
                if expected_command.as_ref().is_some_and(|s| s.len() > 32768) { return Err("自启登记信息无效。".into()); }
                // Path comes only from the backend; never register the panel executable.
                json!({"Action":"startup", "Value":value, "StartupExecutablePath":path, "ExpectedStartupCommand":expected_command})
            },
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn strict_command_allowlist_and_types() {
        for value in [json!({"kind":"importSettings"}), json!({"kind":"preference","key":"DarkMode","value":"true"}),
            json!({"kind":"switch","id":"x","Action":"refresh"}), json!({"kind":"saveBasic","id":"x","profile":{"Volume":0,"SpatialFormat":null,"Dolby":{}} ,"rule":0,"expectedRule":0})] {
            assert!(serde_json::from_value::<Action>(value).is_err());
        }
    }
    #[test]
    fn basic_save_is_never_apply_and_preserves_null_and_zero() {
        let action: Action = serde_json::from_value(json!({"kind":"saveBasic","id":"x","profile":{"Volume":0,"SpatialFormat":null},"expectedProfile":null,"rule":1,"expectedRule":0})).unwrap();
        let request = action.request(&json!({"PanelApiVersion":1})).unwrap();
        assert_eq!(request["Action"], "saveBasicDeviceSettings"); assert_eq!(request["Value"], false);
        assert_eq!(request["Profile"]["Volume"], 0); assert_eq!(request["Profile"]["SpatialFormat"], Value::Null);
        assert!(request["Profile"].get("Dolby").is_none());
        assert!(action.request(&json!({})).is_err());
    }
    #[test]
    fn startup_uses_backend_path_and_keeps_expected_registration() {
        let action = Action::Startup { value:true, expected_command:Some("old".into()) };
        let request = action.request(&json!({"PanelApiVersion":1,"BackendExecutablePath":"C:\\声间\\AudioSwitch.exe","Startup":{"Available":true}})).unwrap();
        assert_eq!(request["StartupExecutablePath"], "C:\\声间\\AudioSwitch.exe"); assert_eq!(request["ExpectedStartupCommand"], "old");
        assert!(Action::Reorder{ flow:0, ids:vec!["a".into(),"a".into()] }.request(&json!({"PanelApiVersion":1})).is_err());
    }
}
