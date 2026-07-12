namespace PredatorControlApp
{
    public enum FanKind
    {
        Cpu,
        Gpu
    }

    /// <summary>
    /// Measured Custom-mode RPM bands for this machine.
    /// Acer EC accepts 0–100% over WMI but maps duty to ~10% steps in firmware.
    /// </summary>
    public static class FanRpmMap
    {
        public const int EcStepPercent = 10;

        // Mid-band RPM from Custom-mode measurements (percent decade → RPM).
        private static readonly int[] CpuBandRpm =
        [
            2295, // 0–9%
            2884, // 10–19%
            3400, // 20–29%
            3950, // 30–39%
            4400, // 40–49%
            4920, // 50–59%
            5360, // 60–69%
            5770, // 70–79%
            6120, // 80–89%
            6520, // 90–99%
            7060  // 100%
        ];

        private static readonly int[] GpuBandRpm =
        [
            2050, // 0–9%
            2560, // 10–19%
            3100, // 20–29%
            3550, // 30–39%
            4000, // 40–49%
            4450, // 50–59%
            4900, // 60–69%
            5200, // 70–79%
            5660, // 80–89%
            6000, // 90–99%
            6521  // 100%
        ];

        /// <summary>Map a requested % to the EC duty band (floor to 10%, keep 100).</summary>
        public static byte QuantizePercent(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (percent >= 100) return 100;
            return (byte)(percent / EcStepPercent * EcStepPercent);
        }

        /// <summary>Snap UI/curve edits to the nearest EC step (0, 10, …, 100).</summary>
        public static int SnapPercent(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            if (percent >= 100) return 100;
            int snapped = (int)Math.Round(percent / (double)EcStepPercent) * EcStepPercent;
            return Math.Clamp(snapped, 0, 100);
        }

        public static int EstimateRpm(FanKind kind, int fanPercent)
        {
            fanPercent = Math.Clamp(fanPercent, 0, 100);
            var table = kind == FanKind.Cpu ? CpuBandRpm : GpuBandRpm;
            int index = fanPercent >= 100 ? table.Length - 1 : fanPercent / EcStepPercent;
            return table[Math.Clamp(index, 0, table.Length - 1)];
        }

        public static int StepToward(int currentQuantized, int targetQuantized)
        {
            currentQuantized = QuantizePercent(currentQuantized);
            targetQuantized = QuantizePercent(targetQuantized);
            if (currentQuantized == targetQuantized) return targetQuantized;
            if (currentQuantized < targetQuantized)
                return Math.Min(targetQuantized, currentQuantized + EcStepPercent);
            return Math.Max(targetQuantized, currentQuantized - EcStepPercent);
        }
    }
}
