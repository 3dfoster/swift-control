using System;
using System.Runtime.InteropServices;

internal static class PeResources
{
    private delegate bool EnumTypeProc(IntPtr module, IntPtr type, IntPtr param);
    private delegate bool EnumNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    public static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: PeResources <file>");
            return 2;
        }

        IntPtr module = LoadLibraryEx(args[0], IntPtr.Zero, 0x22);
        if (module == IntPtr.Zero)
        {
            Console.Error.WriteLine("LoadLibraryEx failed: " + Marshal.GetLastWin32Error());
            return 1;
        }

        try
        {
            EnumResourceTypes(module, delegate(IntPtr handle, IntPtr type, IntPtr ignored)
            {
                EnumResourceNames(handle, type, delegate(IntPtr owner, IntPtr resourceType,
                    IntPtr name, IntPtr unused)
                {
                    IntPtr resource = FindResource(owner, name, resourceType);
                    uint size = resource == IntPtr.Zero ? 0 : SizeofResource(owner, resource);
                    Console.WriteLine("{0}\t{1}\t{2}", Label(resourceType), Label(name), size);
                    return true;
                }, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            FreeLibrary(module);
        }
        return 0;
    }

    private static string Label(IntPtr value)
    {
        long raw = value.ToInt64();
        if ((raw >> 16) == 0) return "#" + raw;
        return Marshal.PtrToStringUni(value);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EnumResourceTypes(IntPtr module, EnumTypeProc callback, IntPtr param);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type,
        EnumNameProc callback, IntPtr param);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);
}
