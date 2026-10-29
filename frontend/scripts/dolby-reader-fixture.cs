// Isolated process fixture; never loads audio or Dolby libraries.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;
class DolbyReaderFixture
{
    static void Main(string[] args)
    {
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush=true });
        var json = new JavaScriptSerializer();
        var request = json.Deserialize<Dictionary<string,object>>(Console.ReadLine());
        if (args.Length != 1 || args[0] != "--dolby-worker" || !request.ContainsKey("Profile") || request["Profile"] != null || Convert.ToInt32(request["OwnerPid"]) <= 0)
            throw new InvalidOperationException("Capture must never receive a write profile.");
        string id = (string)request["DeviceId"];
        if (id == "waiting") Thread.Sleep(30000);
        if (id == "failure") Console.WriteLine(json.Serialize(new { Error="中文读取失败", Profile=(object)null }));
        else Console.WriteLine(json.Serialize(new { Error=(string)null, Profile=new { MainProfile=4, SubProfile=4, Enabled=false, SurroundStrength=0, Eq=new int[20] } }));
    }
}
