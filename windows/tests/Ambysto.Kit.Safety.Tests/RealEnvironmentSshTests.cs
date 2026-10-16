using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>The standard user over SSH (session 0), real environment: step 1 refuses as not interactive (intent Outcome 6).</summary>
[TestClass]
public sealed class RealEnvironmentSshTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("ssh");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.RealEnvironmentSsh)]
    public void TheRealEnvironmentIsSession0AndNotElevated()
    {
        var facts = SystemEnvironmentFacts.Instance;
        Assert.AreEqual(0, facts.SessionId);
        Assert.IsFalse(facts.IsElevated, "elevated");
        Assert.IsFalse(facts.IsSystem, "SYSTEM");
    }

    [TestMethod]
    [TestCategory(Cat.RealEnvironmentSsh)]
    public void TheGateRefusesAsNotInteractive()
    {
        var file = folder.File("plain.txt", 100);
        Expect.Refused(Gates.Real().Check(Gates.Request(file)), RefusalReason.NotInteractive);
    }

    [TestMethod]
    [TestCategory(Cat.RealEnvironmentSsh)]
    public void PuttingBackIsRefusedAsNotInteractive()
        => Expect.Refused(Gates.Real().CheckRestoreDestination(folder.Combine("restored.txt")), RefusalReason.NotInteractive);
}
