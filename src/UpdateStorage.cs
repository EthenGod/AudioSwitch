// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    internal sealed class UpdateStorageException : IOException
    { internal UpdateStorageException(string message) : base(message) { } }
    internal static class UpdateStorage
    {
        internal const long CacheLimit = 256L * 1024 * 1024, BackupLimit = 128L * 1024 * 1024;
        internal static IDisposable CacheLease(string root)
        {
            string path = Path.Combine(root, "cache.lock"); UpdateInstaller.RejectLinks(path);
            Directory.CreateDirectory(root);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        internal static long Measure(string root)
        {
            UpdateInstaller.RejectLinks(root);
            if (!Directory.Exists(root)) return 0;
            long size = 0; int entries = 0;
            var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0)
                foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    if (++entries > 20000) throw new UpdateStorageException("更新目录中的文件过多，请手动整理：" + root);
                    UpdateInstaller.RejectLinks(path);
                    if (Directory.Exists(path)) pending.Push(path);
                    else size = checked(size + new FileInfo(path).Length);
                }
            return size;
        }
        internal static void Require(string root, long limit, long additional, string label)
        {
            long used = Measure(root);
            if (additional < 0 || used > limit || additional > limit - used)
                throw new UpdateStorageException(label + "空间已达预留上限（" + (limit / 1024 / 1024) + " MiB），更新操作已暂停。请手动整理后重试：" + root);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)));
            if (drive.AvailableFreeSpace < additional + 64L * 1024 * 1024)
                throw new UpdateStorageException("磁盘剩余空间不足，更新已暂停，请腾出空间后重试。");
        }
        internal static void Backup(string payload, string directory, string[] files)
        {
            long reserve = 4096;
            foreach (string name in files)
            {
                string source = Path.Combine(payload, name), old = Path.Combine(directory, name);
                UpdateInstaller.RejectLinks(source); UpdateInstaller.RejectLinks(old);
                if (File.Exists(old)) reserve = checked(reserve + new FileInfo(old).Length);
                // Leave room to retain a newly introduced file if rollback cannot remove it.
                if (File.Exists(source)) reserve = checked(reserve + new FileInfo(source).Length);
            }
            Require(Path.Combine(directory, "update-backups"), BackupLimit, reserve, "旧版备份");
        }
    }
}
