// Isolated lifecycle fixture: no audio libraries, configuration or native Dolby access.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;
internal static class ReadOnlyWaitWorker
{
    private static int Main(string[] args)
    {
        Console.SetIn(new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true)));
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        if (args.Length != 1) return 2;
        if (args[0] == "--panel-maintenance") {
            string kind = Console.ReadLine(); if (kind != "update" && kind != "files") return 2;
            Console.ReadLine(); // Cancel line or closed stdin from the owner.
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { Kind = kind, Status = "cancelled", Message = "等待夹具已取消，未读取或写入文件。" })); return 0;
        }
        if (args[0] == "--dolby-worker") {
            var request = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Console.ReadLine());
            if (!request.ContainsKey("Profile") || request["Profile"] != null) return 2;
            // Production Reader owns a Job Object and terminates this capture-only child.
            Thread.Sleep(20000); Console.WriteLine("{\"Error\":\"等待夹具结束，未读取或写入音频。\"}"); return 0;
        }
        return 2;
    }
}
