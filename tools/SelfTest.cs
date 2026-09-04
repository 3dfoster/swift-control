using System;
using System.Threading;

namespace SwiftControl
{
    internal static class SelfTest
    {
        private static int Main(string[] args)
        {
            try
            {
                DashboardSnapshot snapshot = DashboardReader.Read();
                Console.WriteLine("Battery: {0}% ({1})",
                    snapshot.BatteryPercent,
                    snapshot.OnAcPower ? "AC" : "battery");
                Console.WriteLine("80% limit: {0}", snapshot.OptimizedCharging ? "enabled" : "disabled");
                Console.WriteLine("Power mode: {0}", snapshot.CurrentPowerMode);

                VerifyPowerProfiles();
                Console.WriteLine("Power-profile mappings and hysteresis verified.");
                VerifyBatteryHibernateFormatting();
                Console.WriteLine("Battery-hibernate timeout formatting verified.");
                VerifyModernStandbyNetworkFormatting();
                Console.WriteLine("Modern Standby network-policy formatting verified.");
                VerifyLockAfterSuspendPolicy();
                Console.WriteLine("Lock-after-suspend policy verified.");
                VerifySleepOnLidSettings();
                Console.WriteLine("Sleep-on-lid settings verified.");
                WifiConnection wifi = WifiNetwork.Current();
                Console.WriteLine("Wi-Fi: {0}",
                    wifi == null ? "not connected" : wifi.Name);

                if (args.Length > 0 && String.Equals(
                    args[0], "--lighting-probe", StringComparison.OrdinalIgnoreCase))
                {
                    ProbeLighting(false);
                }

                if (args.Length > 0 && String.Equals(
                    args[0], "--lighting-blink", StringComparison.OrdinalIgnoreCase))
                {
                    ProbeLighting(true);
                }

                if (args.Length > 0 && String.Equals(args[0], "--write-current",
                    StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Verifying no-op writes using the current values...");
                    if (!DashboardReader.SetOptimizedCharging(snapshot.OptimizedCharging))
                        throw new InvalidOperationException("Charge-limit verification failed.");
                    if (!DashboardReader.SetPowerMode(snapshot.CurrentPowerMode))
                        throw new InvalidOperationException("Power-mode verification failed.");
                    Console.WriteLine("No-op writes verified.");
                }

                if (args.Length > 0 && String.Equals(
                    args[0], "--enable-battery-hibernate",
                    StringComparison.OrdinalIgnoreCase))
                {
                    BatteryHibernateStatus hibernate = BatteryHibernate.SetManaged(true);
                    Console.WriteLine("Battery hibernate: {0} ({1})",
                        hibernate.Managed ? "managed" : "not managed",
                        BatteryHibernate.FormatTimeout(hibernate.TimeoutSeconds));
                }

                if (args.Length > 0 && String.Equals(
                    args[0], "--disable-battery-hibernate",
                    StringComparison.OrdinalIgnoreCase))
                {
                    BatteryHibernateStatus hibernate = BatteryHibernate.SetManaged(false);
                    Console.WriteLine("Battery hibernate: {0} ({1})",
                        hibernate.Managed ? "managed" : "not managed",
                        BatteryHibernate.FormatTimeout(hibernate.TimeoutSeconds));
                }

                if (args.Length > 0 && String.Equals(
                    args[0], "--disconnect-battery-standby",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ModernStandbyNetworkStatus network =
                        ModernStandbyNetwork.SetDisconnected(true);
                    Console.WriteLine("Standby network: battery {0}; AC {1}",
                        ModernStandbyNetwork.FormatPolicy(network.BatteryPolicy),
                        ModernStandbyNetwork.FormatPolicy(network.PluggedInPolicy));
                }

                if (args.Length > 0 && String.Equals(
                    args[0], "--restore-battery-standby-network",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ModernStandbyNetworkStatus network =
                        ModernStandbyNetwork.SetDisconnected(false);
                    Console.WriteLine("Standby network: battery {0}; AC {1}",
                        ModernStandbyNetwork.FormatPolicy(network.BatteryPolicy),
                        ModernStandbyNetwork.FormatPolicy(network.PluggedInPolicy));
                }

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.ToString());
                return 1;
            }
        }

        private static void ProbeLighting(bool blink)
        {
            using (AcerLightingClient lighting = new AcerLightingClient())
            {
                lighting.Connect();
                int[] devices = lighting.GetDevices();
                Console.WriteLine("Acer lighting: port {0}; devices [{1}]",
                    lighting.Port, String.Join(",", devices));
                foreach (int device in devices)
                {
                    Console.WriteLine("Lighting device {0}: {1}", device,
                        lighting.GetEnabled(device) ? "enabled" : "disabled");
                }

                if (!blink) return;
                try
                {
                    lighting.PlayEffect(AcerLightingEffects.Blink);
                    Thread.Sleep(1500);
                }
                finally
                {
                    lighting.TerminateEffect();
                }
                Console.WriteLine("Acer touchpad Blink effect verified and terminated.");
            }
        }

        private static void VerifyPowerProfiles()
        {
            AssertProfile(PowerProfiles.BatterySaver, 0, 0);
            AssertProfile(PowerProfiles.Everyday, 1, 1);
            AssertProfile(PowerProfiles.Responsive, 1, 2);
            AssertProfile(PowerProfiles.Maximum, 2, 2);

            PowerAutomationSettings settings = new PowerAutomationSettings
            {
                PluggedInProfile = PowerProfiles.Maximum,
                UnpluggedProfile = PowerProfiles.Responsive,
                LowBatteryProfile = PowerProfiles.BatterySaver,
                LowBatteryThreshold = 25
            };
            Assert(settings.ConditionFor(true, 25, -1) == 0,
                "AC should select the plugged-in rule.");
            Assert(settings.ConditionFor(false, 25, 1) == 2,
                "The low-battery rule should enter at its threshold.");
            Assert(settings.ConditionFor(false, 29, 2) == 2,
                "The low-battery rule should remain active inside the hysteresis band.");
            Assert(settings.ConditionFor(false, 30, 2) == 1,
                "The low-battery rule should clear five points above its threshold.");

            settings.SetProfileForCondition(0, PowerProfiles.Responsive);
            settings.SetProfileForCondition(1, PowerProfiles.Maximum);
            settings.SetProfileForCondition(2, PowerProfiles.Everyday);
            Assert(settings.ProfileForCondition(0).Value == PowerProfiles.Responsive,
                "The plugged-in assignment should be replaceable.");
            Assert(settings.ProfileForCondition(1).Value == PowerProfiles.Maximum,
                "The battery assignment should be replaceable.");
            Assert(settings.ProfileForCondition(2).Value == PowerProfiles.Everyday,
                "The low-battery assignment should be replaceable.");
        }

        private static void AssertProfile(int value, int acerMode, int windowsMode)
        {
            PowerProfileOption profile = PowerProfiles.Get(value);
            Assert(profile.AcerMode == acerMode && profile.WindowsMode == windowsMode,
                "Unexpected mapping for " + profile.Name + ".");
            Assert(Object.ReferenceEquals(profile,
                PowerProfiles.Match(acerMode, windowsMode)),
                "The paired profile should round-trip through Match.");
        }

        private static void VerifyBatteryHibernateFormatting()
        {
            Assert(BatteryHibernate.ManagedTimeoutSeconds == 1800,
                "The managed battery timeout should be 30 minutes.");
            Assert(BatteryHibernate.FormatTimeout(0) == "Never",
                "A zero timeout should be shown as Never.");
            Assert(BatteryHibernate.FormatTimeout(1800) == "30 min",
                "The managed timeout should be shown in minutes.");
            Assert(BatteryHibernate.FormatTimeout(5400) == "90 min",
                "A fractional-hour timeout should be shown in minutes.");
            Assert(BatteryHibernate.FormatTimeout(7200) == "2 hours",
                "Whole-hour timeouts should be shown in hours.");
        }

        private static void VerifyModernStandbyNetworkFormatting()
        {
            Assert(ModernStandbyNetwork.FormatPolicy(
                ModernStandbyNetwork.Disabled) == "Disconnected",
                "A disabled standby network should be shown as disconnected.");
            Assert(ModernStandbyNetwork.FormatPolicy(
                ModernStandbyNetwork.Enabled) == "Connected",
                "An enabled standby network should be shown as connected.");
            Assert(ModernStandbyNetwork.FormatPolicy(
                ModernStandbyNetwork.WindowsManaged) == "Managed by Windows",
                "The automatic standby policy should be shown as Windows-managed.");
        }

        private static void VerifyLockAfterSuspendPolicy()
        {
            Guid adapter = new Guid("11111111-2222-3333-4444-555555555555");
            WifiConnection home = new WifiConnection(
                WifiNetwork.StableId(adapter, "Home"), "Home");
            WifiConnection cafe = new WifiConnection(
                WifiNetwork.StableId(adapter, "Cafe"), "Cafe");
            LockAfterSuspendSettings settings = new LockAfterSuspendSettings();

            Assert(!settings.Evaluate(cafe).ShouldLock,
                "Locking should default to off.");
            settings.Mode = LockAfterSuspendSettings.SmartMode;
            Assert(settings.Evaluate(cafe).ShouldLock,
                "Smart mode should lock on an untrusted Wi-Fi.");
            Assert(settings.Evaluate(null).ShouldLock,
                "Smart mode should lock when no Wi-Fi is connected.");
            settings.SetTrusted(home, true);
            Assert(!settings.Evaluate(home).ShouldLock,
                "Smart mode should remain unlocked on trusted Wi-Fi.");
            Assert(settings.Evaluate(cafe).ShouldLock,
                "Trust should be scoped to one Wi-Fi profile.");
            settings.Mode = LockAfterSuspendSettings.AlwaysMode;
            Assert(settings.Evaluate(home).ShouldLock,
                "Always should lock even on a trusted Wi-Fi.");
            settings.Mode = 99;
            Assert(settings.Mode == LockAfterSuspendSettings.Off,
                "An invalid persisted mode should fall back to Off.");
            settings.SetTrusted(home, false);
            Assert(!settings.IsTrusted(home),
                "A trusted Wi-Fi should be removable.");
        }

        private static void VerifySleepOnLidSettings()
        {
            int[] valid = { 0, 15, 30, 60, 90 };
            foreach (int delay in valid)
                Assert(SleepOnLidSettings.ValidDelay(delay) == delay,
                    "A supported lid-sleep delay should remain unchanged.");
            Assert(SleepOnLidSettings.ValidDelay(12) == 15,
                "An unsupported lid-sleep delay should fall back to 15 minutes.");
            Assert(SleepOnLidSettings.FormatDelay(0) == "Immediately",
                "The zero-minute delay should be labeled Immediately.");
            Assert(SleepOnLidSettings.FormatDelay(60) == "60 min",
                "Minute delays should use the compact label.");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
