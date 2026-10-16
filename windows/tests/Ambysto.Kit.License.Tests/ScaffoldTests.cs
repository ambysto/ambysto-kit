using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Licensing.Tests;

[TestClass]
public sealed class ScaffoldTests
{
    [TestMethod]
    public void TheSpecVectorsAreFoundInPlace()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(SpecFiles.Path("docs/spec/license-test-vectors.json")));
        Assert.AreEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.AreEqual(35, doc.RootElement.GetArrayLength());
    }
}
