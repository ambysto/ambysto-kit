using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>Gates for the tests: the seams set to an interactive standard user (session-0 runs) or the real
/// environment (console and real-environment runs), an allow-everything rule unless a test brings its own.</summary>
internal static class Gates
{
    private static readonly Lazy<SafetyContext> Shared = new(() => SafetyContext.ForCurrentUser([]));

    /// <summary>One context per test run, as an app would resolve it once per session.</summary>
    public static SafetyContext Context => Shared.Value;

    /// <summary>A gate whose environment is faked (interactive standard user unless given) and whose policy is the
    /// real machine policy unless given.</summary>
    public static SafetyGate Seamed(IAllowedRootRule? rule = null, IEnvironmentFacts? environment = null, IRecycleBinPolicy? policy = null, SafetyContext? context = null)
        => new(context ?? Context, rule ?? AllowAllRule.Instance, environment ?? FakeEnvironment.Interactive, policy ?? SystemRecycleBinPolicy.Instance);

    /// <summary>A gate on the real environment, as an app builds it.</summary>
    public static SafetyGate Real(IAllowedRootRule? rule = null, SafetyContext? context = null) => new(context ?? Context, rule ?? AllowAllRule.Instance);

    /// <summary>What an app records when it shows the item: the stamp read now.</summary>
    public static GateRequest Request(string path) => new(path, StampOf(path));

    public static FileStamp StampOf(string path)
    {
        var facts = FileFacts.Read(path);
        Assert.IsTrue(facts.Succeeded, $"FileFacts.Read({path}) failed: {facts.FailureReason} {facts.Detail}");
        return facts.Stamp!.Value;
    }

    /// <summary>A stamp for requests whose check fails before step 6 (any value serves).</summary>
    public static readonly FileStamp AnyStamp = new(new FileIdentity(1, 1), 1, 1);

    /// <summary>The seamed gate on a path, with the stamp read now.</summary>
    public static GateResult Check(string path, IAllowedRootRule? rule = null, IEnvironmentFacts? environment = null, IRecycleBinPolicy? policy = null, SafetyContext? context = null)
        => Seamed(rule, environment, policy, context).Check(Request(path));

    /// <summary>The seamed gate on a path with a given stamp (for items that cannot or must not be stamped).</summary>
    public static GateResult CheckWith(string path, FileStamp stamp, IEnvironmentFacts? environment = null, IRecycleBinPolicy? policy = null, SafetyContext? context = null)
        => Seamed(null, environment, policy, context).Check(new GateRequest(path, stamp));
}

/// <summary>Assertions on a gate result: exactly one of approval and refusal, the spec's message on a refusal.</summary>
internal static class Expect
{
    public static ApprovedItem Approved(GateResult result, string? canonicalPath = null)
    {
        Assert.IsTrue(result.IsApproved, $"expected approval, got {result.Reason}: {result.Detail}");
        Assert.IsNotNull(result.Approved);
        Assert.IsNull(result.Reason);
        Assert.IsNull(result.Message);
        if (canonicalPath is not null)
        {
            Assert.AreEqual(canonicalPath, result.Approved.CanonicalPath);
        }
        return result.Approved;
    }

    public static void Refused(GateResult result, RefusalReason reason)
    {
        Assert.IsFalse(result.IsApproved, $"expected {reason}, got approval of {result.Approved?.CanonicalPath}");
        Assert.IsNull(result.Approved);
        Assert.AreEqual(reason, result.Reason, $"detail: {result.Detail}");
        if (reason is not RefusalReason.NotAllowed and not RefusalReason.CouldNotVerify)
        {
            Assert.AreEqual(SpecTable.MessageFor(reason), result.Message);
        }
    }

    public static void NotAllowed(GateResult result, string appText)
    {
        Refused(result, RefusalReason.NotAllowed);
        Assert.AreEqual(appText, result.Detail);
        Assert.AreEqual(appText, result.Message);
    }

    /// <summary>Failing closed: the reason, a named check, and the spec's template around it.</summary>
    public static void CouldNotVerify(GateResult result)
    {
        Refused(result, RefusalReason.CouldNotVerify);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Detail), "a could-not-verify refusal names its check");
        Assert.AreEqual(SpecTable.CouldNotVerifyMessage(result.Detail!), result.Message);
    }

    public static void SameIdentity(FileIdentity expected, FileIdentity actual, string what)
    {
        Assert.AreEqual(expected.VolumeSerial, actual.VolumeSerial, what + ": volume serial");
        Assert.AreEqual(expected.FileId, actual.FileId, what + ": file id");
    }

    /// <summary>Some entry equals <paramref name="path"/> or is a folder above it (a row the spec names by vendor, not depth).</summary>
    public static void Covers(IEnumerable<string> list, string path, string what)
    {
        var target = path.TrimEnd('\\');
        foreach (var entry in list)
        {
            var folder = entry.TrimEnd('\\');
            if (string.Equals(folder, target, StringComparison.OrdinalIgnoreCase) || target.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        Assert.Fail($"{what}: no entry covers {path} in [{string.Join(", ", list)}]");
    }

    public static void ContainsPath(IEnumerable<string> list, string path, string what)
    {
        foreach (var entry in list)
        {
            if (string.Equals(entry.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        Assert.Fail($"{what}: {path} not in [{string.Join(", ", list)}]");
    }
}
