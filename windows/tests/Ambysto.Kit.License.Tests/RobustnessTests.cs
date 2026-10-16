using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Kit.Licensing.Tests;

/// <summary>Fuzzing, timing, threads and references: intent criteria 2, 7 and 8.</summary>
[TestClass]
public sealed class RobustnessTests
{
    private static readonly Vector[] Valid = Vector.All.Where(v => v.ExpectValid).ToArray();

    [TestMethod]
    public void RandomStringsNeverVerify()
    {
        const string pool = "0123456789ABCDEFGHJKMNPQRSTVWXYZabcdefghjkmnpqrstvwxyzOILUoilu-  \t\r\nAMB#_ ０٠ıKé中\U0001F600";
        var random = new Random(20261016);
        var verifier = TestKey.Verifier(Valid[0]);
        for (var i = 0; i < 100_000; i++)
        {
            var length = random.Next(0, 260);
            var chars = new char[length];
            for (var j = 0; j < length; j++)
            {
                chars[j] = pool[random.Next(pool.Length)];
            }
            // A random prefix of a valid key sometimes: it reaches the later steps more often.
            var text = random.Next(4) == 0 ? "AMB-04002-0G30G-" + new string(chars) : new string(chars);
            var result = verifier.Verify(text);
            Assert.IsFalse(result.IsValid, text);
            Assert.IsTrue(Enum.IsDefined(result.Reason!.Value), text);
        }
    }

    [TestMethod]
    public void EverySingleByteChangeOfAValidKeyIsRefused()
    {
        Assert.HasCount(15, Valid);
        var checkedCount = 0;
        foreach (var vector in Valid)
        {
            var verifier = TestKey.Verifier(vector);
            var bytes = CrockfordText.Decode(vector.Text)!;
            CollectionAssert.AreEqual(bytes, CrockfordText.Decode(CrockfordEncoder.Encode(bytes)), vector.Name);   // the test encoder round-trips
            for (var index = 0; index < bytes.Length; index++)
            {
                foreach (var flip in new byte[] { 0x01, 0x80, 0xFF })
                {
                    var changed = (byte[])bytes.Clone();
                    changed[index] ^= flip;
                    var result = verifier.Verify(CrockfordEncoder.Encode(changed));
                    Assert.IsFalse(result.IsValid, $"{vector.Name}: byte {index} ^ {flip:X2}");
                    checkedCount++;
                }
            }
        }
        Assert.AreEqual(Valid.Sum(v => CrockfordText.Decode(v.Text)!.Length * 3), checkedCount);
    }

    [TestMethod]
    public void MutatedTextsNeverThrow()
    {
        var random = new Random(16102026);
        const string pool = "0123456789ABCDEFGHJKMNPQRSTVWXYZOILU- ０";
        for (var i = 0; i < 10_000; i++)
        {
            var vector = Vector.All[random.Next(Vector.All.Count)];
            var text = new List<char>(vector.Text);
            for (var edits = random.Next(1, 4); edits > 0; edits--)
            {
                var at = text.Count == 0 ? 0 : random.Next(text.Count);
                switch (random.Next(3))
                {
                    case 0: text.Insert(at, pool[random.Next(pool.Length)]); break;
                    case 1 when text.Count > 0: text.RemoveAt(at); break;
                    case 2 when text.Count > 0: text[at] = pool[random.Next(pool.Length)]; break;
                }
            }
            var result = TestKey.Verifier(vector).Verify(new string(text.ToArray()));
            Assert.IsTrue(result.IsValid || Enum.IsDefined(result.Reason!.Value));
        }
    }

    [TestMethod]
    public void VerifyingOneKeyTakesUnderTenMilliseconds()
    {
        var vector = Valid[0];
        var verifier = TestKey.Verifier(vector);
        for (var i = 0; i < 100; i++)
        {
            Assert.IsTrue(verifier.Verify(vector.Text).IsValid);
        }
        var times = new double[1_000];
        for (var i = 0; i < times.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            verifier.Verify(vector.Text);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(times);
        var median = times[times.Length / 2];
        Console.WriteLine($"median {median:F3} ms, p99 {times[989]:F3} ms");
        Assert.IsLessThan(10.0, median);
    }

    [TestMethod]
    public void ResultsAreTheSameFromEightThreads()
    {
        var work = Enumerable.Range(0, 1_000).Select(i => Vector.All[i % Vector.All.Count]).ToArray();
        var expected = work.Select(v => Describe(TestKey.Verifier(v).Verify(v.Text))).ToArray();
        var verifiers = Vector.All.ToDictionary(v => v.Name, TestKey.Verifier);
        var actual = new string[work.Length];
        Parallel.For(0, work.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 },
                     i => actual[i] = Describe(verifiers[work[i].Name].Verify(work[i].Text)));
        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void TheLibraryReferencesOnlyTheRuntimeAndNoNetworking()
    {
        var runtime = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var name in typeof(LicenseVerifier).Assembly.GetReferencedAssemblies())
        {
            Assert.IsFalse(name.Name!.StartsWith("System.Net", StringComparison.Ordinal), name.Name);
            var location = Assembly.Load(name).Location;
            Assert.IsTrue(location.StartsWith(runtime, StringComparison.OrdinalIgnoreCase), $"{name.Name} from {location}");
        }
    }

    private static string Describe(LicenseResult result) => result.IsValid ? result.License.KeyId : result.Reason!.Value.ToString();
}
