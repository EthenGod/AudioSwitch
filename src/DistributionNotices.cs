// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.IO;
using System.Linq;

namespace AudioSwitch
{
    internal static class DistributionNotices
    {
        internal static string Read()
        {
            return String.Join("\r\n\r\n", new[] { "NOTICE.txt", "THIRD_PARTY.md", "vendor/svcl/readme.txt", "LICENSE" }.Select(name => {
                using (var stream = typeof(DistributionNotices).Assembly.GetManifestResourceStream("AudioSwitch.Distribution." + name))
                {
                    if (stream == null) throw new InvalidDataException("内置许可说明缺失：" + name);
                    using (var reader = new StreamReader(stream)) return name + "\r\n\r\n" + reader.ReadToEnd();
                }
            }));
        }
    }
}
