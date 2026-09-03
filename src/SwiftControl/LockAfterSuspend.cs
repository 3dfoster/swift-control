using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SwiftControl
{
    internal sealed class WifiConnection
    {
        public string Id { get; private set; }
        public string Name { get; private set; }

        public WifiConnection(string id, string name)
        {
            Id = id ?? "";
            Name = name ?? "";
        }
    }

    internal sealed class LockAfterSuspendDecision
    {
        public bool ShouldLock { get; private set; }
        public WifiConnection Wifi { get; private set; }

        public LockAfterSuspendDecision(bool shouldLock, WifiConnection wifi)
        {
            ShouldLock = shouldLock;
            Wifi = wifi;
        }
    }

    internal sealed class LockAfterSuspendSettings
    {
        private const string RegistryPath = @"Software\SwiftControl";
        private const string ModeValue = "LockAfterSuspendMode";
        private const string AlwaysValue = "LockAfterSuspendAlways";
        private const string SmartValue = "LockAfterSuspendSmart";
        private const string TrustedWifiValue = "LockAfterSuspendTrustedWifi";

        public const int Off = 0;
        public const int SmartMode = 1;
        public const int AlwaysMode = 2;

        private readonly object _sync = new object();
        private readonly HashSet<string> _trustedWifi =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private int _mode;

        public int Mode
        {
            get { lock (_sync) return _mode; }
            set { lock (_sync) _mode = ValidMode(value); }
        }

        public static LockAfterSuspendSettings Load()
        {
            LockAfterSuspendSettings settings = new LockAfterSuspendSettings();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RegistryPath, false))
                {
                    if (key == null) return settings;
                    object storedMode = key.GetValue(ModeValue, null);
                    if (storedMode != null)
                    {
                        settings.Mode = ReadInt(key, ModeValue, Off);
                    }
                    else if (ReadBool(key, AlwaysValue))
                    {
                        settings.Mode = AlwaysMode;
                    }
                    else if (ReadBool(key, SmartValue))
                    {
                        settings.Mode = SmartMode;
                    }
                    string[] trusted = key.GetValue(TrustedWifiValue,
                        new string[0]) as string[];
                    if (trusted != null)
                    {
                        foreach (string id in trusted)
                        {
                            if (!String.IsNullOrWhiteSpace(id))
                                settings._trustedWifi.Add(id);
                        }
                    }
                }
            }
            catch
            {
                // Invalid or unavailable preferences leave locking safely off.
            }
            return settings;
        }

        public void Save()
        {
            lock (_sync)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    if (key == null)
                        throw new InvalidOperationException(
                            "The SwiftControl settings key is unavailable.");
                    int mode = _mode;
                    key.SetValue(ModeValue, mode, RegistryValueKind.DWord);
                    // Keep the original values coherent for older builds.
                    key.SetValue(AlwaysValue, mode == AlwaysMode ? 1 : 0,
                        RegistryValueKind.DWord);
                    key.SetValue(SmartValue, mode == SmartMode ? 1 : 0,
                        RegistryValueKind.DWord);
                    string[] ids = new string[_trustedWifi.Count];
                    _trustedWifi.CopyTo(ids);
                    Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
                    key.SetValue(TrustedWifiValue, ids,
                        RegistryValueKind.MultiString);
                }
            }
        }

        public bool IsTrusted(WifiConnection wifi)
        {
            if (wifi == null || String.IsNullOrEmpty(wifi.Id)) return false;
            lock (_sync) return _trustedWifi.Contains(wifi.Id);
        }

        public void SetTrusted(WifiConnection wifi, bool trusted)
        {
            if (wifi == null || String.IsNullOrEmpty(wifi.Id))
                throw new ArgumentException("No connected Wi-Fi is available.", "wifi");
            lock (_sync)
            {
                if (trusted) _trustedWifi.Add(wifi.Id);
                else _trustedWifi.Remove(wifi.Id);
            }
        }

        public LockAfterSuspendDecision Evaluate(WifiConnection wifi)
        {
            lock (_sync)
            {
                bool shouldLock = _mode == AlwaysMode ||
                    (_mode == SmartMode &&
                    (wifi == null || !_trustedWifi.Contains(wifi.Id)));
                return new LockAfterSuspendDecision(shouldLock, wifi);
            }
        }

        public LockAfterSuspendDecision EvaluateCurrent()
        {
            return Evaluate(WifiNetwork.Current());
        }

        private static bool ReadBool(RegistryKey key, string name)
        {
            try
            {
                return Convert.ToInt32(key.GetValue(name, 0),
                    CultureInfo.InvariantCulture) != 0;
            }
            catch { return false; }
        }

        private static int ReadInt(RegistryKey key, string name, int fallback)
        {
            try
            {
                return Convert.ToInt32(key.GetValue(name, fallback),
                    CultureInfo.InvariantCulture);
            }
            catch { return fallback; }
        }

        private static int ValidMode(int mode)
        {
            return mode >= Off && mode <= AlwaysMode ? mode : Off;
        }
    }

    internal static class WifiNetwork
    {
        private const uint ClientVersion = 2;
        private const int CurrentConnectionOpcode = 7;
        private const int ConnectedState = 1;

        public static WifiConnection Current()
        {
            IntPtr client = IntPtr.Zero;
            IntPtr interfaces = IntPtr.Zero;
            try
            {
                uint negotiated;
                if (WlanOpenHandle(ClientVersion, IntPtr.Zero,
                    out negotiated, out client) != 0) return null;
                if (WlanEnumInterfaces(client, IntPtr.Zero, out interfaces) != 0 ||
                    interfaces == IntPtr.Zero) return null;

                int count = Marshal.ReadInt32(interfaces, 0);
                long first = interfaces.ToInt64() + 8;
                int size = Marshal.SizeOf(typeof(WlanInterfaceInfo));
                for (int index = 0; index < count; index++)
                {
                    IntPtr item = new IntPtr(first + (long)index * size);
                    WlanInterfaceInfo info = (WlanInterfaceInfo)
                        Marshal.PtrToStructure(item, typeof(WlanInterfaceInfo));
                    WifiConnection connection = ReadConnection(client, info.InterfaceGuid);
                    if (connection != null) return connection;
                }
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (interfaces != IntPtr.Zero) WlanFreeMemory(interfaces);
                if (client != IntPtr.Zero) WlanCloseHandle(client, IntPtr.Zero);
            }
        }

        public static string StableId(Guid interfaceId, string profileName)
        {
            return interfaceId.ToString("D") + "|" + (profileName ?? "").Trim();
        }

        private static WifiConnection ReadConnection(IntPtr client, Guid interfaceId)
        {
            int dataSize;
            IntPtr data;
            int valueType;
            int result = WlanQueryInterface(client, ref interfaceId,
                CurrentConnectionOpcode, IntPtr.Zero, out dataSize,
                out data, out valueType);
            if (result != 0 || data == IntPtr.Zero) return null;
            try
            {
                WlanConnectionAttributes attributes = (WlanConnectionAttributes)
                    Marshal.PtrToStructure(data, typeof(WlanConnectionAttributes));
                if (attributes.InterfaceState != ConnectedState) return null;
                string name = (attributes.ProfileName ?? "").TrimEnd('\0').Trim();
                if (String.IsNullOrEmpty(name))
                    name = DecodeSsid(attributes.Association.Ssid);
                if (String.IsNullOrEmpty(name)) name = "Connected Wi-Fi";
                return new WifiConnection(StableId(interfaceId, name), name);
            }
            finally
            {
                WlanFreeMemory(data);
            }
        }

        private static string DecodeSsid(Dot11Ssid ssid)
        {
            if (ssid.Bytes == null || ssid.Length == 0) return "";
            int length = (int)Math.Min(ssid.Length, (uint)ssid.Bytes.Length);
            return Encoding.UTF8.GetString(ssid.Bytes, 0, length);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WlanInterfaceInfo
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Description;
            public int State;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WlanConnectionAttributes
        {
            public int InterfaceState;
            public int ConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ProfileName;
            public WlanAssociationAttributes Association;
            public WlanSecurityAttributes Security;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Dot11Ssid
        {
            public uint Length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] Bytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WlanAssociationAttributes
        {
            public Dot11Ssid Ssid;
            public int BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] Bssid;
            public int PhyType;
            public uint PhyIndex;
            public uint SignalQuality;
            public uint RxRate;
            public uint TxRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WlanSecurityAttributes
        {
            [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
            [MarshalAs(UnmanagedType.Bool)] public bool OneXEnabled;
            public int AuthAlgorithm;
            public int CipherAlgorithm;
        }

        [DllImport("wlanapi.dll")]
        private static extern int WlanOpenHandle(
            uint clientVersion, IntPtr reserved, out uint negotiatedVersion,
            out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        private static extern int WlanCloseHandle(
            IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        private static extern int WlanEnumInterfaces(
            IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        private static extern int WlanQueryInterface(
            IntPtr clientHandle, ref Guid interfaceGuid, int opcode,
            IntPtr reserved, out int dataSize, out IntPtr data,
            out int opcodeValueType);

        [DllImport("wlanapi.dll")]
        private static extern void WlanFreeMemory(IntPtr memory);
    }
}
