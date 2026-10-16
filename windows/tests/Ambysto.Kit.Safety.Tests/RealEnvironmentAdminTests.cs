using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>The administrator account over SSH, elevated, real environment: step 1 refuses as elevated (intent Outcome 6).</summary>
[TestClass]
public sealed class RealEnvironmentAdminTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("admin");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.RealEnvironmentAdmin)]
    public void TheRealEnvironmentIsElevatedAndNotSystem()
    {
        var facts = SystemEnvironmentFacts.Instance;
        Assert.IsTrue(facts.IsElevated, "elevated");
        Assert.IsFalse(facts.IsSystem, "SYSTEM");
    }

    [TestMethod]
    [TestCategory(Cat.RealEnvironmentAdmin)]
    public void TheGateRefusesAsElevated()
    {
        var file = folder.File("plain.txt", 100);
        Expect.Refused(Gates.Real().Check(Gates.Request(file)), RefusalReason.Elevated);
    }

    [TestMethod]
    [TestCategory(Cat.RealEnvironmentAdmin)]
    public void PuttingBackIsRefusedAsElevated()
        => Expect.Refused(Gates.Real().CheckRestoreDestination(folder.Combine("restored.txt")), RefusalReason.Elevated);
}
