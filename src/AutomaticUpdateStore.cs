// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AudioSwitch
{
    public sealed class PendingUpdate
    {
        public int Format { get; set; }
        public string Target { get; set; }
        public string Version { get; set; }
        public string Tag { get; set; }
        public string Digest { get; set; }
        public string WorkId { get; set; }
        public Dictionary<string, string> Hashes { get; set; }
        public bool Attempted { get; set; }
        public string Failure { get; set; }
    }
    internal sealed class AutomaticUpdateStore
    {
        private readonly string dataRoot, target;
        internal readonly string PathName;
        private string downloadWork;
        internal AutomaticUpdateStore(string dataRoot, string target)
        {
            this.dataRoot = Path.GetFullPath(dataRoot); this.target = Path.GetFullPath(target);
            string key;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(this.target.ToUpperInvariant()))).Replace("-", "");
            PathName = Path.Combine(this.dataRoot, "updates", "automatic", key, "pending.json");
        }
        internal PendingUpdate Read()
        {
            if (!File.Exists(PathName)) return null;
            UpdateInstaller.RejectLinks(PathName);
            if (new FileInfo(PathName).Length > 65536) throw new InvalidDataException("待安装更新记录过大。");
            var item = Wire.Decode<PendingUpdate>(File.ReadAllText(PathName, Encoding.UTF8));
            if (item == null || item.Format != 1 || item.Target == null || !UpdateInstaller.SamePath(item.Target, target) ||
                item.Tag != "v" + AppUpdate.ParseVersion(item.Version).ToString(3) ||
                item.Digest == null || !Regex.IsMatch(item.Digest, "\\A[a-fA-F0-9]{64}\\z") ||
                item.WorkId == null || !Regex.IsMatch(item.WorkId, "\\A[a-fA-F0-9]{32}\\z") ||
                item.Hashes == null || item.Hashes.Count != AppUpdate.Files.Length ||
                AppUpdate.Files.Any(name => !item.Hashes.ContainsKey(name) || item.Hashes[name] == null || !Regex.IsMatch(item.Hashes[name], "\\A[a-fA-F0-9]{64}\\z")))
                throw new InvalidDataException("待安装更新记录无效，请在面板中重新检查更新。");
            return item;
        }
        internal string Payload(PendingUpdate item) { return Path.Combine(dataRoot, "updates", item.WorkId, "payload"); }
        internal string Prepare(UpdateRelease release, CancellationToken cancel)
        {
            if (downloadWork == null) downloadWork = Path.Combine(dataRoot, "updates", Guid.NewGuid().ToString("N"));
            return AppUpdate.PrepareInDirectory(release, cancel, null, downloadWork);
        }
        internal void Validate(PendingUpdate item)
        {
            string payload = Payload(item), zip = Path.Combine(Path.GetDirectoryName(payload), "package.zip");
            UpdateInstaller.RejectLinks(zip); UpdateInstaller.RejectLinks(payload);
            if (new FileInfo(zip).Length > AppUpdate.MaxPackage || !String.Equals(AppUpdate.Hash(zip), item.Digest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("已下载的更新包发生变化，请在面板中重新下载。");
            AppUpdate.ValidatePayload(payload, AppUpdate.ParseVersion(item.Version));
            foreach (string name in AppUpdate.Files)
            {
                string path = Path.Combine(payload, name); UpdateInstaller.RejectLinks(path);
                if (new FileInfo(path).Length > AppUpdate.MaxPackage * 2 || !String.Equals(AppUpdate.Hash(path), item.Hashes[name], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("待安装更新文件校验失败：" + name);
            }
        }
        internal void Stage(UpdateRelease release, string payload)
        {
            if (release.Version <= AppUpdate.ParseVersion(AppVersion.Number)) throw new InvalidDataException("不能缓存相同或旧版本。");
            string id = new DirectoryInfo(Path.GetDirectoryName(payload)).Name;
            var item = new PendingUpdate { Format = 1, Target = target, Version = release.Version.ToString(3), Tag = release.Tag,
                Digest = release.Digest, WorkId = id, Hashes = AppUpdate.Files.ToDictionary(name => name, name => AppUpdate.Hash(Path.Combine(payload, name))) };
            if (!Regex.IsMatch(id, "\\A[a-fA-F0-9]{32}\\z") || !UpdateInstaller.SamePath(Payload(item), payload)) throw new InvalidDataException("更新缓存目录不正确。");
            Validate(item); Save(item);
        }
        internal void Save(PendingUpdate item)
        {
            using (UpdateStorage.CacheLease(Path.Combine(dataRoot, "updates"))) SaveLocked(item);
        }
        private void SaveLocked(PendingUpdate item)
        {
            UpdateStorage.Require(Path.Combine(dataRoot, "updates"), UpdateStorage.CacheLimit, 128 * 1024, "更新缓存");
            UpdateInstaller.RejectLinks(PathName);
            Directory.CreateDirectory(Path.GetDirectoryName(PathName));
            string temporary = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, Wire.Encode(item), new UTF8Encoding(false));
            if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".previous-" + Guid.NewGuid().ToString("N"));
            else File.Move(temporary, PathName);
        }
        internal void Fail(string message)
        {
            var item = Read(); if (item == null) return;
            item.Attempted = true; item.Failure = message.Length > 2000 ? message.Substring(0, 2000) : message; Save(item);
        }
        internal bool TryInstall(bool background, Action<string, UpdateRelease, bool> launch, out string issue, bool suspended = false)
        {
            issue = null;
            if (suspended) return false;
            try
            {
                var item = Read();
                if (item == null || AppUpdate.ParseVersion(item.Version) <= AppUpdate.ParseVersion(AppVersion.Number)) return false;
                if (item.Attempted) { issue = item.Failure ?? "上次自动更新未完成，请在面板中手动重试。"; return false; }
                Validate(item);
                // Persist before handing off: a failed installation must not repeat at every login.
                item.Attempted = true; Save(item);
                launch(Payload(item), new UpdateRelease { Version = AppUpdate.ParseVersion(item.Version), Tag = item.Tag, Digest = item.Digest }, background);
                return true;
            }
            catch (Exception ex)
            {
                issue = "启动更新未完成：" + ex.Message;
                try { Fail(issue); } catch { }
                return false;
            }
        }
    }
}
