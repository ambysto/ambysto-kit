using System;

namespace Ambysto.Kit.Safety.Internal;

/// <summary>Ends a gate run with a refusal. Internal only; the public API returns a <see cref="GateResult"/>.</summary>
internal sealed class Refusal : Exception
{
    internal Refusal(RefusalReason reason, string? detail = null) : base(reason.ToString()) { Reason = reason; Detail = detail; }

    internal RefusalReason Reason { get; }
    internal string? Detail { get; }

    /// <summary>Runs one check; any failure other than a refusal ends the run with "Could not verify: check" (failing closed).</summary>
    internal static T Verify<T>(string check, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Refusal)
        {
            throw;
        }
        catch (NotImplementedException)
        {
            throw;
        }
#pragma warning disable CA1031 // failing closed: every error of a check is a refusal, never a pass
        catch (Exception)
        {
            throw new Refusal(RefusalReason.CouldNotVerify, check);
        }
#pragma warning restore CA1031
    }
}
