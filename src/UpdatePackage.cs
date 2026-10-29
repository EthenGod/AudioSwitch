// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;

namespace AudioSwitch
{
    internal static class UpdatePackage
    {
        internal const int Legacy = 0, Desktop = 1;
        internal static readonly string[] DesktopContent = PanelLaunch.Files.Take(12).Concat(new[] { "DESKTOP-README.txt" }).ToArray();
        internal static readonly string[] DesktopFiles = DesktopContent.Concat(new[] { "desktop-manifest.json" }).ToArray();
        internal static string[] Files(int format)
        {
            if (format != Legacy && format != Desktop) throw new InvalidDataException("不支持的更新包格式。");
            return format == Desktop ? DesktopFiles : AppUpdate.Files;
        }
        internal static bool Exists(string directory, string name) { string path = Path.Combine(directory, name); return File.Exists(path) || Directory.Exists(path); }
        internal static bool IsDesktop(string directory) { return Exists(directory, "desktop-manifest.json") || Exists(directory, "DESKTOP-README.txt"); }
        internal static string AssetName(Version version, int format) { Files(format); return "AudioSwitch-" + (format == Desktop ? "Desktop-" : "") + "v" + version.ToString(3) + "-win-x64.zip"; }
        internal static void RequireTarget(string directory, int format)
        {
            Files(format);
            if (PanelLaunch.IsCandidate(directory)) throw new InvalidDataException(PanelLaunch.LegacyUpdateBlocked);
            if (format == Legacy && IsDesktop(directory)) throw new InvalidDataException("新界面目录必须使用完整桌面包，不能安装旧格式包或局部修复。");
        }
        internal static void RequirePayload(string directory, int format)
        {
            Files(format);
            // A cancelled Desktop extraction may not have written its final manifest yet.
            // The expected type comes from the selected asset, never from the partial directory.
            if (format == Legacy) RequireTarget(directory, format);
            else if (Exists(directory, "candidate-manifest.json") || Exists(directory, "CANDIDATE-README.txt"))
                throw new InvalidDataException(PanelLaunch.LegacyUpdateBlocked);
        }
        internal static bool CanInstall(Version version, int format, string directory)
        {
            Files(format);
            var current = AppUpdate.ParseVersion(AppVersion.Number);
            return version > current || (version == current && format == Desktop && !PanelLaunch.IsModern(directory));
        }
    }
}
