using System;

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

                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.ToString());
                return 1;
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

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
