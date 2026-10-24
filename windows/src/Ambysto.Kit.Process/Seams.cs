using System.Collections.Generic;

namespace Ambysto.Kit.Processes;

/// <summary>Monotonic clock that does not advance while the system sleeps (test seam; production uses <c>QueryUnbiasedInterruptTime</c>).</summary>
internal interface IMonotonicClock
{
    /// <summary>Current monotonic time in nanoseconds.</summary>
    long NowNanoseconds();
}

/// <summary>Result of opening a process handle and reading its token and metadata for a snapshot entry (test seam).</summary>
/// <param name="ExitedOrMissing">True when <c>OpenProcess</c> failed because the process exited (<c>ERROR_INVALID_PARAMETER</c>) or <c>GetProcessTimes</c> failed.</param>
/// <param name="AccessDenied">True when <c>OpenProcess</c>, <c>OpenProcessToken</c> or <c>GetTokenInformation</c> failed with access denied or another non-exit error.</param>
/// <param name="HandleStartTimeBeforeTicks"><c>CreationTime</c> from <c>GetProcessTimes</c> before reading metadata.</param>
/// <param name="HandleExitTimeBeforeTicks"><c>ExitTime</c> from <c>GetProcessTimes</c> before reading metadata (<c>0</c> while running).</param>
/// <param name="OwnerSid">The process token's user SID string (<c>S-1-5-…</c>), or null when unavailable.</param>
/// <param name="IsElevated">True when <c>TokenElevation</c> reports <c>TokenIsElevated != 0</c>.</param>
/// <param name="IsWow64">True when <c>IsWow64Process2</c> reports a 32-bit WOW64 process (<c>processMachine != 0</c>).</param>
/// <param name="ExecutablePath">Raw DOS executable path from <c>QueryFullProcessImageNameW</c>, or null.</param>
/// <param name="WorkingDirectory">Raw PEB working directory string (before <see cref="ProcessReader"/> strips <c>\\?\</c> or trims a trailing <c>\</c>), or null.</param>
/// <param name="CommandLine">Raw command line string from <c>ProcessCommandLineInformation</c> (before <see cref="ProcessReader"/> splits it into <c>Arguments</c>), or null.</param>
/// <param name="HandleStartTimeAfterTicks"><c>CreationTime</c> from <c>GetProcessTimes</c> after reading metadata.</param>
/// <param name="HandleExitTimeAfterTicks"><c>ExitTime</c> from <c>GetProcessTimes</c> after reading metadata (<c>0</c> while running).</param>
internal sealed record RawProcessHandleResult(
    bool ExitedOrMissing,
    bool AccessDenied,
    long HandleStartTimeBeforeTicks,
    long HandleExitTimeBeforeTicks,
    string? OwnerSid,
    bool IsElevated,
    bool IsWow64,
    string? ExecutablePath,
    string? WorkingDirectory,
    string? CommandLine,
    long HandleStartTimeAfterTicks,
    long HandleExitTimeAfterTicks);

/// <summary>One entry from <c>NtQuerySystemInformation(SystemProcessInformation)</c> paired with its handle read (test seam).</summary>
/// <param name="ProcessId">The process id (<c>UniqueProcessId</c>).</param>
/// <param name="InheritedFromProcessId">The raw parent process id (<c>InheritedFromUniqueProcessId</c>).</param>
/// <param name="Name">Short image name (<c>ImageName</c>).</param>
/// <param name="SessionId">Windows session id.</param>
/// <param name="NumberOfThreads">Thread count in the snapshot (<c>0</c> when already terminated).</param>
/// <param name="SnapshotStartTimeTicks"><c>CreateTime</c> in 100-ns FILETIME ticks from the snapshot.</param>
/// <param name="Counters">Counters captured atomically in the snapshot entry.</param>
/// <param name="HandleResult">Result of opening the process handle and reading metadata.</param>
internal sealed record RawProcessEntry(
    int ProcessId,
    int InheritedFromProcessId,
    string Name,
    int SessionId,
    uint NumberOfThreads,
    long SnapshotStartTimeTicks,
    ProcessCounters Counters,
    RawProcessHandleResult HandleResult);

/// <summary>Source of raw process table entries (test seam; production uses <c>NtQuerySystemInformation(SystemProcessInformation)</c> and Win32 handle queries).</summary>
internal interface IProcessSnapshotSource
{
    /// <summary>The current user's SID string (<c>S-1-5-…</c>).</summary>
    string CurrentUserSid { get; }

    /// <summary>Reads all entries from the system process snapshot.</summary>
    IReadOnlyList<RawProcessEntry> ReadEntries();

    /// <summary>Reads a single process entry by its process id, or null if absent.</summary>
    RawProcessEntry? ReadEntry(int processId);
}

/// <summary>One raw listening TCP row from <c>GetExtendedTcpTable</c> (test seam).</summary>
/// <param name="Family">IPv4 or IPv6.</param>
/// <param name="LocalAddressBytes">4 bytes for IPv4 or 16 bytes for IPv6.</param>
/// <param name="ScopeId">IPv6 scope id in host byte order (<c>0</c> for IPv4 or unscoped IPv6).</param>
/// <param name="RawPortDword">Raw 32-bit <c>dwLocalPort</c> value from the OS table (port is in the low 16 bits in network byte order; upper 16 bits are ignored).</param>
/// <param name="OwningProcessId">The owning process id (<c>dwOwningPid</c>).</param>
internal readonly record struct RawTcpListenerRow(
    SocketAddressFamily Family,
    byte[] LocalAddressBytes,
    uint ScopeId,
    uint RawPortDword,
    int OwningProcessId);

/// <summary>Source of raw TCP listener rows (test seam; production uses <c>GetExtendedTcpTable</c>).</summary>
internal interface ITcpTableSource
{
    /// <summary>Reads all listening TCP rows (IPv4 and IPv6).</summary>
    IReadOnlyList<RawTcpListenerRow> ReadListeners();
}
