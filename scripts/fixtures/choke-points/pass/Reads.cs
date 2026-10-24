// Reads only: attribute opens, listing, registry reads, process listing and read-only process handle flags.
internal static class Reads
{
    internal const uint FILE_READ_ATTRIBUTES = 0x0080;
    internal const uint READ_CONTROL = 0x00020000;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_VM_READ = 0x0010;
    internal static bool Exists(string p) => System.IO.File.Exists(p) && System.IO.Directory.Exists(p);
    internal static object? Value() => Microsoft.Win32.Registry.CurrentUser.OpenSubKey("x", writable: false)?.GetValue("y");
    internal static int Count() => System.Diagnostics.Process.GetProcesses().Length;
    // File.Delete and TerminateProcess in a comment is not a use.
}
