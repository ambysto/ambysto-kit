using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>The refusal table of docs/spec/safety.md ("Windows: refusal reasons"), read in place: the message a test
/// expects comes from the spec, never from a copy, so the two cannot drift.</summary>
internal static class SpecTable
{
    private const string Heading = "## Windows: refusal reasons";

    private static readonly Dictionary<string, RefusalReason> Keys = new(StringComparer.Ordinal)
    {
        ["not an interactive session"] = RefusalReason.NotInteractive,
        ["elevated or SYSTEM"] = RefusalReason.Elevated,
        ["gone"] = RefusalReason.AlreadyGone,
        ["link or special file"] = RefusalReason.LinkOrSpecialFile,
        ["contains a link or special file"] = RefusalReason.ContainsLinkOrSpecialFile,
        ["path form"] = RefusalReason.UnsupportedPathForm,
        ["volume"] = RefusalReason.UnsupportedVolume,
        ["changed"] = RefusalReason.ChangedSinceScan,
        ["Recycle Bin off"] = RefusalReason.RecycleBinOff,
        ["Recycle Bin full"] = RefusalReason.RecycleBinFull,
        ["too large"] = RefusalReason.TooLargeForRecycleBin,
        ["path too long"] = RefusalReason.PathTooLong,
        ["cloud file"] = RefusalReason.CloudFile,
        ["cloud-sync folder"] = RefusalReason.CloudSyncFolder,
        ["root or profile"] = RefusalReason.RootOrProfile,
        ["protected"] = RefusalReason.Protected,
        ["system-owned"] = RefusalReason.SystemOwned,
        ["destination exists"] = RefusalReason.RestoreDestinationExists,
        ["destination folder gone"] = RefusalReason.RestoreFolderMissing,
        ["not allowed"] = RefusalReason.NotAllowed,
        ["could not verify"] = RefusalReason.CouldNotVerify,
    };

    private static readonly Lazy<IReadOnlyDictionary<RefusalReason, string>> Rows = new(Parse);

    /// <summary>Every row of the table: the reason and the message cell (quotes removed; "the app's own text" for
    /// not allowed; "Could not verify: &lt;check&gt;" for could not verify).</summary>
    public static IReadOnlyDictionary<RefusalReason, string> All => Rows.Value;

    /// <summary>The fixed message of a reason that has one (every reason except not allowed and could not verify).</summary>
    public static string MessageFor(RefusalReason reason)
    {
        if (reason is RefusalReason.NotAllowed or RefusalReason.CouldNotVerify)
        {
            throw new ArgumentException("no fixed message in the spec", nameof(reason));
        }
        return All[reason];
    }

    /// <summary>The could-not-verify message for a named check, from the spec's template.</summary>
    public static string CouldNotVerifyMessage(string check) => All[RefusalReason.CouldNotVerify].Replace("<check>", check, StringComparison.Ordinal);

    private static Dictionary<RefusalReason, string> Parse()
    {
        var lines = File.ReadAllLines(RepoFiles.Path("docs/spec/safety.md"));
        var start = Array.IndexOf(lines, Heading);
        if (start < 0)
        {
            throw new InvalidDataException($"'{Heading}' not found in docs/spec/safety.md");
        }
        var rows = new Dictionary<RefusalReason, string>();
        foreach (var line in lines.Skip(start + 1))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }
            if (!line.StartsWith('|'))
            {
                continue;
            }
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != 2 || cells[0] == "Reason" || cells[0].StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }
            if (!Keys.TryGetValue(cells[0], out var reason))
            {
                throw new InvalidDataException($"the spec's refusal table has a row the tests do not know: '{cells[0]}'");
            }
            var message = cells[1];
            if (message.Length >= 2 && message[0] == '"' && message[^1] == '"')
            {
                message = message[1..^1];
            }
            rows[reason] = message;
        }
        return rows;
    }
}
