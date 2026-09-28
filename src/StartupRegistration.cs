// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using Microsoft.Win32;

namespace AudioSwitch
{
    public sealed class StartupState
    {
        public bool Enabled { get; set; }
        public bool Available { get; set; }
        public string Message { get; set; }
        public string Details { get; set; }
        public string RegisteredCommand { get; set; }
        public string CurrentExecutablePath { get; set; }
        public bool NeedsRepair { get; set; }
    }
    internal interface IStartupStore
    {
        string Read();
        void Write(string command);
    }
    internal sealed class RegistryStartupStore : IStartupStore
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AudioSwitch";
        public string Read()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, false))
            {
                if (key == null) return null;
                object value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null) return null;
                if (key.GetValueKind(ValueName) != RegistryValueKind.String || !(value is string))
                    throw new IOException("已有同名启动项的格式无法识别，请在 Windows 启动应用中检查。");
                return (string)value;
            }
        }
        public void Write(string command)
        {
            if (command == null)
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, true))
                    if (key != null) key.DeleteValue(ValueName, false);
            }
            else using (var key = Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue(ValueName, command, RegistryValueKind.String);
        }
    }
    internal static class StartupRegistration
    {
        internal static string Command(string executable)
        {
            if (String.IsNullOrEmpty(executable) || !Path.IsPathRooted(executable) || executable.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new IOException("程序路径无效，无法设置开机自启。");
            if (!String.Equals(Path.GetFileName(executable), "AudioSwitch.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("程序文件需要命名为 AudioSwitch.exe，请恢复文件名后再设置自启。");
            string command = "\"" + Path.GetFullPath(executable) + "\" --background";
            if (command.Length > 260) throw new IOException("程序路径太长，请移到较短的目录后再开启自启。");
            return command;
        }
        private static string RegisteredPath(string command)
        {
            if (command == null) return null;
            const string suffix = "\" --background";
            if (command.Length <= suffix.Length + 1 || !command.StartsWith("\"", StringComparison.Ordinal) || !command.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
            string path = command.Substring(1, command.Length - suffix.Length - 1);
            try { return String.Equals(Path.GetFileName(path), "AudioSwitch.exe", StringComparison.OrdinalIgnoreCase) && String.Equals(Command(path), command, StringComparison.OrdinalIgnoreCase) ? path : null; }
            catch { return null; }
        }
        private static bool Owned(string command) { return command == null || RegisteredPath(command) != null; }
        internal static StartupState Read(string executable, IStartupStore store, Func<string, bool> exists = null)
        {
            var state = new StartupState { CurrentExecutablePath = executable };
            try
            {
                exists = exists ?? File.Exists;
                string expected = Command(executable), actual = store.Read();
                state.RegisteredCommand = actual;
                if (!Owned(actual)) { state.Message = "已有同名启动项无法识别，点击查看"; state.Details = "已有同名启动项无法识别，未修改。请检查 Windows 启动应用。"; return state; }
                if (!exists(executable)) { state.Message = "当前程序路径失效，点击查看"; state.Details = "当前运行的程序文件已不可访问，可能已被移动或删除。请从托盘退出声间，再从新位置重新打开，然后开启自启。\n\n当前运行路径：\n" + executable; return state; }
                state.Enabled = String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                state.Available = true; state.NeedsRepair = actual != null && !state.Enabled;
                state.Message = state.NeedsRepair ? (exists(RegisteredPath(actual)) ? "自启指向其他位置，点击查看" : "原自启路径已失效，点击查看") : "登录 Windows 后只启动托盘";
                state.Details = state.NeedsRepair ? "自启尚未指向当前程序。开启“开机自启”可改为当前位置，检测本身不会修改启动项。\n\n原登记路径：\n" + RegisteredPath(actual) + "\n\n当前程序路径：\n" + executable : "登录 Windows 后只启动托盘。开关表示已登记本程序的启动项；若 Windows 启动应用被禁用，还需在系统设置中允许启动。\n\n当前程序路径：\n" + executable;
                return state;
            }
            catch (Exception ex) { state.Available = false; state.Enabled = false; state.Message = "无法读取自启设置，点击查看"; state.Details = "无法读取自启设置：" + ex.Message; return state; }
        }
        internal static void Set(bool enabled, string executable, IStartupStore store, Func<string, bool> exists = null, bool checkExpected = false, string expectedCommand = null)
        {
            exists = exists ?? File.Exists;
            string expected = Command(executable), before = store.Read();
            if (enabled && !exists(executable)) throw new IOException("当前程序路径已不可访问。请从托盘退出声间，从新位置重新打开后再开启自启。");
            if (checkExpected && !String.Equals(before, expectedCommand, StringComparison.Ordinal)) throw new IOException("自启设置已被其他窗口或程序更改，请查看刷新后的状态再操作。");
            if (!Owned(before)) throw new IOException("已有同名启动项无法识别，未修改。请先在 Windows 启动应用中检查。");
            if (!enabled && before != null && !String.Equals(before, expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("自启指向另一位置的声间，未修改该启动项。");
            string desired = enabled ? expected : null;
            if (String.Equals(before, desired, StringComparison.Ordinal)) return;
            try
            {
                store.Write(desired);
                if (!String.Equals(store.Read(), desired, StringComparison.Ordinal)) throw new IOException("Windows 未保存自启设置。");
            }
            catch (Exception ex)
            {
                try
                {
                    string actual = null; bool known = false;
                    try { actual = store.Read(); known = true; } catch { }
                    if (known && !String.Equals(actual, before, StringComparison.Ordinal) && !String.Equals(actual, desired, StringComparison.Ordinal))
                        throw new IOException("启动项已被其他程序改变，未覆盖该项。");
                    // A read failure after a write must not skip restoring the value we already saved.
                    if (!known || !String.Equals(actual, before, StringComparison.Ordinal)) store.Write(before);
                    if (!String.Equals(store.Read(), before, StringComparison.Ordinal)) throw new IOException("恢复后读回结果不一致。");
                }
                catch (Exception recovery) { throw new IOException("自启设置失败，原启动项也未能确认恢复。请检查 Windows 启动应用。" + recovery.Message, ex); }
                throw new IOException("自启设置失败，已确认保留原启动项。" + ex.Message, ex);
            }
        }
    }
}
