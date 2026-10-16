using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>The standard user in the interactive console session, real environment (no seam): step 1 passes.</summary>
[TestClass]
public sealed class ConsoleTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("console");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Console)]
    public void TheRealEnvironmentIsAnInteractiveStandardUser()
    {
        var facts = SystemEnvironmentFacts.Instance;
        Assert.IsTrue(facts.SessionId > 0, "session id");
        Assert.IsTrue(facts.HasInteractiveSid, "INTERACTIVE SID");
        Assert.IsFalse(facts.IsElevated, "elevated");
        Assert.IsFalse(facts.IsSystem, "SYSTEM");
    }

    [TestMethod]
    [TestCategory(Cat.Console)]
    public void APlainFileIsApprovedOnTheRealEnvironment()
    {
        var file = folder.File("plain.txt", 100);
        Expect.Approved(Gates.Real().Check(Gates.Request(file)), Win32.FinalPath(file));
    }

    [TestMethod]
    [TestCategory(Cat.Console)]
    public void ALaterCheckRefusesOnTheRealEnvironment()
        => Expect.Refused(Gates.Real().Check(new GateRequest(folder.Combine("missing.txt"), Gates.AnyStamp)), RefusalReason.AlreadyGone);

    [TestMethod]
    [TestCategory(Cat.Console)]
    public void ARestoreDestinationIsApprovedOnTheRealEnvironment()
        => Expect.Approved(Gates.Real().CheckRestoreDestination(folder.Combine("restored.txt")));
}
