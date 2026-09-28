// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.IO;

namespace AudioSwitch
{
    internal static class InstanceLocation
    {
        internal static void RequireSame(string current, string backend)
        {
            if (String.IsNullOrEmpty(backend)) throw new IOException("无法确认后台程序位置。请从托盘退出声间，再打开所需位置的程序。");
            if (!UpdateInstaller.SamePath(current, backend))
                throw new IOException("另一位置的声间仍在运行。请先从托盘退出它，再打开当前位置的程序，然后检查开机自启。\n\n正在运行：\n" + backend + "\n\n本次打开：\n" + current);
        }
        internal static void RequireBackend(string current, Reply reply)
        {
            string path = reply.BackendExecutablePath;
            if (String.IsNullOrEmpty(path))
            {
                // Older backends report a PID but no path. Do not silently connect a relocated UI to them.
                using (var process = Process.GetProcessById(reply.BackendPid)) path = process.MainModule.FileName;
            }
            RequireSame(current, path);
        }
    }
}
