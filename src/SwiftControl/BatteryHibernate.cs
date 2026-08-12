using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SwiftControl
{
    internal sealed class BatteryHibernateStatus
    {
        public bool Managed { get; private set; }
        public uint TimeoutSeconds { get; private set; }

        public BatteryHibernateStatus(bool managed, uint timeoutSeconds)
        {
            Managed = managed;
            TimeoutSeconds = timeoutSeconds;
        }
    }

    internal static class BatteryHibernate
    {
        public const uint ManagedTimeoutSeconds = 30 * 60;

        private const string RegistryPath = @"Software\SwiftControl";
        private const string ManagedValue = "BatteryHibernateManaged";
        private const string RestoreValue = "BatteryHibernateRestoreSeconds";
        private const uint DefaultRestoreSeconds = 90 * 60;

        private static readonly Guid SleepSubgroup = new Guid(
            "238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid HibernateIdle = new Guid(
            "9d7815a6-7ee4-497e-8888-515a05f02364");

        public static BatteryHibernateStatus ReadStatus()
        {
            uint timeout = ReadDcTimeout();
            return new BatteryHibernateStatus(
                ReadManagedPreference() && timeout == ManagedTimeoutSeconds,
                timeout);
        }

        public static BatteryHibernateStatus SetManaged(bool enabled)
        {
            uint current = ReadDcTimeout();
            if (enabled)
            {
                bool alreadyManaged =
                    ReadManagedPreference() && current == ManagedTimeoutSeconds;
                if (!alreadyManaged) SaveEnableState(current);
                try
                {
                    WriteDcTimeout(ManagedTimeoutSeconds);
                }
                catch
                {
                    SaveManagedPreference(false);
                    throw;
                }
            }
            else
            {
                uint restore = ReadRestoreTimeout();
                WriteDcTimeout(restore);
                SaveManagedPreference(false);
            }

            BatteryHibernateStatus result = ReadStatus();
            if (result.Managed != enabled)
                throw new InvalidOperationException(
                    "Windows did not retain the battery hibernate timeout.");
            return result;
        }

        public static string FormatTimeout(uint seconds)
        {
            if (seconds == 0) return "Never";
            if (seconds % 3600 == 0)
            {
                uint hours = seconds / 3600;
                return hours.ToString(CultureInfo.InvariantCulture) +
                    (hours == 1 ? " hour" : " hours");
            }
            if (seconds % 60 == 0)
            {
                uint minutes = seconds / 60;
                return minutes.ToString(CultureInfo.InvariantCulture) + " min";
            }
            return seconds.ToString(CultureInfo.InvariantCulture) + " sec";
        }

        private static uint ReadDcTimeout()
        {
            Guid scheme = ReadActiveScheme();
            Guid subgroup = SleepSubgroup;
            Guid setting = HibernateIdle;
            uint value;
            uint result = PowerReadDCValueIndex(
                IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
            ThrowIfFailed(result, "read the battery hibernate timeout");
            return value;
        }

        private static void WriteDcTimeout(uint seconds)
        {
            Guid scheme = ReadActiveScheme();
            Guid subgroup = SleepSubgroup;
            Guid setting = HibernateIdle;
            uint result = PowerWriteDCValueIndex(
                IntPtr.Zero, ref scheme, ref subgroup, ref setting, seconds);
            ThrowIfFailed(result, "change the battery hibernate timeout");

            result = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            ThrowIfFailed(result, "apply the battery hibernate timeout");

            uint retained;
            result = PowerReadDCValueIndex(
                IntPtr.Zero, ref scheme, ref subgroup, ref setting, out retained);
            ThrowIfFailed(result, "verify the battery hibernate timeout");
            if (retained != seconds)
                throw new InvalidOperationException(
                    "Windows retained a different battery hibernate timeout.");
        }

        private static Guid ReadActiveScheme()
        {
            IntPtr pointer;
            uint result = PowerGetActiveScheme(IntPtr.Zero, out pointer);
            ThrowIfFailed(result, "read the active Windows power plan");
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Windows returned no active power-plan identifier.");
            try
            {
                return (Guid)Marshal.PtrToStructure(pointer, typeof(Guid));
            }
            finally
            {
                LocalFree(pointer);
            }
        }

        private static void SaveEnableState(uint restoreSeconds)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                    throw new InvalidOperationException(
                        "The SwiftControl settings key is unavailable.");
                key.SetValue(RestoreValue, restoreSeconds, RegistryValueKind.DWord);
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

        private static bool ReadManagedPreference()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null) return false;
                    return Convert.ToInt32(key.GetValue(ManagedValue, 0),
                        CultureInfo.InvariantCulture) != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static uint ReadRestoreTimeout()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null) return DefaultRestoreSeconds;
                    object stored = key.GetValue(RestoreValue, DefaultRestoreSeconds);
                    long value = Convert.ToInt64(stored, CultureInfo.InvariantCulture);
                    if (value < 0 || value > UInt32.MaxValue) return DefaultRestoreSeconds;
                    return (uint)value;
                }
            }
            catch
            {
                return DefaultRestoreSeconds;
            }
        }

        private static void ThrowIfFailed(uint result, string action)
        {
            if (result != 0)
                throw new Win32Exception((int)result, "Could not " + action + ".");
        }

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerGetActiveScheme(
            IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid,
            ref Guid powerSettingGuid, out uint valueIndex);

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid,
            ref Guid powerSettingGuid, uint valueIndex);

        [DllImport("powrprof.dll", ExactSpelling = true)]
        private static extern uint PowerSetActiveScheme(
            IntPtr userRootPowerKey, ref Guid schemeGuid);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
