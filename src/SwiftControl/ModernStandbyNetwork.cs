using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SwiftControl
{
    internal sealed class ModernStandbyNetworkStatus
    {
        public uint BatteryPolicy { get; private set; }
        public uint PluggedInPolicy { get; private set; }
        public bool Disconnected { get { return BatteryPolicy == 0; } }

        public ModernStandbyNetworkStatus(uint batteryPolicy, uint pluggedInPolicy)
        {
            BatteryPolicy = batteryPolicy;
            PluggedInPolicy = pluggedInPolicy;
        }
    }

    internal static class ModernStandbyNetwork
    {
        public const uint Disabled = 0;
        public const uint Enabled = 1;
        public const uint WindowsManaged = 2;

        private const string RegistryPath = @"Software\SwiftControl";
        private const string RestoreValue = "StandbyNetworkRestorePolicy";

        private static readonly Guid SleepSubgroup = new Guid(
            "238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid ConnectivityInStandby = new Guid(
            "f15576e8-98b7-4186-b944-eafa664402d9");

        public static ModernStandbyNetworkStatus ReadStatus()
        {
            Guid scheme = ReadActiveScheme();
            return new ModernStandbyNetworkStatus(
                ReadPolicy(scheme, false), ReadPolicy(scheme, true));
        }

        public static ModernStandbyNetworkStatus SetDisconnected(bool disconnected)
        {
            ModernStandbyNetworkStatus before = ReadStatus();
            uint target;
            if (disconnected)
            {
                if (before.BatteryPolicy != Disabled)
                    SaveRestorePolicy(before.BatteryPolicy);
                target = Disabled;
            }
            else
            {
                target = ReadRestorePolicy();
            }

            WriteBatteryPolicy(target);
            ModernStandbyNetworkStatus result = ReadStatus();
            if (result.Disconnected != disconnected)
                throw new InvalidOperationException(
                    "Windows did not retain the battery standby-network policy.");
            if (result.PluggedInPolicy != before.PluggedInPolicy)
                throw new InvalidOperationException(
                    "The plugged-in standby-network policy changed unexpectedly.");
            return result;
        }

        public static string FormatPolicy(uint policy)
        {
            if (policy == Disabled) return "Disconnected";
            if (policy == Enabled) return "Connected";
            if (policy == WindowsManaged) return "Managed by Windows";
            return "Unknown (" + policy.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static uint ReadPolicy(Guid scheme, bool pluggedIn)
        {
            Guid subgroup = SleepSubgroup;
            Guid setting = ConnectivityInStandby;
            uint value;
            uint result = pluggedIn
                ? PowerReadACValueIndex(
                    IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
                : PowerReadDCValueIndex(
                    IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
            ThrowIfFailed(result, pluggedIn
                ? "read the plugged-in standby-network policy"
                : "read the battery standby-network policy");
            return value;
        }

        private static void WriteBatteryPolicy(uint policy)
        {
            Guid scheme = ReadActiveScheme();
            Guid subgroup = SleepSubgroup;
            Guid setting = ConnectivityInStandby;
            uint result = PowerWriteDCValueIndex(
                IntPtr.Zero, ref scheme, ref subgroup, ref setting, policy);
            ThrowIfFailed(result, "change the battery standby-network policy");

            result = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            ThrowIfFailed(result, "apply the battery standby-network policy");

            uint retained = ReadPolicy(scheme, false);
            if (retained != policy)
                throw new InvalidOperationException(
                    "Windows retained a different battery standby-network policy.");
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

        private static void SaveRestorePolicy(uint policy)
        {
            if (!IsRestorable(policy)) return;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                if (key == null)
                    throw new InvalidOperationException(
                        "The SwiftControl settings key is unavailable.");
                key.SetValue(RestoreValue, policy, RegistryValueKind.DWord);
            }
        }

        private static uint ReadRestorePolicy()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
                {
                    if (key == null) return WindowsManaged;
                    object stored = key.GetValue(RestoreValue, WindowsManaged);
                    uint policy = Convert.ToUInt32(stored, CultureInfo.InvariantCulture);
                    return IsRestorable(policy) ? policy : WindowsManaged;
                }
            }
            catch
            {
                return WindowsManaged;
            }
        }

        private static bool IsRestorable(uint policy)
        {
            return policy == Enabled || policy == WindowsManaged;
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
        private static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid,
            ref Guid powerSettingGuid, out uint valueIndex);

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
