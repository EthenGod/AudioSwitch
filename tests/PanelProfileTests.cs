// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;

namespace AudioSwitch
{
    internal static class PanelProfileTests
    {
        internal static void Run(Action<bool, string> check)
        {
            var preferences = new Preferences();
            var state = new AudioState();
            state.Devices.Add(new Endpoint { Id = "online", Name = "同名设备", Flow = 0 });
            preferences.DeviceOrder.Add(new Endpoint { Id = "offline", Name = "同名设备", Flow = 0 });
            preferences.DeviceOrder.Add(new Endpoint { Id = "mic", Name = "麦克风", Flow = 1 });
            state.Defaults["0:1"] = "online";
            var dolby = new DolbyProfile { MainProfile = 4, SubProfile = 4, Enabled = true, Eq = Enumerable.Range(0, 20).ToArray() };
            preferences.DeviceProfiles["offline"] = new DeviceProfile { Volume = 35, SpatialFormat = null, Dolby = dolby };
            var request = new Request { Action = "saveBasicDeviceSettings", DeviceId = "offline", Profile = new DeviceProfile { Volume = 0, SpatialFormat = "" },
                ExpectedProfile = new DeviceProfile { Volume = 35, SpatialFormat = null }, DeviceRule = DeviceRule.AcceptSystem, ExpectedDeviceRule = DeviceRule.Normal };
            request = Wire.Decode<Request>(Wire.Encode(request));
            int saves = 0;
            // A newer Dolby edit must survive an older basic-editor draft.
            dolby.Dialog = true;
            PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; });
            check(saves == 1 && preferences.DeviceProfiles["offline"].Volume == 0 && preferences.DeviceProfiles["offline"].SpatialFormat == "", "panel offline save preserves zero/off");
            check(Wire.Encode(preferences.DeviceProfiles["offline"].Dolby) == Wire.Encode(dolby), "panel save preserves latest complete Dolby profile");
            check(!preferences.DeviceProfiles.ContainsKey("online") && state.Default(0, 1) == "online", "panel same-name save matches ID and leaves routing unchanged");
            check(DeviceAutomation.Rule(preferences, "offline") == DeviceRule.AcceptSystem, "panel saves whitelist independently");
            Reject(delegate { PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; }); }, check, "panel rejects stale draft");
            check(saves == 1, "panel conflict never persists");
            request.ExpectedProfile = new DeviceProfile { Volume = 0, SpatialFormat = "" }; request.ExpectedDeviceRule = DeviceRule.AcceptSystem;
            request.Profile = new DeviceProfile(); request.DeviceRule = DeviceRule.Normal;
            var before = Wire.Encode(preferences);
            Reject(delegate { PanelProfile.SaveBasic(preferences, state, request, delegate { throw new InvalidOperationException("磁盘写入失败"); }); }, check, "panel surfaces persistence failure");
            check(Wire.Encode(preferences) == before, "panel persistence failure restores in-memory profile and rule");
            PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; });
            check(preferences.DeviceProfiles["offline"].Volume == null && preferences.DeviceProfiles["offline"].SpatialFormat == null, "panel null remains keep unchanged");
            check(!preferences.DeviceRules.ContainsKey("offline"), "panel global rule removes whitelist override");
            request.ExpectedProfile = new DeviceProfile(); request.ExpectedDeviceRule = DeviceRule.Normal;
            request.Profile.Dolby = new DolbyProfile();
            Reject(delegate { PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; }); }, check, "panel rejects Dolby payload");
            request.Profile.Dolby = null; request.Value = true;
            Reject(delegate { PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; }); }, check, "panel cannot save and apply");
            request.Value = false; request.DeviceId = "mic"; request.ExpectedProfile = null; request.Profile.SpatialFormat = "";
            Reject(delegate { PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; }); }, check, "panel rejects microphone spatial writes");
            request.Profile.SpatialFormat = null; request.Profile.Volume = 101;
            Reject(delegate { PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; }); }, check, "panel validates volume range");
            request.Profile.Volume = null;
            PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; });
            check(preferences.DeviceProfiles.ContainsKey("mic") && saves == 3, "panel saves absent profile without capturing current volume");
            request.DeviceId = "missing";
            Reject(delegate { PanelProfile.SaveBasic(preferences, state, request, delegate { saves++; }); }, check, "panel rejects unknown endpoint");
        }
        private static void Reject(Action action, Action<bool, string> check, string label)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, label);
        }
    }
}
