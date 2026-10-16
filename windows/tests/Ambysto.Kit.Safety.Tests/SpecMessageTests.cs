using System;
using System.Linq;
using Ambysto.Kit.Safety.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Safety.Tests;

/// <summary>The refusal messages are the spec's table (docs/spec/safety.md, "Windows: refusal reasons"), read in place.</summary>
[TestClass]
public sealed class SpecMessageTests
{
    /// <summary>Helper self-check (passes before the implementation exists): the table and the enum cover each other.</summary>
    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void TheSpecTableCoversEveryReason()
    {
        var reasons = Enum.GetValues<RefusalReason>();
        Assert.HasCount(reasons.Length, SpecTable.All);
        foreach (var reason in reasons)
        {
            Assert.IsTrue(SpecTable.All.ContainsKey(reason), $"no table row for {reason}");
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void EveryFixedMessageIsTheSpecsWording()
    {
        foreach (var reason in Enum.GetValues<RefusalReason>().Where(r => r is not RefusalReason.NotAllowed and not RefusalReason.CouldNotVerify))
        {
            Assert.AreEqual(SpecTable.MessageFor(reason), reason.Message(), reason.ToString());
        }
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void CouldNotVerifyMessageIsTheFixedPartOfTheTemplate()
    {
        var template = SpecTable.All[RefusalReason.CouldNotVerify];
        var message = RefusalReason.CouldNotVerify.Message();
        Assert.IsFalse(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("<check>", message);
        Assert.StartsWith(message.TrimEnd(), template);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AGateResultCarriesTheSpecMessageOrTheAppsText()
    {
        Assert.AreEqual(SpecTable.MessageFor(RefusalReason.PathTooLong), new GateResult(RefusalReason.PathTooLong, null).Message);
        Assert.AreEqual(SpecTable.MessageFor(RefusalReason.Protected), new GateResult(RefusalReason.Protected, "some detail").Message);
        Assert.AreEqual("the app's text", new GateResult(RefusalReason.NotAllowed, "the app's text").Message);
        Assert.AreEqual(SpecTable.CouldNotVerifyMessage("volume"), new GateResult(RefusalReason.CouldNotVerify, "volume").Message);
    }

    [TestMethod]
    [TestCategory(Cat.Session0)]
    public void AnApprovedResultHasNoMessage()
    {
        var facts = new ItemFacts(@"C:\x", @"C:\Users\u", @"C:\Users", 1, 1, [], false);
        var approved = new GateResult(new ApprovedItem(@"C:\x", Gates.AnyStamp, 0, 0, 1, facts));
        Assert.IsTrue(approved.IsApproved);
        Assert.IsNull(approved.Reason);
        Assert.IsNull(approved.Message);
    }
}
