using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace SwiftControl
{
    internal sealed class PowerModeOption
    {
        public int Value { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class DashboardSnapshot
    {
        public int BatteryPercent { get; set; }
        public bool OnAcPower { get; set; }
        public bool OptimizedCharging { get; set; }
        public int CurrentPowerMode { get; set; }
        public List<PowerModeOption> PowerModes { get; set; }
    }

    internal sealed class PowerSourceSnapshot
    {
        public bool OnAcPower { get; set; }
        public int BatteryPercent { get; set; }
    }

    internal static class DashboardReader
    {
        private const string CareQuestion = "AcerCareCenterFunctionalityTests_HandshakingQuestion";
        private const string QuickQuestion = "AcerQuickAccessFunctionalityTests_HandshakingQuestion";

        public static DashboardSnapshot Read()
        {
            DashboardSnapshot snapshot = new DashboardSnapshot();
            snapshot.PowerModes = new List<PowerModeOption>();

            using (AcerServiceClient care = new AcerServiceClient(4343, CareQuestion))
            {
                care.Connect();
                Dictionary<string, object> limit = AcerJson.Result(care.Get("BatteryHealthy"));
                Dictionary<string, object> status = AcerJson.Result(care.Get("BatteryStatus"));

                snapshot.OptimizedCharging = OptimizedChargingValue(limit);
                snapshot.BatteryPercent = AcerJson.Int(status, "BatteryPercent", 0);
                if (snapshot.BatteryPercent < 0 || snapshot.BatteryPercent > 100)
                    throw new InvalidOperationException("Acer reported an invalid battery percentage.");
                snapshot.OnAcPower = AcerJson.Bool(status, "ACMode", false);
            }

            using (AcerServiceClient quick = new AcerServiceClient(5141, QuickQuestion))
            {
                quick.Connect();
                Dictionary<string, object> current = AcerJson.Result(quick.Get("SystemUsageControl"));
                Dictionary<string, object> modesPacket = quick.Get("SystemUsageModes");

                snapshot.CurrentPowerMode = PowerModeValue(current);

                object modesValue;
                if (modesPacket.TryGetValue("Result", out modesValue))
                {
                    foreach (object item in AcerJson.Dictionaries(modesValue))
                    {
                        Dictionary<string, object> mode = item as Dictionary<string, object>;
                        if (mode == null) continue;
                        string name = AcerJson.Text(mode, "Text", "Mode");
                        name = name.Replace(" mode", "");
                        snapshot.PowerModes.Add(new PowerModeOption
                        {
                            Value = AcerJson.Int(mode, "Value", 1),
                            Name = name
                        });
                    }
                }
                if (snapshot.PowerModes.Count == 0)
                    throw new InvalidOperationException("Acer did not report any available power modes.");
            }

            return snapshot;
        }

        public static bool SetOptimizedCharging(bool enabled)
        {
            using (AcerServiceClient care = new AcerServiceClient(4343, CareQuestion))
            {
                care.Connect();
                care.Set("BatteryHealthy", enabled ? 0 : 1);
            }

            using (AcerServiceClient verify = new AcerServiceClient(4343, CareQuestion))
            {
                verify.Connect();
                Dictionary<string, object> result = AcerJson.Result(verify.Get("BatteryHealthy"));
                return OptimizedChargingValue(result) == enabled;
            }
        }

        public static bool ReadOptimizedCharging()
        {
            using (AcerServiceClient care = new AcerServiceClient(4343, CareQuestion))
            {
                care.Connect();
                Dictionary<string, object> result = AcerJson.Result(care.Get("BatteryHealthy"));
                return OptimizedChargingValue(result);
            }
        }

        public static bool SetPowerMode(int value)
        {
            using (AcerServiceClient quick = new AcerServiceClient(5141, QuickQuestion))
            {
                quick.Connect();
                quick.Set("SystemUsageControl", value);
            }

            using (AcerServiceClient verify = new AcerServiceClient(5141, QuickQuestion))
            {
                // Avoid accepting the service's immediate Set echo as proof.
                // AcerSense also follows a Set with a fresh Get; perform two
                // delayed Gets so the firmware-backed state must remain stable.
                Thread.Sleep(400);
                verify.Connect();
                Dictionary<string, object> result = AcerJson.Result(verify.Get("SystemUsageControl"));
                if (PowerModeValue(result) != value) return false;
                Thread.Sleep(700);
                Dictionary<string, object> stable = AcerJson.Result(verify.Get("SystemUsageControl"));
                return PowerModeValue(stable) == value;
            }
        }

        public static int ReadPowerMode()
        {
            using (AcerServiceClient quick = new AcerServiceClient(5141, QuickQuestion))
            {
                quick.Connect();
                Dictionary<string, object> result = AcerJson.Result(
                    quick.Get("SystemUsageControl"));
                return PowerModeValue(result);
            }
        }

        public static PowerSourceSnapshot ReadPowerSource()
        {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (status.ACLineStatus == 255)
                throw new InvalidOperationException("Windows could not determine the power source.");

            return new PowerSourceSnapshot
            {
                OnAcPower = status.ACLineStatus == 1,
                BatteryPercent = status.BatteryLifePercent == 255
                    ? -1 : status.BatteryLifePercent
            };
        }

        private static bool OptimizedChargingValue(Dictionary<string, object> result)
        {
            int value = AcerJson.Int(result, "Value", -1);
            if (value != 0 && value != 1)
                throw new InvalidOperationException("Acer reported an invalid charging-limit value.");
            return value == 0;
        }

        private static int PowerModeValue(Dictionary<string, object> result)
        {
            int value = AcerJson.Int(result, "Value", -1);
            if (value < 0 || value > 2)
                throw new InvalidOperationException("Acer reported an invalid power-mode value.");
            return value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    }
}
