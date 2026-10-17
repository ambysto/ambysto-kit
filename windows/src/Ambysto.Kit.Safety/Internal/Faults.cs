using System;
using System.Threading;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>The test-only fault seam (plan "Stryker follow-up", gaps G20–G23, G25, G26). Every system query the gate and
/// the in-use checker fail closed on passes its result through here, so a test can make one query fail, or change what
/// it reports, for the calls on its own async flow; the code that handles the result then runs exactly as on a real
/// failure. The seam is internal (the tests reach it through InternalsVisibleTo) and holds one injection per async flow;
/// with none, every result passes through unchanged.</summary>
internal static class Faults
{
    internal enum Query
    {
        /// <summary>Opening an item for attributes or security (a hook only: lets a test see every open).</summary>
        Open,
        AttributesByName,
        AttributeTag,
        FileId,
        TimesAndLength,
        FinalPath,
        FileSystem,
        VolumeRoot,
        VolumeName,
        TokenElevation,
        PlaceholderInfo,
        SyncRoot,
        Owner,
        ResourceProtection,
        RecycleBinUsage,
        Size,
        AllocatedSize,
        ListFirst,
        ListNext,
        /// <summary>The attributes and reparse tag of each folder entry (a change only).</summary>
        Listing,
        ProcessesUsingFile,
        RestartManagerStart,
        RestartManagerRegister,
        RestartManagerList,
        KnownFolder,
        /// <summary>Between the item's stamp (step 6) and the identity read with protection (step 11) (a hook only).</summary>
        BeforeProtectionIdentity,
    }

    /// <summary>ERROR_GEN_FAILURE: what a failed Win32 call reports when a failure is injected.</summary>
    internal const int InjectedError = 31;

    /// <summary>E_FAIL.</summary>
    internal const int InjectedHResult = unchecked((int)0x80004005);

    /// <summary>STATUS_UNSUCCESSFUL.</summary>
    internal const int InjectedStatus = unchecked((int)0xC0000001);

    private enum Kind { Fail, Report, Change, Hook }

    private sealed record Injection(Query Query, Kind Kind, Func<string, bool>? Where,
        Func<string, uint, uint, (uint Attributes, uint Tag)>? Change, Action<string>? Hook);

    private static readonly AsyncLocal<Injection?> Active = new();

    /// <summary>Makes <paramref name="query"/> fail (for paths <paramref name="where"/> accepts, or every call).</summary>
    internal static IDisposable Fail(Query query, Func<string, bool>? where = null) => Set(new Injection(query, Kind.Fail, where, null, null));

    /// <summary>Makes a yes-or-no query answer yes (Windows Resource Protection covers the file).</summary>
    internal static IDisposable Report(Query query, Func<string, bool>? where = null) => Set(new Injection(query, Kind.Report, where, null, null));

    /// <summary>Changes the attributes and reparse tag a query reports.</summary>
    internal static IDisposable Change(Query query, Func<string, uint, uint, (uint Attributes, uint Tag)> change) =>
        Set(new Injection(query, Kind.Change, null, change, null));

    /// <summary>Runs <paramref name="hook"/> with the path when the code reaches <paramref name="query"/>.</summary>
    internal static IDisposable Hook(Query query, Action<string> hook) => Set(new Injection(query, Kind.Hook, null, null, hook));

    /// <summary>A Win32 BOOL result; an injected failure returns false with <see cref="InjectedError"/> as the last error.</summary>
    internal static bool Ok(Query query, bool ok, string? path = null)
    {
        if (Hit(query, Kind.Fail, path))
        {
            System.Runtime.InteropServices.Marshal.SetLastPInvokeError(InjectedError);
            return false;
        }
        return ok;
    }

    /// <summary>A raw return value whose failure value is <paramref name="failure"/> (the caller then reads the last error);
    /// an injected failure returns <paramref name="failure"/> with <see cref="InjectedError"/> as the last error, so the
    /// caller's own success test runs on it.</summary>
    internal static uint Value(Query query, uint value, uint failure, string? path = null)
    {
        if (Hit(query, Kind.Fail, path))
        {
            System.Runtime.InteropServices.Marshal.SetLastPInvokeError(InjectedError);
            return failure;
        }
        return value;
    }

    /// <summary>An HRESULT.</summary>
    internal static int Hr(Query query, int hr, string? path = null) => Hit(query, Kind.Fail, path) ? InjectedHResult : hr;

    /// <summary>A Win32 error code returned directly (0 = success).</summary>
    internal static int Code(Query query, int code, string? path = null) => Hit(query, Kind.Fail, path) ? InjectedError : code;

    /// <summary>An NTSTATUS.</summary>
    internal static int Status(Query query, int status, string? path = null) => Hit(query, Kind.Fail, path) ? InjectedStatus : status;

    /// <summary>A yes-or-no answer.</summary>
    internal static bool Yes(Query query, bool answer, string? path = null) => Hit(query, Kind.Report, path) || answer;

    /// <summary>Attributes and reparse tag as reported for <paramref name="path"/>.</summary>
    internal static (uint Attributes, uint Tag) Entry(Query query, string path, uint attributes, uint tag)
    {
        var active = Active.Value;
        return active is { Kind: Kind.Change, Change: not null } && active.Query == query ? active.Change(path, attributes, tag) : (attributes, tag);
    }

    /// <summary>Runs the hook injected for <paramref name="query"/>, if any.</summary>
    internal static void Point(Query query, string path)
    {
        var active = Active.Value;
        if (active is { Kind: Kind.Hook, Hook: not null } && active.Query == query)
        {
            active.Hook(path);
        }
    }

    private static bool Hit(Query query, Kind kind, string? path)
    {
        var active = Active.Value;
        return active is not null && active.Query == query && active.Kind == kind && (active.Where is null || (path is not null && active.Where(path)));
    }

    private static Scope Set(Injection injection)
    {
        var scope = new Scope(Active.Value);
        Active.Value = injection;
        return scope;
    }

    private sealed class Scope(Injection? previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}
