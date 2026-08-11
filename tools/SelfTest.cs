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
    }
}
