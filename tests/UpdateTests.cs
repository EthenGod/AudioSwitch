// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;

namespace AudioSwitch
{
    internal static class UpdateTests
    {
        private static bool Rejects(Action action)
        { try { action(); return false; } catch (Exception) { return true; } }
        private static Dictionary<string, object> Release(string tag)
        {
            string name = "AudioSwitch-" + tag + "-win-x64.zip";
            return new Dictionary<string, object> { { "tag_name", tag }, { "draft", false }, { "prerelease", false }, { "body", "更新说明：保留设备配置" },
                { "assets", new object[] { new Dictionary<string, object> { { "name", name }, { "browser_download_url", AppUpdate.ReleasesUrl + "/download/" + tag + "/" + name },
                    { "size", 4096 }, { "digest", "sha256:" + new string('a', 64) } } } } };
        }
        private static Dictionary<string, object> Asset(Dictionary<string, object> release)
        { return (Dictionary<string, object>)((object[])release["assets"])[0]; }
        private static void Fixture(string directory, string prefix)
        {
            foreach (string name in AppUpdate.Files)
            {
                string path = Path.Combine(directory, name); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, prefix + name);
            }
        }
        internal static void Run(Action<bool, string> check)
        {
            check(AppUpdate.ParseVersion("v0.10.10") > AppUpdate.ParseVersion("v0.10.2"), "update versions compare numerically");
            foreach (string invalid in new[] { "v0.11.0-rc1", "latest", "0.11", "1.0.0.0", "v01.2.3", "1.0.65535", "1.0.2\n" })
                check(Rejects(() => AppUpdate.ParseVersion(invalid)), "reject unsupported update version: " + invalid);
            var current = AppUpdate.ParseVersion(AppVersion.Number);
            string next = "v" + new Version(current.Major, current.Minor + 1, 0).ToString(3);
            var release = Release(next);
            var parsed = AppUpdate.ParseRelease(Wire.Encode(release));
            check(parsed.Version > current && parsed.Notes.Contains("保留设备配置"), "parse GitHub release and UTF-8 notes");
            var single = new Dictionary<string, object> { { "name", "AudioSwitch.exe" } };
            release["assets"] = new object[] { Asset(release), single };
            check(AppUpdate.ParseRelease(Wire.Encode(release)).Url == parsed.Url, "new single-EXE asset does not change the full package selected for updates");
            check(AppUpdate.ParseRelease(Wire.Encode(Release("v0.10.1"))).Version < current, "existing v0.10.1 is never offered as downgrade");
            release["prerelease"] = true; check(AppUpdate.ParseRelease(Wire.Encode(release)) == null, "preview releases are ignored");
            release["prerelease"] = false; release["draft"] = true; check(AppUpdate.ParseRelease(Wire.Encode(release)) == null, "draft releases are ignored");
            release = Release(next); Asset(release)["browser_download_url"] = "https://example.com/AudioSwitch.zip";
            check(Rejects(() => AppUpdate.ParseRelease(Wire.Encode(release))), "reject update from another host");
            release = Release(next); Asset(release)["digest"] = null;
            check(Rejects(() => AppUpdate.ParseRelease(Wire.Encode(release))), "missing download checksum blocks installation");
            release = Release(next); Asset(release)["size"] = "4096";
            check(Rejects(() => AppUpdate.ParseRelease(Wire.Encode(release))), "string package size rejected");
            release = Release(next); Asset(release)["size"] = AppUpdate.MaxPackage + 1;
            check(Rejects(() => AppUpdate.ParseRelease(Wire.Encode(release))), "oversized package rejected");
            release = Release(next); Asset(release)["name"] = "Source-code.zip";
            check(Rejects(() => AppUpdate.ParseRelease(Wire.Encode(release))), "source archive cannot be selected for install");
            release = Release(next); release["assets"] = new object[] { Asset(release), Asset(release) };
            check(Rejects(() => AppUpdate.ParseRelease(Wire.Encode(release))), "ambiguous duplicate assets rejected");
            foreach (string url in new[] { "http://github.com/a", "https://github.com.evil.test/a", "https://github.com:8443/a", "https://user@github.com/a" })
                check(!AppUpdate.AllowedDownload(new Uri(url)), "reject unsafe redirect: " + url);
            using (var input = new MemoryStream(new byte[20])) using (var output = new MemoryStream())
                check(Rejects(() => AppUpdate.CopyBounded(input, output, 10, CancellationToken.None, null)), "stream size limit enforced independently of response headers");
            using (var cancel = new CancellationTokenSource()) using (var input = new MemoryStream(new byte[20])) using (var output = new MemoryStream())
            { cancel.Cancel(); check(Rejects(() => AppUpdate.CopyBounded(input, output, 20, cancel.Token, null)) && output.Length == 0, "cancelled download writes no further bytes"); }

            string work = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            string payload = Path.Combine(work, "payload"); Fixture(payload, "new-");
            string zip = Path.Combine(work, "valid.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (string name in AppUpdate.Files) archive.CreateEntryFromFile(Path.Combine(payload, name), name);
            string extracted = Path.Combine(work, "extracted"); AppUpdate.Extract(zip, extracted, CancellationToken.None);
            check(AppUpdate.Files.All(name => File.ReadAllText(Path.Combine(extracted, name)) == "new-" + name), "exact distribution files extracted");
            check(Rejects(() => AppUpdate.ValidatePayload(extracted, current)), "non-executable payload rejected before shutdown");
            check(AppUpdate.Hash(zip).Length == 64, "package SHA-256 calculated");
            foreach (string entry in new[] { "../outside.txt", "vendor/../../outside.txt", "C:/outside.txt", "settings.json", "AudioSwitch.exe" })
            {
                string badZip = Path.Combine(work, Guid.NewGuid().ToString("N") + ".zip");
                File.Copy(zip, badZip);
                using (var archive = ZipFile.Open(badZip, ZipArchiveMode.Update)) using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("bad");
                check(Rejects(() => AppUpdate.Extract(badZip, Path.Combine(work, Guid.NewGuid().ToString("N")), CancellationToken.None)), "reject extra/duplicate/traversal ZIP entry: " + entry);
            }
            string target = Path.Combine(work, "installed"); Fixture(target, "old-");
            File.WriteAllText(Path.Combine(target, "settings.json"), "keep-settings");
            string backup = Path.Combine(work, "backup-success");
            UpdateInstaller.ApplyFiles(payload, target, backup);
            check(AppUpdate.Files.All(name => File.ReadAllText(Path.Combine(target, name)) == "new-" + name), "successful update replaces every distribution file");
            check(AppUpdate.Files.All(name => File.ReadAllText(Path.Combine(backup, name)) == "old-" + name), "complete previous distribution is preserved");
            check(File.ReadAllText(Path.Combine(target, "settings.json")) == "keep-settings", "settings file is untouched");
            Fixture(target, "old-");
            string failure = null;
            try { UpdateInstaller.ApplyFiles(payload, target, Path.Combine(work, "backup-rollback"), name => {
                if (name == "vendor/svcl/svcl.exe") { File.WriteAllText(Path.Combine(target, name), "partial"); throw new IOException("simulated partial write"); }
            }); } catch (IOException ex) { failure = ex.Message; }
            check(failure != null && failure.Contains("已核验恢复") && AppUpdate.Files.All(name => File.ReadAllText(Path.Combine(target, name)) == "old-" + name), "partially written file and all earlier writes are restored and verified");
            FileStream locked = null; failure = null;
            try { UpdateInstaller.ApplyFiles(payload, target, Path.Combine(work, "backup-failed-recovery"), name => {
                if (name == "AudioSwitch.exe") { locked = new FileStream(Path.Combine(target, name), FileMode.Open, FileAccess.Read, FileShare.Read); throw new IOException("simulated failure"); }
            }); } catch (IOException ex) { failure = ex.Message; } finally { if (locked != null) locked.Dispose(); }
            check(failure != null && failure.Contains("部分文件未能恢复") && !failure.Contains("已核验恢复"), "failed rollback never claims full recovery");
            string absent = Path.Combine(work, "missing-original"); Directory.CreateDirectory(absent);
            check(Rejects(() => UpdateInstaller.ApplyFiles(payload, absent, Path.Combine(work, "backup-absent"), name => { throw new IOException("fail"); })) && !File.Exists(Path.Combine(absent, "AudioSwitch.exe")), "rollback moves newly introduced file aside without deleting it");
            AppUpdate.ValidatePayload(AppDomain.CurrentDomain.BaseDirectory, current);
            check(true, "real built x64 executable version and pinned SVCL pass package validation");
            check(Rejects(() => AppUpdate.ValidatePayload(AppDomain.CurrentDomain.BaseDirectory, new Version(99, 0, 0))), "mismatched binary version rejected");
            check(Rejects(() => UpdateInstaller.ValidateBackupPaths(work, Path.Combine(work, new string('x', 230)))), "overlong backup paths rejected before panel or tray shutdown");
            RunWebsiteTests(check, next);
        }
        private static string AssetHtml(string tag, string hash)
        {
            return "<li><a href='/EthenGod/AudioSwitch/releases/download/" + tag + "/AudioSwitch-" + tag + "-win-x64.zip'>Download</a>" +
                "<clipboard-copy value='sha256:" + hash + "'></clipboard-copy><span>212 KB</span></li>";
        }
        private static UpdatePage Page(string tag)
        {
            return new UpdatePage { Address = new Uri(AppUpdate.ReleasesUrl + "/tag/" + tag),
                Text = "<div data-test-selector=\"body-content\" class=\"markdown-body\"><p>更新 &amp; 改进</p><script>bad()</script><p>保留设置</p></div>" };
        }
        private static void RunWebsiteTests(Action<bool, string> check, string next)
        {
            var requests = new List<string>();
            var result = AppUpdate.Check(CancellationToken.None, (url, cancel) => {
                requests.Add(url); if (url != AppUpdate.ReleasesUrl + "/latest") throw new Exception("Unexpected API request");
                return Page("v0.10.1");
            });
            check(result.Tag == "v0.10.1" && requests.Count == 1, "already current/ahead version checks public release page without consuming API quota");
            check(result.Notes.Contains("更新 & 改进") && result.Notes.Contains("保留设置") && !result.Notes.Contains("bad()"), "website release notes are readable plain text");
            foreach (var status in new[] { WebExceptionStatus.ProtocolError, WebExceptionStatus.Timeout })
            {
                requests.Clear();
                result = AppUpdate.Check(CancellationToken.None, (url, cancel) => {
                    requests.Add(url);
                    if (url.EndsWith("/latest") && url != AppUpdate.ApiUrl) return Page(next);
                    if (url == AppUpdate.ApiUrl) throw new WebException("403 rate limit exceeded", status);
                    return new UpdatePage { Address = new Uri(url), Text = AssetHtml(next, new string('c', 64)) };
                });
                check(requests.Count == 3 && result.Digest == new string('c', 64) && result.Size == 0 && result.Url.EndsWith("-win-x64.zip"), "API failure uses exact official asset digest without treating rounded size as bytes: " + status);
            }
            requests.Clear();
            result = AppUpdate.Check(CancellationToken.None, (url, cancel) => {
                requests.Add(url); return url == AppUpdate.ApiUrl ? new UpdatePage { Address = new Uri(url), Text = Wire.Encode(Release(next)) } : Page(next);
            });
            check(requests.Count == 2 && result.Size == 4096, "available matching API metadata preserves exact size");
            var release = AppUpdate.ParseReleasePage(Page(next));
            foreach (string bad in new[] {
                AssetHtml(next, "invalid"),
                AssetHtml(next, new string('a', 64)) + AssetHtml(next, new string('a', 64)),
                AssetHtml(next, new string('a', 64)).Replace("/EthenGod/AudioSwitch/", "https://evil.test/"),
                AssetHtml(next, new string('a', 64)).Replace("<clipboard-copy", "</li><li><clipboard-copy") })
                check(Rejects(() => AppUpdate.ParseAssetPage(release, bad)), "website fallback rejects missing/ambiguous/wrong-asset checksum");
            foreach (string url in new[] { "https://github.com/login", "https://github.com/other/AudioSwitch/releases/tag/v1.0.0", AppUpdate.ReleasesUrl + "/tag/v1.0.0-rc1", AppUpdate.ReleasesUrl + "/tag/v1.0.0?x=y" })
                check(Rejects(() => AppUpdate.ParseReleasePage(new UpdatePage { Address = new Uri(url), Text = "" })), "reject noncanonical latest-release redirect: " + url);
            using (var cancel = new CancellationTokenSource())
            {
                requests.Clear();
                check(Rejects(() => AppUpdate.Check(cancel.Token, (url, token) => {
                    requests.Add(url); cancel.Cancel(); return Page(next);
                })) && requests.Count == 1, "cancellation prevents API and fallback requests");
            }
        }
        internal static void OnlineCheck(Action<bool, string> check)
        {
            using (var cancel = new CancellationTokenSource(30000))
            {
                var result = AppUpdate.Check(cancel.Token);
                check(result != null && result.Version != null, "live GitHub release check succeeds through production code");
                Console.WriteLine("Latest published: " + result.Tag + "; local: v" + AppVersion.Number);
                var assets = AppUpdate.ReadPage(AppUpdate.ReleasesUrl + "/expanded_assets/" + result.Tag, cancel.Token);
                AppUpdate.ParseAssetPage(result, assets.Text);
                check(result.Digest.Length == 64 && result.Url.EndsWith("-win-x64.zip"), "live official asset page provides exact package SHA-256 without REST API");
            }
        }
        internal static void ProcessSmoke(Action<bool, string> check, bool repair = false)
        {
            Mutex existing;
            if (Mutex.TryOpenExisting("Local\\AudioSwitch-Host-" + Wire.Identity, out existing))
            { existing.Dispose(); throw new InvalidOperationException("更新进程测试需要先退出声间，以免占用真实后台的通信通道。"); }
            string work = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "更新 空格&()-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string target = Path.Combine(work, "installed"), payload = Path.Combine(work, "payload");
            Directory.CreateDirectory(work);
            foreach (string folder in new[] { target, payload }) foreach (string name in AppUpdate.Files)
            {
                string file = Path.Combine(folder, name); Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), file);
            }
            // A disposable stand-in for the panel and tray. It never creates AudioService or reads user preferences.
            string source = @"using System; using System.IO; using System.IO.Pipes; using System.Diagnostics; using System.Threading; using System.Security.Principal;
[assembly:System.Reflection.AssemblyVersion(""VERSION"")]
class Fixture {
 static void Main(string[] args) {
  string identity = WindowsIdentity.GetCurrent().User.Value + ""-"" + Process.GetCurrentProcess().SessionId;
  if (args[0] == ""--parent"") { using(var e=EventWaitHandle.OpenExisting(args[1])) e.WaitOne(25000); return; }
  if (args[0] == ""--host"") {
   using(var mutex = new Mutex(true, ""Local\\AudioSwitch-Host-""+identity)) {
    using(var ready=EventWaitHandle.OpenExisting(args[1])) ready.Set();
    for(int i=0;i<2;i++) using(var pipe=new NamedPipeServerStream(""AudioSwitch-""+identity,PipeDirection.InOut)) {
     pipe.WaitForConnection(); var reader=new StreamReader(pipe); var writer=new StreamWriter(pipe){AutoFlush=true};
     string request=reader.ReadLine();
     writer.WriteLine(request.Contains(""snapshot"") ? ""{\""BackendPid\"":""+Process.GetCurrentProcess().Id+""}"" : ""{}"");
    }
   } return;
  }
  if(args[0].StartsWith(""--updated="")) {
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,""fixture-started.txt""), ""started without touching audio"");
   using(var ready=EventWaitHandle.OpenExisting(args[0].Substring(10))) ready.Set();
  }
 }
}";
            string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            string manifest = Path.Combine(work, "integrity.txt");
            using (var input = typeof(FileIntegrity).Assembly.GetManifestResourceStream("AudioSwitch.Integrity"))
            using (var output = File.Create(manifest)) input.CopyTo(output);
            foreach (string folder in new[] { target, payload })
            {
                string cs = Path.Combine(work, folder == target ? "old.cs" : "new.cs");
                File.WriteAllText(cs, source.Replace("VERSION", folder == target || repair ? AppVersion.Number : "99.0.0"));
                string resource = repair && folder == payload ? " /resource:" + UpdateInstaller.Quote(manifest) + ",AudioSwitch.Integrity" : "";
                using (var compile = Process.Start(new ProcessStartInfo(compiler, "/nologo /utf8output /target:exe /platform:x64 /out:" + UpdateInstaller.Quote(Path.Combine(folder, "AudioSwitch.exe")) + resource + " " + UpdateInstaller.Quote(cs)) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 }))
                {
                    if (!compile.WaitForExit(15000)) throw new Exception("Update fixture compilation timed out.");
                    string diagnostic = compile.StandardOutput.ReadToEnd();
                    if (compile.ExitCode != 0) throw new Exception("Cannot compile update fixture: " + diagnostic);
                }
            }
            if (repair)
            {
                string path = Path.Combine(payload, "AudioSwitch.exe"); byte[] bytes = File.ReadAllBytes(path);
                string marker = "AudioSwitch-Self-SHA256-v1=";
                int offset = System.Text.Encoding.ASCII.GetString(bytes).IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    sha.TransformBlock(bytes, 0, offset, bytes, 0); sha.TransformFinalBlock(bytes, offset + 64, bytes.Length - offset - 64);
                    byte[] digest = System.Text.Encoding.ASCII.GetBytes(BitConverter.ToString(sha.Hash).Replace("-", ""));
                    Buffer.BlockCopy(digest, 0, bytes, offset, 64);
                }
                File.WriteAllBytes(path, bytes);
                check(FileIntegrity.Check(payload).Passed, "same-version repair fixture has valid executable and runtime hashes");
                foreach (string name in AppUpdate.Files.Except(FileIntegrity.RequiredFiles)) File.WriteAllText(Path.Combine(target, name), "keep optional file");
            }
            string helper = Path.Combine(work, "AudioSwitch.Update.exe");
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe"), helper);
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AudioSwitch.exe.config"), helper + ".config");
            string readyName = "Local\\AudioSwitch-Update-Ready-" + Guid.NewGuid().ToString("N");
            string hostReadyName = readyName + "-host";
            string originalHash = AppUpdate.Hash(Path.Combine(target, "AudioSwitch.exe"));
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName))
            using (var hostReady = new EventWaitHandle(false, EventResetMode.ManualReset, hostReadyName))
            using (var backend = Process.Start(new ProcessStartInfo(Path.Combine(target, "AudioSwitch.exe"), "--host " + UpdateInstaller.Quote(hostReadyName)) { UseShellExecute = false, CreateNoWindow = true }))
            using (var parent = Process.Start(new ProcessStartInfo(Path.Combine(target, "AudioSwitch.exe"), "--parent " + UpdateInstaller.Quote(readyName)) { UseShellExecute = false, CreateNoWindow = true }))
            {
                Process helperProcess = null;
                try
                {
                    check(hostReady.WaitOne(5000), "isolated fake tray host starts without audio access");
                    var plan = new UpdatePlan { Target = Path.Combine(target, "AudioSwitch.exe"), Payload = payload, Version = repair ? AppVersion.Number : "99.0.0", ParentPid = parent.Id, ReadyEvent = readyName, Repair = repair,
                        Hashes = (repair ? FileIntegrity.RequiredFiles : AppUpdate.Files).ToDictionary(name => name, name => AppUpdate.Hash(Path.Combine(payload, name))) };
                    string planFile = Path.Combine(work, "install.json"); File.WriteAllText(planFile, Wire.Encode(plan));
                    helperProcess = Process.Start(new ProcessStartInfo(helper, "--apply-update " + UpdateInstaller.Quote(planFile)) { UseShellExecute = false, CreateNoWindow = true });
                    check(helperProcess.WaitForExit(30000), "real update helper completes cross-process replacement and restart");
                    check(parent.HasExited && backend.HasExited, "update waits for the exact panel and tray processes to exit");
                    check(File.Exists(Path.Combine(target, "fixture-started.txt")), "new executable acknowledges startup across processes");
                    check(AppUpdate.Hash(Path.Combine(target, "AudioSwitch.exe")) == plan.Hashes["AudioSwitch.exe"], "installed executable matches verified payload");
                    string saved = Directory.GetDirectories(Path.Combine(target, "update-backups")).Single();
                    check(AppUpdate.Hash(Path.Combine(saved, "AudioSwitch.exe")) == originalHash, "old running executable preserved in complete backup");
                    check(!UpdateInstaller.IsUpdating(), "update mutex is released after helper exits");
                    if (repair) check(AppUpdate.Files.Except(FileIntegrity.RequiredFiles).All(name => File.ReadAllText(Path.Combine(target, name)) == "keep optional file"), "real same-version repair helper leaves all optional files untouched");
                }
                finally
                {
                    foreach (var process in new[] { helperProcess, parent, backend })
                    {
                        if (process == null) continue;
                        string expected = process == helperProcess ? helper : Path.Combine(target, "AudioSwitch.exe");
                        if (UpdateInstaller.Matches(process, expected)) { process.Kill(); process.WaitForExit(5000); }
                    }
                    if (helperProcess != null) helperProcess.Dispose();
                }
            }
        }
    }
}
