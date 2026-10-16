using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>Windows step 1 through the internal seam (intent criterion 5): elevated, SYSTEM and session-0 callers
/// are refused before any other check; an interactive standard user passes.</summary>
[TestClass]
public sealed class EnvironmentSeamTests
{
    private TempFolder folder = null!;
    private string file = "";

    [TestInitialize]
    public void Create()
    {
        folder = TempFolder.InProfile("env");
        file = folder.File("plain.txt", 100);
    }

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void Session0IsRefusedAsNotInteractive()
        => Expect.Refused(Gates.Check(file, environment: FakeEnvironment.Session0), RefusalReason.NotInteractive);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ASessionAboveZeroWithoutTheInteractiveSidIsRefusedAsNotInteractive()
        => Expect.Refused(Gates.Check(file, environment: FakeEnvironment.NoInteractiveSid), RefusalReason.NotInteractive);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnElevatedTokenIsRefused()
        => Expect.Refused(Gates.Check(file, environment: FakeEnvironment.Elevated), RefusalReason.Elevated);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void SystemIsRefusedAsElevated()
        => Expect.Refused(Gates.Check(file, environment: FakeEnvironment.System), RefusalReason.Elevated);

    /// <summary>The administrator over SSH is both elevated and in session 0; the brief expects Elevated.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void ElevatedInSession0IsRefusedAsElevated()
        => Expect.Refused(Gates.Check(file, environment: FakeEnvironment.ElevatedInSession0), RefusalReason.Elevated);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnInteractiveStandardUserPasses()
        => Expect.Approved(Gates.Check(file, environment: FakeEnvironment.Interactive), Win32.FinalPath(file));

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheEnvironmentIsCheckedBeforeExistence()
        => Expect.Refused(Gates.CheckWith(folder.Combine("missing.txt"), Gates.AnyStamp, environment: FakeEnvironment.Session0), RefusalReason.NotInteractive);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheEnvironmentIsCheckedBeforeThePathForm()
        => Expect.Refused(Gates.CheckWith(MappedDrive.Share + @"\missing.txt", Gates.AnyStamp, environment: FakeEnvironment.Elevated), RefusalReason.Elevated);

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void PuttingBackRunsTheEnvironmentCheckFirst()
    {
        var destination = folder.Combine("restored.txt");
        Expect.Refused(Gates.Seamed(environment: FakeEnvironment.Session0).CheckRestoreDestination(destination), RefusalReason.NotInteractive);
        Expect.Refused(Gates.Seamed(environment: FakeEnvironment.Elevated).CheckRestoreDestination(destination), RefusalReason.Elevated);
        Expect.Refused(Gates.Seamed(environment: FakeEnvironment.System).CheckRestoreDestination(destination), RefusalReason.Elevated);
        Expect.Approved(Gates.Seamed(environment: FakeEnvironment.Interactive).CheckRestoreDestination(destination));
    }
}
