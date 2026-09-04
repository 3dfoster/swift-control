using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SwiftControl
{
    internal sealed class SleepOnLidSettings
    {
        private const string RegistryPath = @"Software\SwiftControl";
        private const string EnabledValue = "SleepOnLidEnabled";
        private const string DelayValue = "SleepOnLidDelayMinutes";

        public bool Enabled { get; set; }
        public int DelayMinutes { get; set; }

        public SleepOnLidSettings()
        {
            DelayMinutes = 15;
        }

        public static SleepOnLidSettings Load()
        {
            SleepOnLidSettings settings = new SleepOnLidSettings();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RegistryPath, false))
                {
                    if (key == null) return settings;
                    settings.Enabled = Convert.ToInt32(
                        key.GetValue(EnabledValue, 0), CultureInfo.InvariantCulture) != 0;
                    settings.DelayMinutes = ValidDelay(Convert.ToInt32(
                        key.GetValue(DelayValue, 15), CultureInfo.InvariantCulture));
                }
            }
            catch { }
            return settings;
        }

        public void Save()
        {
            DelayMinutes = ValidDelay(DelayMinutes);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                    throw new InvalidOperationException(
                        "The SwiftControl settings key is unavailable.");
                key.SetValue(EnabledValue, Enabled ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue(DelayValue, DelayMinutes, RegistryValueKind.DWord);
            }
        }

        public static int ValidDelay(int minutes)
        {
            return minutes == 0 || minutes == 15 || minutes == 30 ||
                minutes == 60 || minutes == 90 ? minutes : 15;
        }

        public static string FormatDelay(int minutes)
        {
            return minutes == 0 ? "Immediately" :
                minutes.ToString(CultureInfo.InvariantCulture) + " min";
        }
    }

    internal static class SleepOnLidPolicy
    {
        private const string RegistryPath = @"Software\SwiftControl";
        private const string ManagedValue = "SleepOnLidPolicyManaged";
        private const string RestoreDcLidValue = "SleepOnLidRestoreDcAction";
        private const uint DoNothing = 0;
        private const uint DefaultLidAction = 1;

        private static readonly Guid ButtonsSubgroup = new Guid(
            "4f971e89-eebd-4455-a8de-9e59040e7347");
        private static readonly Guid LidCloseAction = new Guid(
            "5ca83367-6e45-459f-a27b-476b1d01c936");
        private static readonly Guid SleepSubgroup = new Guid(
            "238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid SleepIdle = new Guid(
            "29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

        public static void Apply(bool enabled)
        {
            Guid scheme = ReadActiveScheme();
            if (enabled)
            {
                uint current = ReadValue(scheme, ButtonsSubgroup, LidCloseAction, false);
                if (!ReadManaged()) SaveManaged(current);
                WriteValue(scheme, ButtonsSubgroup, LidCloseAction, false, DoNothing);
            }
            else if (ReadManaged())
            {
                WriteValue(scheme, ButtonsSubgroup, LidCloseAction, false, ReadRestore());
                SaveManagedPreference(false);
            }

            // This machine's intentional AC policy is manual sleep only.
            WriteValue(scheme, ButtonsSubgroup, LidCloseAction, true, DoNothing);
            WriteValue(scheme, SleepSubgroup, SleepIdle, true, 0);
            Activate(scheme);
        }

        private static Guid ReadActiveScheme()
        {
            IntPtr pointer;
            uint result = PowerGetActiveScheme(IntPtr.Zero, out pointer);
            ThrowIfFailed(result, "read the active Windows power plan");
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Windows returned no active power-plan identifier.");
            try { return (Guid)Marshal.PtrToStructure(pointer, typeof(Guid)); }
            finally { LocalFree(pointer); }
        }

        private static uint ReadValue(
            Guid scheme, Guid subgroup, Guid setting, bool pluggedIn)
        {
            uint value;
            uint result = pluggedIn
                ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup,
                    ref setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup,
                    ref setting, out value);
            ThrowIfFailed(result, "read a Windows sleep policy");
            return value;
        }

        private static void WriteValue(
            Guid scheme, Guid subgroup, Guid setting, bool pluggedIn, uint value)
        {
            uint result = pluggedIn
                ? PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subgroup,
                    ref setting, value)
                : PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup,
                    ref setting, value);
            ThrowIfFailed(result, "change a Windows sleep policy");
        }

        private static void Activate(Guid scheme)
        {
            uint result = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            ThrowIfFailed(result, "apply the Windows sleep policies");
        }

        private static void SaveManaged(uint restore)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                    throw new InvalidOperationException(
                        "The SwiftControl settings key is unavailable.");
                key.SetValue(RestoreDcLidValue, restore, RegistryValueKind.DWord);
                key.SetValue(ManagedValue, 1, RegistryValueKind.DWord);
            }
        }

        private static void SaveManagedPreference(bool managed)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                    throw new InvalidOperationException(
                        "The SwiftControl settings key is unavailable.");
                key.SetValue(ManagedValue, managed ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        private static bool ReadManaged()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                    return key != null && Convert.ToInt32(
                        key.GetValue(ManagedValue, 0), CultureInfo.InvariantCulture) != 0;
            }
            catch { return false; }
        }

        private static uint ReadRestore()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath))
                {
                    if (key == null) return DefaultLidAction;
                    uint value = Convert.ToUInt32(
                        key.GetValue(RestoreDcLidValue, DefaultLidAction),
                        CultureInfo.InvariantCulture);
                    return value <= 3 ? value : DefaultLidAction;
                }
            }
            catch { return DefaultLidAction; }
        }

        private static void ThrowIfFailed(uint result, string action)
        {
            if (result != 0)
                throw new Win32Exception((int)result, "Could not " + action + ".");
        }

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme,
            ref Guid subgroup, ref Guid setting, out uint value);
        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme,
            ref Guid subgroup, ref Guid setting, out uint value);
        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme,
            ref Guid subgroup, ref Guid setting, uint value);
        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme,
            ref Guid subgroup, ref Guid setting, uint value);
        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
