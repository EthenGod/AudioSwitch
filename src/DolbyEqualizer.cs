using System;
using System.Linq;

namespace AudioSwitch
{
    internal static class DolbyEqualizer
    {
        // Dolby Access 3.27.12250.0: sampled controls use these DAP positions.
        // Labels are the Access UI frequencies, not the DAP interpolation positions.
        internal static readonly string[] Labels = { "32 Hz", "64 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz" };
        private static readonly int[] Anchors = { 0, 1, 2, 3, 4, 7, 10, 13, 16, 18 };
        private static readonly int[] Positions = { 47, 141, 234, 328, 469, 656, 844, 1031, 1313, 1688, 2250, 3000, 3750, 4688, 5813, 7125, 9000, 11250, 13875, 19688 };

        internal static decimal[] ToTen(int[] raw)
        {
            if (raw == null || raw.Length != 20 || raw.Any(n => n < -192 || n > 192)) throw new ArgumentException("均衡器需要 20 个 -192 到 192 的整数。");
            return Anchors.Select(i => raw[i] / 16m).ToArray();
        }

        internal static int[] ToTwenty(decimal[] db)
        {
            if (db == null || db.Length != 10 || db.Any(n => n < -12 || n > 12)) throw new ArgumentException("均衡器需要 10 个 -12 到 12 dB 的数值。");
            var gains = db.Select(n => (int)Math.Round(n * 16m, MidpointRounding.AwayFromZero)).ToArray();
            var result = new int[20];
            for (int i = 0; i < result.Length; i++)
            {
                int segment = 0;
                while (segment < 8 && i > Anchors[segment + 1]) segment++;
                int left = Anchors[segment], right = Anchors[segment + 1];
                // Access extends the final segment to the last DAP band, then clamps it.
                decimal value = gains[segment] + (gains[segment + 1] - gains[segment]) * (decimal)(Positions[i] - Positions[left]) / (Positions[right] - Positions[left]);
                result[i] = (int)Math.Max(-192m, Math.Min(192m, Math.Round(value, MidpointRounding.AwayFromZero)));
            }
            return result;
        }
    }
}
