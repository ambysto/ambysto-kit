using System;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>G30, contract hygiene of the public API (plan Design): a null argument is an ArgumentNullException, never a
/// decision. An empty app-folder entry is left open (ignore or throw: owner to decide) and not asserted here.</summary>
[TestClass]
public sealed class NullArgumentTests
{
    private static readonly AllowAllRule Rule = AllowAllRule.Instance;

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANullPathInARequestThrows()
        => Assert.ThrowsExactly<ArgumentNullException>(() => new GateRequest(null!, Gates.AnyStamp));

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANullContextOrRuleThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new SafetyGate(null!, Rule));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SafetyGate(Gates.Context, null!));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANullRequestOrDestinationThrows()
    {
        var gate = Gates.Seamed();
        Assert.ThrowsExactly<ArgumentNullException>(() => gate.Check(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => gate.CheckRestoreDestination(null!));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANullAppFolderListOrEntryThrows()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => SafetyContext.ForCurrentUser(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => SafetyContext.ForCurrentUser([null!]));
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANullPathToFileFactsThrows()
        => Assert.ThrowsExactly<ArgumentNullException>(() => FileFacts.Read(null!));

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ANullItemToTheInUseCheckerThrows()
        => Assert.ThrowsExactly<ArgumentNullException>(() => new InUseChecker().Check(null!));
}
