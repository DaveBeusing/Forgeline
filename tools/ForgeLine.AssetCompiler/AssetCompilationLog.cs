using System.Diagnostics;
using System.Text.Json;

namespace ForgeLine.AssetCompiler;

internal sealed class AssetCompilationLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter? _file;
    private readonly Timer _heartbeat;
    private readonly long _origin = Stopwatch.GetTimestamp();
    private AssetCompilationProgress? _active;
    private long _lastTimestamp;
    private int _sequence;
    private bool _disposed;

    internal AssetCompilationLog(string? path)
    {
        if (path is not null)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            _file = new StreamWriter(fullPath, append: false) { AutoFlush = true };
            Console.WriteLine($"[asset:progress] log=\"{fullPath}\" pid={Environment.ProcessId}");
        }
        _heartbeat = new Timer(_ => Heartbeat(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    internal void Write(AssetCompilationProgress progress)
    {
        lock (_gate)
        {
            if (_disposed) return;
            progress = progress with { ElapsedMilliseconds = Stopwatch.GetElapsedTime(_origin).TotalMilliseconds };
            _active = progress.State == "started" ? progress : null;
            _lastTimestamp = Stopwatch.GetTimestamp();
            Emit(progress);
        }
    }

    private void Emit(AssetCompilationProgress progress)
    {
        Console.WriteLine($"[asset:progress] stage={progress.Stage} state={progress.State} " +
            $"id={progress.AssetId ?? "-"} elapsedMs={progress.ElapsedMilliseconds:F1} " +
            $"stageMs={progress.StageMilliseconds:F1} managedBytes={progress.ManagedBytes} " +
            $"workingSetBytes={progress.WorkingSetBytes} privateBytes={progress.PrivateBytes} " +
            $"peakWorkingSetBytes={progress.PeakWorkingSetBytes} memorySampleAgeMs={progress.MemorySampleAgeMilliseconds:F1} source=\"{progress.SourcePath}\" {progress.Detail}");
        _file?.WriteLine(JsonSerializer.Serialize(new
        {
            TimestampUtc = DateTimeOffset.UtcNow, ProcessId = Environment.ProcessId,
            Sequence = ++_sequence, Progress = progress
        }));
    }

    private void Heartbeat()
    {
        lock (_gate)
        {
            if (_disposed || _active is null) return;
            double since = Stopwatch.GetElapsedTime(_lastTimestamp).TotalMilliseconds;
            using Process process = Process.GetCurrentProcess();
            Emit(_active with
            {
                State = "heartbeat", ElapsedMilliseconds = _active.ElapsedMilliseconds + since,
                StageMilliseconds = _active.StageMilliseconds + since,
                ManagedBytes = GC.GetTotalMemory(false), WorkingSetBytes = process.WorkingSet64,
                PrivateBytes = process.PrivateMemorySize64, PeakWorkingSetBytes = process.PeakWorkingSet64,
                MemorySampleAgeMilliseconds = 0
            });
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _heartbeat.Dispose();
            _file?.Dispose();
        }
    }
}
