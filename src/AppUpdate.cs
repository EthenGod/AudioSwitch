// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace AudioSwitch
{
    internal sealed class UpdateRelease
    {
        internal Version Version;
        internal string Tag, Notes, Url, Digest;
        internal long Size;
    }
    internal sealed class UpdatePage
    {
        internal Uri Address;
        internal string Text;
    }
    internal static class AppUpdate
    {
        internal const string ReleasesUrl = "https://github.com/EthenGod/AudioSwitch/releases";
        internal const string ApiUrl = "https://api.github.com/repos/EthenGod/AudioSwitch/releases/latest";
        internal const long MaxPackage = 32 * 1024 * 1024;
        internal static readonly string[] Files = { "AudioSwitch.exe", "AudioSwitch.exe.config", "vendor/svcl/svcl.exe", "vendor/svcl/readme.txt", "vendor/svcl/svcl.chm", "LICENSE", "NOTICE.txt", "THIRD_PARTY.md" };
        internal static Version ParseVersion(string text)
        {
            if (text == null || !Regex.IsMatch(text, @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z"))
                throw new InvalidDataException("发布版本号应为 v主版本.次版本.修订号，例如 v0.11.0。");
            Version result;
            if (!Version.TryParse(text.TrimStart('v'), out result) || result.Major > 65534 || result.Minor > 65534 || result.Build > 65534)
                throw new InvalidDataException("发布版本号超出支持范围。");
            return result;
        }
        internal static UpdateRelease ParseRelease(string json)
        {
            var root = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !(root["draft"] is bool) || !(root["prerelease"] is bool)) throw new InvalidDataException("发布信息格式不正确。");
            if ((bool)root["draft"] || (bool)root["prerelease"]) return null;
            string tag = root["tag_name"] as string;
            var version = ParseVersion(tag);
            if (version <= ParseVersion(AppVersion.Number)) return new UpdateRelease { Version = version, Tag = tag };
            string name = "AudioSwitch-v" + version.ToString(3) + "-win-x64.zip";
            var assets = root["assets"] as object[];
            if (assets == null) throw new InvalidDataException("这个版本还没有可安装的 Windows 更新包。");
            var matches = assets.OfType<Dictionary<string, object>>().Where(a => a.ContainsKey("name") && (a["name"] as string) == name).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("这个版本缺少唯一的 Windows x64 更新包。请打开发布页查看。");
            var asset = matches[0]; object digest;
            string hash = asset.TryGetValue("digest", out digest) ? digest as string : null;
            if (hash == null || !Regex.IsMatch(hash, @"\Asha256:[a-fA-F0-9]{64}\z")) throw new InvalidDataException("发布包缺少 SHA-256 校验信息，请发布者重新上传附件。");
            string url = asset["browser_download_url"] as string;
            string expected = ReleasesUrl + "/download/" + tag + "/" + name;
            if (!String.Equals(url, expected, StringComparison.Ordinal)) throw new InvalidDataException("更新包地址不属于此项目的对应版本。");
            object size = asset["size"];
            if (!(size is int) && !(size is long)) throw new InvalidDataException("更新包大小无效。");
            long length = Convert.ToInt64(size);
            if (length <= 0 || length > MaxPackage) throw new InvalidDataException("更新包大小超出限制。");
            object notes;
            return new UpdateRelease { Version = version, Tag = tag, Url = url, Digest = hash.Substring(7), Size = length,
                Notes = root.TryGetValue("body", out notes) ? notes as string : "" };
        }
        internal static UpdateRelease Check(CancellationToken cancel)
        {
            return Check(cancel, ReadPage);
        }
        internal static UpdateRelease RepairRelease(CancellationToken cancel, Func<string, CancellationToken, UpdatePage> read = null)
        {
            read = read ?? ReadPage;
            var release = new UpdateRelease { Version = ParseVersion(AppVersion.Number), Tag = "v" + AppVersion.Number };
            string url = ReleasesUrl + "/expanded_assets/" + release.Tag;
            try
            {
                var page = read(url, cancel);
                if (page.Address.AbsoluteUri != url) throw new InvalidDataException("修复包页面地址已改变。");
                ParseAssetPage(release, page.Text);
                return release;
            }
            catch (WebException ex)
            {
                var status = Status(ex); if (ex.Response != null) ex.Response.Dispose();
                cancel.ThrowIfCancellationRequested();
                throw new IOException(status == HttpStatusCode.NotFound ? "当前版本 v" + AppVersion.Number + " 尚无可下载的修复包，请从发布页下载完整版本。" : "暂时无法下载修复包，请检查网络后重试。", ex);
            }
        }
        internal static UpdateRelease Check(CancellationToken cancel, Func<string, CancellationToken, UpdatePage> read)
        {
            // The public latest-release redirect does not consume the shared anonymous REST API quota.
            UpdatePage page;
            try { page = read(ReleasesUrl + "/latest", cancel); }
            catch (WebException ex)
            {
                if (Status(ex) == HttpStatusCode.NotFound) { if (ex.Response != null) ex.Response.Dispose(); return null; }
                throw;
            }
            cancel.ThrowIfCancellationRequested();
            var release = ParseReleasePage(page);
            if (release.Version <= ParseVersion(AppVersion.Number)) return release;
            try
            {
                var api = ParseRelease(read(ApiUrl, cancel).Text);
                // The release may change between requests. Keep the version shown by /latest as the authority.
                if (api != null && api.Tag == release.Tag) return api;
            }
            catch (WebException ex)
            {
                if (ex.Response != null) ex.Response.Dispose();
                cancel.ThrowIfCancellationRequested();
            }
            catch (InvalidDataException) { } // An absent API digest must still be verified using this asset's official page.
            cancel.ThrowIfCancellationRequested();
            string assetsUrl = ReleasesUrl + "/expanded_assets/" + release.Tag;
            var assets = read(assetsUrl, cancel);
            if (assets.Address.AbsoluteUri != assetsUrl) throw new InvalidDataException("附件页面地址已改变，请打开发布页查看。");
            ParseAssetPage(release, assets.Text);
            return release;
        }
        private static HttpStatusCode Status(WebException error)
        {
            var response = error.Response as HttpWebResponse;
            return response == null ? 0 : response.StatusCode;
        }
        internal static UpdatePage ReadPage(string address, CancellationToken cancel)
        {
            using (var buffer = new MemoryStream())
            {
                var final = Download(address, buffer, 2 * 1024 * 1024, cancel, null);
                return new UpdatePage { Address = final, Text = Encoding.UTF8.GetString(buffer.ToArray()) };
            }
        }
        internal static UpdateRelease ParseReleasePage(UpdatePage page)
        {
            string prefix = ReleasesUrl + "/tag/";
            if (page == null || page.Address == null || !page.Address.AbsoluteUri.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("没有取得本项目的正式版本信息，请打开发布页查看。");
            string tag = page.Address.AbsoluteUri.Substring(prefix.Length);
            var version = ParseVersion(tag);
            // Render notes as plain text only; never execute or follow links embedded in release notes.
            string notes = "请点击“打开发布页”查看完整更新说明。";
            var body = Regex.Match(page.Text ?? "", @"<div\b[^>]*\bdata-test-selector\s*=\s*[""']body-content[""'][^>]*>(?<body>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (body.Success)
            {
                string html = Regex.Replace(body.Groups["body"].Value, @"<(script|style)\b[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                html = Regex.Replace(html, @"</(?:p|li|h[1-6]|pre)>|<br\s*/?>", "\r\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                notes = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "", RegexOptions.None, TimeSpan.FromSeconds(1))).Trim();
            }
            return new UpdateRelease { Version = version, Tag = tag, Notes = notes };
        }
        internal static void ParseAssetPage(UpdateRelease release, string html)
        {
            string name = "AudioSwitch-v" + release.Version.ToString(3) + "-win-x64.zip";
            string expected = ReleasesUrl + "/download/" + release.Tag + "/" + name;
            var rows = Regex.Matches(html, @"<li\b[^>]*>(?<body>.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            var matches = new List<string>();
            foreach (Match row in rows)
            {
                var links = Regex.Matches(row.Groups["body"].Value, @"<a\b[^>]*\bhref\s*=\s*[""'](?<href>[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                foreach (Match link in links)
                {
                    Uri address;
                    if (Uri.TryCreate(new Uri(ReleasesUrl), WebUtility.HtmlDecode(link.Groups["href"].Value), out address) && address.AbsoluteUri == expected)
                        matches.Add(row.Groups["body"].Value);
                }
            }
            if (matches.Count != 1) throw new InvalidDataException("这个版本缺少唯一的 Windows x64 更新包。请打开发布页查看。");
            var hashes = Regex.Matches(matches[0], @"<clipboard-copy\b[^>]*\bvalue\s*=\s*[""']sha256:(?<hash>[a-fA-F0-9]{64})[""'][^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (hashes.Count != 1) throw new InvalidDataException("GitHub 附件页没有提供可验证的 SHA-256 信息，请打开发布页查看。");
            release.Url = expected; release.Digest = hashes[0].Groups["hash"].Value;
            // The website rounds sizes (e.g. "212 KB"). Use a strict byte limit and the full digest instead.
            release.Size = 0;
        }
        internal static bool AllowedDownload(Uri uri)
        {
            return uri.Scheme == "https" && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Host == "api.github.com" || uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
        }
        private static Uri Download(string address, Stream output, long limit, CancellationToken cancel, Action<long> progress)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var uri = new Uri(address);
            for (int redirect = 0; redirect < 6; redirect++)
            {
                cancel.ThrowIfCancellationRequested();
                if (!AllowedDownload(uri)) throw new InvalidDataException("下载被重定向到不受信任的地址。");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "AudioSwitch/" + AppVersion.Number;
                request.Accept = uri.Host == "api.github.com" ? "application/vnd.github+json" : "*/*";
                request.Timeout = 20000; request.ReadWriteTimeout = 20000; request.AllowAutoRedirect = false;
                using (cancel.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    { uri = new Uri(uri, response.Headers["Location"]); continue; }
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit) throw new InvalidDataException("下载响应不正确或文件过大。");
                    using (var input = response.GetResponseStream()) CopyBounded(input, output, limit, cancel, progress);
                    return response.ResponseUri;
                }
            }
            throw new InvalidDataException("下载重定向次数过多，请稍后重试。");
        }
        internal static long CopyBounded(Stream input, Stream output, long limit, CancellationToken cancel, Action<long> progress)
        {
            var buffer = new byte[32768]; long total = 0; int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                cancel.ThrowIfCancellationRequested(); total += count;
                if (total > limit) throw new InvalidDataException("文件大小超出限制。");
                output.Write(buffer, 0, count); if (progress != null) progress(total);
            }
            cancel.ThrowIfCancellationRequested(); return total;
        }
        internal static string Prepare(UpdateRelease release, CancellationToken cancel, Action<long> progress)
        {
            string work = Path.Combine(Program.DataDirectory, "updates", Guid.NewGuid().ToString("N"));
            return PrepareInDirectory(release, cancel, progress, work);
        }
        internal static string PrepareInDirectory(UpdateRelease release, CancellationToken cancel, Action<long> progress, string work)
        {
            using (UpdateStorage.CacheLease(Path.GetDirectoryName(work)))
                return PrepareLocked(release, cancel, progress, work);
        }
        private static string PrepareLocked(UpdateRelease release, CancellationToken cancel, Action<long> progress, string work)
        {
            cancel.ThrowIfCancellationRequested();
            UpdateInstaller.RejectLinks(work);
            if (File.Exists(Path.Combine(work, "install.json"))) throw new IOException("该缓存已交给安装助手，请重新检查更新。");
            string root = Path.GetDirectoryName(work);
            // A retry replaces only this attempt's fixed files. Count all older cache towards the cap.
            long replaceable = 0;
            foreach (string name in Files.Select(name => Path.Combine("payload", name)).Concat(new[] { "package.zip" }))
            {
                string path = Path.Combine(work, name); UpdateInstaller.RejectLinks(path);
                if (File.Exists(path)) replaceable += new FileInfo(path).Length;
            }
            long helperSize = new FileInfo(typeof(AppUpdate).Assembly.Location).Length;
            long reserve = (release.Size > 0 ? release.Size : MaxPackage) + MaxPackage * 2 + helperSize + 256 * 1024;
            UpdateStorage.Require(root, UpdateStorage.CacheLimit, Math.Max(0, reserve - replaceable), "更新缓存");
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "package.zip");
            // A completed verified ZIP is reusable after cancellation during extraction.
            if (!File.Exists(zip) || (release.Size > 0 && new FileInfo(zip).Length != release.Size) || !String.Equals(Hash(zip), release.Digest, StringComparison.OrdinalIgnoreCase))
                using (var output = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None))
                    Download(release.Url, output, release.Size > 0 ? release.Size : MaxPackage, cancel, progress);
            if ((release.Size > 0 && new FileInfo(zip).Length != release.Size) || !String.Equals(Hash(zip), release.Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包不完整或校验失败。原程序没有更改，请重新下载。");
            string payload = Path.Combine(work, "payload");
            Extract(zip, payload, cancel, true);
            ValidatePayload(payload, release.Version);
            return payload;
        }
        internal static string Hash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static void Extract(string zip, string destination, CancellationToken cancel, bool replaceAttempt = false)
        {
            using (var archive = ZipFile.OpenRead(zip))
            {
                // Only the fixed distribution files are allowed; no arbitrary paths, settings, or DLLs.
                if (archive.Entries.Count != Files.Length || Files.Any(name => archive.Entries.Count(e => e.FullName == name) != 1))
                    throw new InvalidDataException("更新包文件结构不正确，请使用此项目的 Windows x64 正式发布包。");
                long total = 0;
                foreach (var name in Files)
                {
                    var entry = archive.GetEntry(name); total += entry.Length;
                    if (entry.Length <= 0 || entry.Length > MaxPackage * 2 || total > MaxPackage * 2) throw new InvalidDataException("解压后的更新文件大小无效。");
                    string path = Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar));
                    UpdateInstaller.RejectLinks(path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open()) using (var output = new FileStream(path, replaceAttempt ? FileMode.Create : FileMode.CreateNew))
                        if (CopyBounded(input, output, entry.Length, cancel, null) != entry.Length) throw new InvalidDataException("更新文件不完整。");
                }
            }
        }
        internal static void ValidatePayload(string directory, Version version)
        {
            foreach (var name in Files) if (!File.Exists(Path.Combine(directory, name))) throw new InvalidDataException("更新文件缺失：" + name);
            var assembly = AssemblyName.GetAssemblyName(Path.Combine(directory, "AudioSwitch.exe"));
            if (assembly.Name != "AudioSwitch" || assembly.ProcessorArchitecture != ProcessorArchitecture.Amd64 || assembly.Version.ToString(3) != version.ToString(3))
                throw new InvalidDataException("程序版本或平台与发布信息不一致。");
            if (Hash(Path.Combine(directory, "vendor/svcl/svcl.exe")) != SpatialAudio.HelperHash.ToLowerInvariant())
                throw new InvalidDataException("空间音效组件校验失败，不能安装此更新包。");
        }
    }
}
