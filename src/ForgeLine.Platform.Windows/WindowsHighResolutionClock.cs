using System.Diagnostics;
using ForgeLine.Core;

namespace ForgeLine.Platform.Windows;

public sealed class WindowsHighResolutionClock : IClock
{
    public long Frequency => Stopwatch.Frequency;

    public long GetTimestamp()
    {
        return Stopwatch.GetTimestamp();
    }

    public TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
    {
        return Stopwatch.GetElapsedTime(startTimestamp, endTimestamp);
    }
}
