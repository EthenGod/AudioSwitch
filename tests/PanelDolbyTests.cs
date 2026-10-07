using System;
using System.Linq;
namespace AudioSwitch
{
    internal static class PanelDolbyTests
    {
        internal static void Run(Action<bool, string> check)
        {
            var p = new Preferences(); var state = new AudioState();
            state.Devices.Add(new Endpoint { Id = "current", Name = "同名设备", Flow = 0 }); state.Defaults["0:1"] = "current";
            p.DeviceOrder.Add(new Endpoint { Id = "offline", Name = "同名设备", Flow = 0 });
            p.DeviceOrder.Add(new Endpoint { Id = "mic", Name = "麦克风", Flow = 1 });
            var old = new DolbyProfile { MainProfile = 4, SubProfile = 4, Eq = Enumerable.Range(0,20).ToArray() };
            p.DeviceProfiles["offline"] = new DeviceProfile { Volume = 0, SpatialFormat = "", Dolby = old }; p.DeviceRules["offline"] = DeviceRule.AcceptSystem;
            var request = Wire.Decode<Request>(Wire.Encode(new Request { DeviceId="offline", DolbyProfile=new DolbyProfile { Enabled=false, SurroundStrength=0 }, ExpectedDolbyProfile=old }));
            int saved = 0;
            PanelProfile.SaveDolby(p, state, request, delegate { saved++; });
            check(saved == 1 && p.DeviceProfiles["offline"].Dolby.Enabled == false && p.DeviceProfiles["offline"].Dolby.SurroundStrength == 0, "Dolby save preserves false and zero");
            check(p.DeviceProfiles["offline"].Volume == 0 && p.DeviceProfiles["offline"].SpatialFormat == "" && p.DeviceRules["offline"] == DeviceRule.AcceptSystem, "Dolby-only save retains basic preset and whitelist");
            check(!p.DeviceProfiles.ContainsKey("current") && state.Default(0,1) == "current", "offline same-name Dolby save does not change routing");
            Reject(() => PanelProfile.SaveDolby(p,state,request,()=>saved++),check,"Dolby save rejects stale baseline");
            request.ExpectedDolbyProfile = p.DeviceProfiles["offline"].Dolby; request.DolbyProfile = old;
            p.DeviceProfiles["offline"].Volume = 65;
            var before = Wire.Encode(p);
            Reject(() => PanelProfile.SaveDolby(p,state,request,()=> { throw new InvalidOperationException("disk"); }),check,"Dolby save reports persistence failure");
            check(before == Wire.Encode(p), "Dolby persistence failure restores in-memory state");
            PanelProfile.SaveDolby(p,state,request,()=>saved++);
            check(p.DeviceProfiles["offline"].Volume == 65 && p.DeviceProfiles["offline"].Dolby.Eq.SequenceEqual(old.Eq), "Dolby saves exact twenty bands and merges newer volume");
            request.ExpectedDolbyProfile = old; request.DolbyProfile = null;
            PanelProfile.SaveDolby(p,state,request,()=>saved++);
            check(p.DeviceProfiles["offline"].Dolby == null && p.DeviceProfiles["offline"].Volume == 65,"Dolby disabled plan preserves basic preset");
            request.Value = true; Reject(()=>PanelProfile.SaveDolby(p,state,request,()=>saved++),check,"Dolby save-only rejects apply flag");
            request.Value = false; request.ExpectedDolbyProfile = null; request.DolbyProfile = new DolbyProfile { MainProfile=4, SubProfile=4, Eq=new int[19] };
            Reject(()=>PanelProfile.SaveDolby(p,state,request,()=>saved++),check,"Dolby invalid EQ rejected before persistence");
            request.DolbyProfile = null; request.DeviceId = "mic"; Reject(()=>PanelProfile.SaveDolby(p,state,request,()=>saved++),check,"microphone Dolby save rejected");
            request.DeviceId = "missing"; Reject(()=>PanelProfile.SaveDolby(p,state,request,()=>saved++),check,"unknown device Dolby save rejected");
            check(saved == 3, "rejected Dolby saves never persist");
        }
        private static void Reject(Action action, Action<bool,string> check, string label) { bool rejected=false; try { action(); } catch(InvalidOperationException) { rejected=true; } check(rejected,label); }
    }
}
