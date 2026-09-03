using System;
using Microsoft.Win32;

namespace SwiftControl
{
    internal sealed class TouchpadLightingSettings
    {
        private const string RegistryPath = @"Software\SwiftControl";

        public bool CodexEnabled { get; set; }
        public string CodexEffect { get; set; }

        public static TouchpadLightingSettings Load()
        {
            TouchpadLightingSettings settings = Defaults();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null) return settings;
                    settings.CodexEnabled = ReadInt(key, "LightingCodexEnabled", 1) != 0;
                    settings.CodexEffect = ValidEffect(
                        Convert.ToString(key.GetValue("LightingCodexEffect", settings.CodexEffect)),
                        AcerLightingEffects.Twinkle);
                }
            }
            catch
            {
                // Preference corruption must never prevent the tray utility from starting.
            }
            return settings;
        }

        public void Save()
        {
            CodexEffect = ValidEffect(CodexEffect, AcerLightingEffects.Twinkle);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                    throw new InvalidOperationException("The SwiftControl settings key is unavailable.");
                key.SetValue("LightingCodexEnabled", CodexEnabled ? 1 : 0,
                    RegistryValueKind.DWord);
                key.SetValue("LightingCodexEffect", CodexEffect,
                    RegistryValueKind.String);
            }
        }

        public static int PreviewDuration(string effect)
        {
            effect = ValidEffect(effect, AcerLightingEffects.Blink);
            if (effect == AcerLightingEffects.Blink) return 1500;
            if (effect == AcerLightingEffects.Breath) return 2600;
            if (effect == AcerLightingEffects.Circle) return 3500;
            return 3800;
        }

        private static TouchpadLightingSettings Defaults()
        {
            return new TouchpadLightingSettings
            {
                CodexEnabled = true,
                CodexEffect = AcerLightingEffects.Twinkle
            };
        }

        private static string ValidEffect(string effect, string fallback)
        {
            try { return AcerLightingEffects.Normalize(effect); }
            catch (ArgumentException) { return fallback; }
        }

        private static int ReadInt(RegistryKey key, string name, int fallback)
        {
            try { return Convert.ToInt32(key.GetValue(name, fallback)); }
            catch { return fallback; }
        }
    }
}
