// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

namespace AudioSwitch
{
    internal static class SpatialAudio
    {
        private static readonly Type Configuration = Type.GetType("Windows.Media.Audio.SpatialAudioDeviceConfiguration, Windows.Media, ContentType=WindowsRuntime");
        private static readonly Type Subtypes = Type.GetType("Windows.Media.Audio.SpatialAudioFormatSubtype, Windows.Media, ContentType=WindowsRuntime");
        private const string HelperHash = "0C4738296D253495BC995DA7B5938E27B306C8F204DC9F7D4D5D455F1D8D3E38";
        internal static SpatialState Read(string id)
        {
            if (Configuration == null || Subtypes == null) throw new InvalidOperationException("当前 Windows 版本不能读取空间音效设置。");
            // The WinRT API expects the audio render device-interface path, not the MMDevice endpoint ID.
            string deviceInterface = @"\\?\SWD#MMDEVAPI#" + id + @"#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
            object instance = null;
            try
            {
                instance = Configuration.GetMethod("GetForDeviceId").Invoke(null, new object[] { deviceInterface });
                var result = new SpatialState {
                    Supported = (bool)Configuration.GetProperty("IsSpatialAudioSupported").GetValue(instance, null),
                    CurrentFormat = (string)Configuration.GetProperty("DefaultSpatialAudioFormat").GetValue(instance, null),
                    Options = new List<SpatialOption> { new SpatialOption { Id = "", Name = "关闭空间音效" } }
                };
                foreach (var property in Subtypes.GetProperties(BindingFlags.Public | BindingFlags.Static))
                {
                    string format = (string)property.GetValue(null, null);
                    if (result.Supported && (bool)Configuration.GetMethod("IsSpatialAudioFormatSupported").Invoke(instance, new object[] { format }))
                        result.Options.Add(new SpatialOption { Id = format, Name = FriendlyName(property.Name) });
                }
                return result;
            }
            catch (TargetInvocationException ex) { throw new InvalidOperationException("读取空间音效失败：" + (ex.InnerException ?? ex).Message); }
            // WinRT projected objects are managed by the CLR; do not ReleaseComObject them.
        }
        private static string FriendlyName(string name)
        {
            switch (name)
            {
                case "WindowsSonic": return "Windows Sonic（耳机）";
                case "DolbyAtmosForHeadphones": return "Dolby Atmos（耳机）";
                case "DolbyAtmosForHomeTheater": return "Dolby Atmos（家庭影院）";
                case "DolbyAtmosForSpeakers": return "Dolby Atmos（扬声器）";
                case "DTSHeadphoneX": return "DTS Headphone:X";
                case "DTSXUltra": return "DTS:X Ultra";
                case "DTSXForHomeTheater": return "DTS:X（家庭影院）";
                default: return name;
            }
        }
        internal static string QuoteArgument(string value)
        {
            // Standard Windows argv quoting, including empty strings and trailing slashes.
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
                else { result.Append('\\', slashes); result.Append(c); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }
        internal static void Set(string id, string format)
        {
            if (DeviceProfiles.SameFormat(format, "")) format = "";
            Guid parsed;
            if (!String.IsNullOrEmpty(format) && !Guid.TryParse(format, out parsed)) throw new InvalidOperationException("空间音效格式无效。");
            string helper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vendor", "svcl", "svcl.exe");
            if (!File.Exists(helper)) throw new InvalidOperationException("缺少空间音效组件，请保留程序旁的 vendor 文件夹。");
            using (var stream = File.OpenRead(helper))
            using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "") != HelperHash)
                    throw new InvalidOperationException("空间音效组件校验失败，请重新构建或还原组件。");
            var start = new ProcessStartInfo(helper, "/SetSpatial " + QuoteArgument(id) + " " + QuoteArgument(format)) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var process = Process.Start(start))
            {
                if (!process.WaitForExit(2200)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("空间音效设置超时。"); }
                if (process.ExitCode != 0) throw new InvalidOperationException("空间音效组件未能应用设置。");
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (DeviceProfiles.SameFormat(Read(id).CurrentFormat, format)) return;
                if (attempt < 3) Thread.Sleep(60);
            }
            throw new InvalidOperationException("Windows 未采用该空间音效。请确认设备支持，且相关音效已安装并启用。");
        }
    }
}
