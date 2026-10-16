using System;
using System.Collections.Generic;

namespace Ambysto.Kit.Safety.Tests.Support;

/// <summary>The environment seam (Windows step 1) under test control. The default is what the console session gives
/// a standard user: a session above 0, the INTERACTIVE SID, not elevated, not SYSTEM.</summary>
internal sealed record FakeEnvironment(bool IsElevated, bool IsSystem, int SessionId, bool HasInteractiveSid) : IEnvironmentFacts
{
    public static readonly FakeEnvironment Interactive = new(false, false, 1, true);
    public static readonly FakeEnvironment Session0 = new(false, false, 0, false);
    public static readonly FakeEnvironment NoInteractiveSid = new(false, false, 1, false);
    public static readonly FakeEnvironment Elevated = Interactive with { IsElevated = true };
    public static readonly FakeEnvironment System = Interactive with { IsSystem = true };
    public static readonly FakeEnvironment ElevatedInSession0 = new(true, false, 0, false);
}

/// <summary>The policy seam (NoRecycleFiles) under test control.</summary>
internal sealed class FakeRecycleBinPolicy(bool? noRecycleFiles) : IRecycleBinPolicy
{
    public static readonly FakeRecycleBinPolicy Allowed = new(false);
    public static readonly FakeRecycleBinPolicy Disallowed = new(true);
    public static readonly FakeRecycleBinPolicy Unreadable = new(null);

    public bool? NoRecycleFiles { get; } = noRecycleFiles;
}

/// <summary>An app's rule that allows every location: protection must still hold (intent criterion 8).</summary>
internal sealed class AllowAllRule : IAllowedRootRule
{
    public static readonly AllowAllRule Instance = new();

    public AllowedRootDecision Evaluate(ItemFacts item) => AllowedRootDecision.Allow();

    public AllowedRootDecision EvaluateRestore(ItemFacts destinationParent) => AllowedRootDecision.Allow();
}

/// <summary>An app's rule that refuses every location with its own text.</summary>
internal sealed class RefuseAllRule(string message) : IAllowedRootRule
{
    public string Message { get; } = message;

    public AllowedRootDecision Evaluate(ItemFacts item) => AllowedRootDecision.Refuse(Message);

    public AllowedRootDecision EvaluateRestore(ItemFacts destinationParent) => AllowedRootDecision.Refuse(Message);
}

/// <summary>A rule that records what it was asked, to prove when the gate consults the app and with which facts.</summary>
internal sealed class RecordingRule : IAllowedRootRule
{
    public List<ItemFacts> Evaluated { get; } = [];
    public List<ItemFacts> RestoreEvaluated { get; } = [];
    public bool Allow { get; init; } = true;

    public AllowedRootDecision Evaluate(ItemFacts item)
    {
        Evaluated.Add(item);
        return Allow ? AllowedRootDecision.Allow() : AllowedRootDecision.Refuse("recorded refusal");
    }

    public AllowedRootDecision EvaluateRestore(ItemFacts destinationParent)
    {
        RestoreEvaluated.Add(destinationParent);
        return Allow ? AllowedRootDecision.Allow() : AllowedRootDecision.Refuse("recorded restore refusal");
    }
}
