using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class StartupCheckpointTests
{
    [Fact]
    public void CancelledSettingsLoadDoesNotCreateUserFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.Startup", Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new ClientSettingsStore(directory)
            .LoadCancellable(cancellation.Token));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void CheckpointPersistsPendingProgressAndFailureBeforeApplicationExit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.Startup", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "startup.json");
        try
        {
            long origin = Stopwatch.GetTimestamp();
            var diagnostics = new StartupDiagnostics(origin, Guid.NewGuid(), 1);
            diagnostics.Mark(StartupPhase.ProcessEntered);
            diagnostics.Mark(StartupPhase.WindowVisible);
            diagnostics.Mark(StartupPhase.NativeBootstrapFirstFrame);
            diagnostics.Begin(StartupPhase.RuntimeAssets);
            using var checkpoint = new StartupCheckpoint(diagnostics, path);
            checkpoint.Flush();
            using (JsonDocument pending = JsonDocument.Parse(File.ReadAllText(path)))
                Assert.Equal("Pending", pending.RootElement.GetProperty("ApplicationReadiness").GetString());
            diagnostics.Finish(new InvalidDataException("Broken manifest"));
            checkpoint.Flush();
            using JsonDocument failed = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("Failed", failed.RootElement.GetProperty("ApplicationReadiness").GetString());
            Assert.False(File.Exists(path + ".tmp"));
            StartupEvent first = diagnostics.Snapshot()[0];
            Assert.Equal(origin, first.Timestamp);
            Assert.Equal(0, first.ElapsedMilliseconds);
            Assert.False(diagnostics.Has(StartupPhase.MainMenuInteractive));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void ReadinessChangesArePersistedEvenWithoutAnotherEvent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.Startup", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "startup.json");
        try
        {
            var diagnostics = new StartupDiagnostics(Stopwatch.GetTimestamp(), Guid.NewGuid(), 1);
            diagnostics.Mark(StartupPhase.ProcessEntered);
            using var checkpoint = new StartupCheckpoint(diagnostics, path);
            checkpoint.Flush();
            int events = diagnostics.Snapshot().Length;
            diagnostics.Finish(new InvalidDataException("Failed before another phase started"));
            Assert.Equal(events, diagnostics.Snapshot().Length);
            checkpoint.Flush();
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("Failed", report.RootElement.GetProperty("ApplicationReadiness").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedAtomicReplacementIsRetriedWithoutAnotherEvent()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ForgeLine.Startup", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "startup.json");
        Directory.CreateDirectory(path); // A directory cannot be replaced by the report file.
        try
        {
            var diagnostics = new StartupDiagnostics(Stopwatch.GetTimestamp(), Guid.NewGuid(), 1);
            diagnostics.Mark(StartupPhase.ProcessEntered);
            using var checkpoint = new StartupCheckpoint(diagnostics, path);
            checkpoint.Flush();
            Assert.False(File.Exists(path));
            Directory.Delete(path);
            checkpoint.Flush();
            using var report = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("Pending", report.RootElement.GetProperty("ApplicationReadiness").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
