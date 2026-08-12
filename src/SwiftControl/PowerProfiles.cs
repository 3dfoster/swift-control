using System;

namespace SwiftControl
{
    internal sealed class PowerProfileOption
    {
        public int Value { get; set; }
        public string Name { get; set; }
        public string Caption { get; set; }
        public int AcerMode { get; set; }
        public int WindowsMode { get; set; }
        public string Description { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class PowerProfiles
    {
        public const int BatterySaver = 0;
        public const int Everyday = 1;
        public const int Responsive = 2;
        public const int Maximum = 3;

        private static readonly PowerProfileOption[] Profiles =
        {
            new PowerProfileOption
            {
                Value = BatterySaver,
                Name = "Battery Saver",
                Caption = "quiet · efficient",
                AcerMode = 0,
                WindowsMode = 0,
                Description = "Silent system profile with Windows Best power efficiency."
            },
            new PowerProfileOption
            {
                Value = Everyday,
                Name = "Everyday",
                Caption = "cool · balanced",
                AcerMode = 1,
                WindowsMode = 1,
                Description = "Normal system profile with Windows Balanced."
            },
            new PowerProfileOption
            {
                Value = Responsive,
                Name = "Responsive",
                Caption = "normal · fast",
                AcerMode = 1,
                WindowsMode = 2,
                Description = "Normal system profile with Windows Best performance."
            },
            new PowerProfileOption
            {
                Value = Maximum,
                Name = "Maximum",
                Caption = "highest sustained",
                AcerMode = 2,
                WindowsMode = 2,
                Description = "Performance system profile with Windows Best performance."
            }
        };

        public static PowerProfileOption[] All()
        {
            return (PowerProfileOption[])Profiles.Clone();
        }

        public static PowerProfileOption Get(int value)
        {
            foreach (PowerProfileOption profile in Profiles)
            {
                if (profile.Value == value) return profile;
            }
            return Profiles[Everyday];
        }

        public static PowerProfileOption Match(int acerMode, int windowsMode)
        {
            foreach (PowerProfileOption profile in Profiles)
            {
                if (profile.AcerMode == acerMode && profile.WindowsMode == windowsMode)
                    return profile;
            }
            return null;
        }

        public static int FromLegacyAcerMode(int acerMode)
        {
            if (acerMode == 0) return BatterySaver;
            if (acerMode == 2) return Maximum;
            return Everyday;
        }

        public static bool IsValid(int value)
        {
            return value >= BatterySaver && value <= Maximum;
        }

        public static string AcerModeName(int mode)
        {
            if (mode == 0) return "Silent";
            if (mode == 2) return "Performance";
            return "Normal";
        }

        public static int BatteryPl1(int mode)
        {
            if (mode == 0) return 15;
            if (mode == 2) return 30;
            return 20;
        }

        public static string AcerDescription(int mode, bool onAcPower)
        {
            string name = AcerModeName(mode);
            if (onAcPower)
                return name + " · fan and thermal envelope · AC limits not measured";
            return name + " · " + BatteryPl1(mode) +
                " W sustained target · 37 W short boost · measured on battery";
        }

        public static string WindowsDescription(int mode)
        {
            if (mode == 0) return "Best power efficiency · reduces performance requests";
            if (mode == 2) return "Best performance · requests maximum responsiveness";
            return "Balanced · adapts performance to demand";
        }

        public static string CurrentDescription(
            int acerMode, int windowsMode, bool onAcPower)
        {
            if (acerMode < 0 || windowsMode < 0) return "Reading current policies…";
            string acer = AcerModeName(acerMode);
            string windows = WindowsPowerMode.Name(windowsMode);
            if (onAcPower)
                return acer + " system profile · Windows " + windows;
            return acer + " · " + BatteryPl1(acerMode) +
                " W sustained · Windows " + windows;
        }
    }
}
