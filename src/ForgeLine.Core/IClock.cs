namespace ForgeLine.Core;

public interface IClock
{
    long Frequency { get; }

    long GetTimestamp();

    TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp);
}
