using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SwiftControl
{
    internal static class WindowsPowerMode
    {
        private static readonly Guid BestEfficiency = new Guid(
            "961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid Balanced = Guid.Empty;
        private static readonly Guid BestPerformance = new Guid(
            "ded574b5-45a0-4f42-8737-46345c09c238");

        public static int Read(bool onAcPower)
        {
            Guid mode;
            uint result = onAcPower
                ? PowerGetUserConfiguredACPowerMode(out mode)
                : PowerGetUserConfiguredDCPowerMode(out mode);
            if (result != 0) throw new Win32Exception((int)result);

            if (mode == BestEfficiency) return 0;
            if (mode == Balanced) return 1;
            if (mode == BestPerformance) return 2;
            throw new InvalidOperationException(
                "Windows reported an unsupported power-mode identifier: " + mode);
        }

        public static bool Set(bool onAcPower, int mode)
        {
            Guid value = GuidFor(mode);
            uint result = onAcPower
                ? PowerSetUserConfiguredACPowerMode(ref value)
                : PowerSetUserConfiguredDCPowerMode(ref value);
            if (result != 0) throw new Win32Exception((int)result);
            return Read(onAcPower) == mode;
        }

        public static string Name(int mode)
        {
            if (mode == 0) return "Best efficiency";
            if (mode == 2) return "Best performance";
            return "Balanced";
        }

        private static Guid GuidFor(int mode)
        {
            if (mode == 0) return BestEfficiency;
            if (mode == 1) return Balanced;
            if (mode == 2) return BestPerformance;
            throw new ArgumentOutOfRangeException("mode");
        }

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerGetUserConfiguredACPowerMode(out Guid powerModeGuid);

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid powerModeGuid);

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid powerModeGuid);

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid powerModeGuid);
    }
}
