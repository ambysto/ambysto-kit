using System;
using System.IO;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>G02, "Windows: failing closed": "Every check that cannot establish its fact (... an unreadable setting)
/// refuses ... with Could not verify". With the machine policy "maximum Recycle Bin size" set by
/// windows/tests/admin/setup-size-policy.ps1 (its own suite: the policy turns every approval on the system drive into a
/// refusal), a small file on the system drive is "Could not verify" with its check named. The control is every
/// approval of the Session0 suite, which runs without the policy (a standard user cannot remove it mid-run).</summary>
[TestClass]
public sealed class AdminSizePolicyTests
{
    private TempFolder folder = null!;

    [TestInitialize]
    public void Create() => folder = TempFolder.InProfile("sizepolicy");

    [TestCleanup]
    public void Delete() => folder.Dispose();

    [TestMethod]
    [TestCategory(Cat.AdminSizePolicy)]
    public void ARecycleBinSizePolicyMakesTheGateFailClosed()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer");
        Assert.IsNotNull(key?.GetValue("RecycleBinSize"), "precondition: setup-size-policy.ps1 did not set RecycleBinSize");
        var file = folder.File("small.bin", 4096);
        Expect.CouldNotVerify(Gates.Check(file, policy: FakeRecycleBinPolicy.Allowed));
        var dir = folder.Dir("sub");
        folder.File(@"sub\small.bin", 4096);
        Expect.CouldNotVerify(Gates.Check(dir, policy: FakeRecycleBinPolicy.Allowed));
    }
}
