using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests.SnapshotPublication;

public sealed class HostPublicationProfileTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealMailboxPublicationKeepsRetainedReaderDuringStableAndChangingPayloads(bool changing)
    {
        string? output = Environment.GetEnvironmentVariable("FORGELINE_HOST_PROFILE_OUTPUT");
        int samples = string.IsNullOrEmpty(output) ? 16 : 256;
        int warmupBatches = string.IsNullOrEmpty(output) ? 128 : 2048;
        const int operations = 32;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ClientRenderFrame retained = default;
        using var host = new ClientRenderHost(frame =>
        {
            retained = frame;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        });
        var caller = default(ClientRenderFrame) with
        {
            GameplayLines = Enumerable.Repeat(new DebugLine(Vector3.One, Vector3.UnitX, Vector4.One), 128).ToArray(),
            GameplayLabels = Enumerable.Repeat(new DebugLabel(Vector3.One, "ready", Vector4.One), 32).ToArray(),
            DebugLines = Enumerable.Repeat(new DebugLine(Vector3.One, Vector3.UnitY, Vector4.One), 32).ToArray(),
            DebugLabels = Enumerable.Repeat(new DebugLabel(Vector3.One, "diagnostic", Vector4.One), 8).ToArray(),
            InformationLayer = default(RtsInformationLayerView) with
            { SelectedEntities = Enumerable.Range(1, 8).Select(i => new EntityId((uint)i, 1)).ToArray() }
        };
        Assert.True(host.Publish(caller));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var observations = new Observation[samples];
        long warmupStarted = Stopwatch.GetTimestamp();
        long warmupBefore = GC.GetAllocatedBytesForCurrentThread();
        long warmupAllocated = 0;
        double warmupMilliseconds = 0;
        try
        {
            for (int i = -warmupBatches; i < samples; i++)
            {
                if (i == 0)
                {
                    warmupAllocated = GC.GetAllocatedBytesForCurrentThread() - warmupBefore;
                    warmupMilliseconds = Stopwatch.GetElapsedTime(warmupStarted).TotalMilliseconds;
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int operation = 0; operation < operations; operation++)
                {
                    if (changing) caller.GameplayLines[0] = new(new Vector3(i * operations + operation, 0, 0), Vector3.UnitX, Vector4.One);
                    Assert.True(host.Publish(caller));
                }
                double micros = Stopwatch.GetElapsedTime(started).TotalMicroseconds / operations;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                if (i >= 0) observations[i] = new(micros, (double)allocated / operations);
            }
            Assert.Equal(Vector3.One, retained.GameplayLines[0].Start);
            Assert.NotSame(caller.GameplayLines, retained.GameplayLines);
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, $"host-changing-{changing}.json"), JsonSerializer.Serialize(new
                {
                    Source = typeof(ClientRenderHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                    Method = "Actual ClientRenderHost.Publish and mailbox; retained reader blocked deliberately. Fixed 128/32 gameplay and 32/8 debug line/label arrays, eight selected identities. Producer-thread bytes/time include defensive copy, lock, counter and signal; exclude fixture, observer storage, GPU and Present. Not gameplay FPS.",
                    Changing = changing, WarmupBatches = warmupBatches, WarmupAllocatedBytes = warmupAllocated,
                    WarmupMilliseconds = warmupMilliseconds, OperationsPerBatch = operations, Observations = observations
                }, JsonOptions));
            }
        }
        finally { host.RequestStop(); release.Set(); }
        host.Dispose();
        Assert.Equal(Vector3.One, retained.GameplayLines[0].Start);
    }

    private readonly record struct Observation(double MicrosecondsPerPublication, double BytesPerPublication);
}
