using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ForgeLine.Client;

internal static class RenderOwnerThreadIdentity
{
    internal static object ClockAnchor()
    {
        long before = Stopwatch.GetTimestamp();
        DateTimeOffset utc = DateTimeOffset.UtcNow;
        long after = Stopwatch.GetTimestamp();
        return new { beforeTick = before, utc, afterTick = after, frequencyHz = Stopwatch.Frequency, scope = "Bracketed UTC/QPC anchor; preserve bracket uncertainty for external correlation" };
    }
    [DllImport("kernel32.dll", ExactSpelling = true)]
    internal static extern uint GetCurrentThreadId();
}
