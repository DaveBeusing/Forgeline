using System.Diagnostics;

namespace ForgeLine.AssetCompiler;

public sealed record AssetCompilationProgress(
    string Stage, string State, string? AssetId, string? SourcePath, string? Detail,
    double ElapsedMilliseconds, double StageMilliseconds, long ManagedBytes,
    long WorkingSetBytes, long PrivateBytes, long PeakWorkingSetBytes,
    double MemorySampleAgeMilliseconds = 0);

internal sealed class AssetCompilationReporter(Action<AssetCompilationProgress>? observer) : IDisposable
{
    private readonly long _origin = Stopwatch.GetTimestamp();
    private readonly Process? _process = observer is null ? null : Process.GetCurrentProcess();
    private long _sampleTime;
    private long _workingSet;
    private long _privateBytes;
    private long _peakWorkingSet;

    internal T Measure<T>(string stage, Func<T> work, string? id = null,
        string? path = null, string? detail = null)
    {
        long start = Stopwatch.GetTimestamp();
        Report(stage, "started", start, id, path, detail);
        try
        {
            T result = work();
            Report(stage, "completed", start, id, path, detail);
            return result;
        }
        catch (Exception exception)
        {
            Report(stage, "failed", start, id, path, $"{detail} {exception.GetType().Name}: {exception.Message}");
            throw;
        }
    }

    internal void Report(string stage, string state, long start = 0, string? id = null,
        string? path = null, string? detail = null)
    {
        if (observer is null) return;
        long now = Stopwatch.GetTimestamp();
        if (_sampleTime == 0 || Stopwatch.GetElapsedTime(_sampleTime, now).TotalMilliseconds >= 250)
        {
            _process!.Refresh();
            _workingSet = _process.WorkingSet64;
            _privateBytes = _process.PrivateMemorySize64;
            _peakWorkingSet = _process.PeakWorkingSet64;
            _sampleTime = now;
        }
        observer(new AssetCompilationProgress(stage, state, id, path, detail,
            Stopwatch.GetElapsedTime(_origin, now).TotalMilliseconds,
            start == 0 ? 0 : Stopwatch.GetElapsedTime(start, now).TotalMilliseconds,
            GC.GetTotalMemory(false), _workingSet, _privateBytes, _peakWorkingSet,
            Stopwatch.GetElapsedTime(_sampleTime, now).TotalMilliseconds));
    }

    public void Dispose() => _process?.Dispose();
}
