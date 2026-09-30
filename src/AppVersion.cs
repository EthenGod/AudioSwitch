// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System.Reflection;

[assembly: AssemblyTitle("AudioSwitch")]
[assembly: AssemblyVersion(AudioSwitch.AppVersion.Number)]
[assembly: AssemblyFileVersion(AudioSwitch.AppVersion.Number)]

namespace AudioSwitch
{
    internal static class AppVersion
    {
        // Release tags and package names use v + Number. Keep three numeric components.
        internal const string Number = "0.11.1";
    }
}
