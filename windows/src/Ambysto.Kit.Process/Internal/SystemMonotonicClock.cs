using System;
using Ambysto.Kit.Processes.Native;

namespace Ambysto.Kit.Processes.Internal;

internal sealed class SystemMonotonicClock : IMonotonicClock
{
    public long NowNanoseconds()
    {
        if (!ProcessNativeMethods.QueryUnbiasedInterruptTime(out ulong unbiasedTicks))
        {
            throw new InvalidOperationException("QueryUnbiasedInterruptTime failed.");
        }

        return checked((long)unbiasedTicks * 100L);
    }
}
