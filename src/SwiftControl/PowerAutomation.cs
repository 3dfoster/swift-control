using System;
using Microsoft.Win32;

namespace SwiftControl
{
    internal sealed class PowerAutomationSettings
    {
        private const string RegistryPath = @"Software\SwiftControl";
        private const int LowBatteryHysteresis = 5;

        public bool Enabled { get; set; }
        public int PluggedInProfile { get; set; }
        public int UnpluggedProfile { get; set; }
        public int LowBatteryProfile { get; set; }
        public int LowBatteryThreshold { get; set; }

        public static PowerAutomationSettings Load()
        {
            PowerAutomationSettings settings = Defaults();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null) return settings;
                    settings.Enabled = ReadInt(key, "AutomationEnabled", 0) != 0;
                    settings.PluggedInProfile = ReadProfileOrLegacy(
                        key, "PluggedInProfile", "PluggedInMode", settings.PluggedInProfile);
                    settings.UnpluggedProfile = ReadProfileOrLegacy(
                        key, "UnpluggedProfile", "UnpluggedMode", settings.UnpluggedProfile);
                    settings.LowBatteryProfile = ReadProfileOrLegacy(
                        key, "LowBatteryProfile", "LowBatteryMode", settings.LowBatteryProfile);
                    settings.LowBatteryThreshold = ValidThreshold(ReadInt(
                        key, "LowBatteryThreshold", settings.LowBatteryThreshold));
                }
            }
            catch
            {
                // A corrupt or unavailable preference must not prevent the tray
                // application from starting. The defaults remain safe and off.
            }
            return settings;
        }

        public void Save()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                    throw new InvalidOperationException("The SwiftControl settings key is unavailable.");
                key.SetValue("AutomationEnabled", Enabled ? 1 : 0, RegistryValueKind.DWord);
                PluggedInProfile = ValidProfile(PluggedInProfile, PowerProfiles.Maximum);
                UnpluggedProfile = ValidProfile(UnpluggedProfile, PowerProfiles.Responsive);
                LowBatteryProfile = ValidProfile(LowBatteryProfile, PowerProfiles.BatterySaver);
                key.SetValue("PluggedInProfile", PluggedInProfile, RegistryValueKind.DWord);
                key.SetValue("UnpluggedProfile", UnpluggedProfile, RegistryValueKind.DWord);
                key.SetValue("LowBatteryProfile", LowBatteryProfile, RegistryValueKind.DWord);

                // Retain the old Acer-only values so downgrading SwiftControl
                // does not discard the user's hardware-profile preferences.
                key.SetValue("PluggedInMode", PowerProfiles.Get(PluggedInProfile).AcerMode,
                    RegistryValueKind.DWord);
                key.SetValue("UnpluggedMode", PowerProfiles.Get(UnpluggedProfile).AcerMode,
                    RegistryValueKind.DWord);
                key.SetValue("LowBatteryMode", PowerProfiles.Get(LowBatteryProfile).AcerMode,
                    RegistryValueKind.DWord);
                key.SetValue("LowBatteryThreshold", ValidThreshold(LowBatteryThreshold),
                    RegistryValueKind.DWord);
            }
        }

        public int ConditionFor(bool onAcPower, int batteryPercent, int previousCondition)
        {
            if (onAcPower) return 0;
            if (batteryPercent >= 0)
            {
                if (previousCondition == 2 &&
                    batteryPercent < LowBatteryThreshold + LowBatteryHysteresis)
                    return 2;
                if (batteryPercent <= LowBatteryThreshold) return 2;
            }
            return 1;
        }

        public PowerProfileOption ProfileForCondition(int condition)
        {
            if (condition == 0) return PowerProfiles.Get(PluggedInProfile);
            if (condition == 2) return PowerProfiles.Get(LowBatteryProfile);
            return PowerProfiles.Get(UnpluggedProfile);
        }

        public void SetProfileForCondition(int condition, int profileValue)
        {
            if (!PowerProfiles.IsValid(profileValue))
                throw new ArgumentOutOfRangeException("profileValue");
            if (condition == 0) PluggedInProfile = profileValue;
            else if (condition == 1) UnpluggedProfile = profileValue;
            else if (condition == 2) LowBatteryProfile = profileValue;
            else throw new ArgumentOutOfRangeException("condition");
        }

        public static int ValidThreshold(int value)
        {
            return Math.Max(1, Math.Min(99, value));
        }

        private static PowerAutomationSettings Defaults()
        {
            return new PowerAutomationSettings
            {
                Enabled = false,
                PluggedInProfile = PowerProfiles.Maximum,
                UnpluggedProfile = PowerProfiles.Responsive,
                LowBatteryProfile = PowerProfiles.BatterySaver,
                LowBatteryThreshold = 25
            };
        }

        private static int ValidProfile(int value, int fallback)
        {
            return PowerProfiles.IsValid(value) ? value : fallback;
        }

        private static int ReadProfileOrLegacy(
            RegistryKey key, string profileName, string legacyModeName, int fallback)
        {
            object storedProfile = key.GetValue(profileName, null);
            if (storedProfile != null)
                return ValidProfile(ReadInt(key, profileName, fallback), fallback);

            object storedMode = key.GetValue(legacyModeName, null);
            if (storedMode == null) return fallback;
            int legacyMode = ReadInt(key, legacyModeName,
                PowerProfiles.Get(fallback).AcerMode);
            return PowerProfiles.FromLegacyAcerMode(legacyMode);
        }

        private static int ReadInt(RegistryKey key, string name, int fallback)
        {
            object value = key.GetValue(name, fallback);
            try { return Convert.ToInt32(value); }
            catch { return fallback; }
        }
    }
}
