using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AseIsql.Desktop;

// The SAP ASE ODBC driver loads SAP's crypto kernel on the first login of each process, and slcryptokernel.dll alone
// spends about 2 s initializing (measured on load, before any network). Loading the driver and its crypto libraries
// on a background thread while the login window is open moves that cost out of the first Connect. Nothing connects:
// the modules are only mapped, and they stay loaded so the driver later reuses them.
internal static class DriverPrewarm
{
    private static readonly string[] Modules = ["slcryptokernel.dll", "sapcrypto.dll", "sybdrvodb64.dll"];
    private const uint SearchDllLoadDir = 0x00000100, SearchDefaultDirs = 0x00001000;
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

    public static Task<TimeSpan?> Running { get; private set; } = Task.FromResult<TimeSpan?>(null);

    public static void Start()
    {
        if (!Environment.Is64BitProcess) return;
        Running = Task.Run<TimeSpan?>(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ODBC\ODBCINST.INI\Adaptive Server Enterprise");
                if (key?.GetValue("Driver") is not string driver || !File.Exists(driver)) return null;
                string directory = Path.GetDirectoryName(driver)!;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                foreach (string module in Modules)
                {
                    string path = Path.Combine(directory, module);
                    if (File.Exists(path)) LoadLibraryEx(path, IntPtr.Zero, SearchDllLoadDir | SearchDefaultDirs);
                }
                return clock.Elapsed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
        });
    }
}
