using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests.SnapshotPublication;

public sealed class DefensivePublicationTests
{
    [Fact]
    public void IdenticalInputsShareOnlyOwnedCopiesAndEveryCallerMutationInvalidatesThem()
    {
        ClientRenderPublication.ResetThreadCopies();
        var original = Frame();
        var retained = ClientRenderPublication.Copy(original);
        var repeated = ClientRenderPublication.Copy(original);
        Assert.NotSame(original.GameplayLines, retained.GameplayLines);
        Assert.NotSame(original.GameplayLabels, retained.GameplayLabels);
        Assert.NotSame(original.InformationLayer.SelectedEntities, retained.InformationLayer.SelectedEntities);
        Assert.Same(retained.GameplayLines, repeated.GameplayLines);
        Assert.Same(retained.GameplayLabels, repeated.GameplayLabels);
        Assert.Same(retained.DebugLines, repeated.DebugLines);
        Assert.Same(retained.DebugLabels, repeated.DebugLabels);
        Assert.Same(retained.InformationLayer.SelectedEntities, repeated.InformationLayer.SelectedEntities);
        original.GameplayLines[0] = default;
        original.GameplayLabels[0] = new(Vector3.Zero, "changed", Vector4.Zero);
        original.DebugLines[0] = default;
        original.DebugLabels[0] = default;
        ((EntityId[])original.InformationLayer.SelectedEntities)[0] = new(1, 2);
        var changed = ClientRenderPublication.Copy(original);
        Assert.NotEqual(retained.GameplayLines[0], changed.GameplayLines[0]);
        Assert.Equal("status", retained.GameplayLabels[0].Text);
        Assert.Equal(new EntityId(1, 1), retained.InformationLayer.SelectedEntities.Single());
        Assert.Equal(new EntityId(1, 2), changed.InformationLayer.SelectedEntities.Single());
        Assert.NotSame(retained.DebugLines, changed.DebugLines);
        Assert.NotSame(retained.DebugLabels, changed.DebugLabels);
        var copiedAgain = ClientRenderPublication.Copy(retained);
        Assert.NotSame(retained.GameplayLines, copiedAgain.GameplayLines);
    }

    [Fact]
    public void WarmUnchangedPublicationAllocatesNothingAndCacheDropsOversizedInputs()
    {
        ClientRenderPublication.ResetThreadCopies();
        var frame = Frame();
        for (int i = 0; i < 1024; i++) ClientRenderPublication.Copy(frame);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) ClientRenderPublication.Copy(frame);
        Assert.Equal(bytes, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(5, ClientRenderPublication.CurrentThreadCachedElements);
        frame = frame with { GameplayLabels = [new(Vector3.Zero, new string('x', 16385), Vector4.One)],
            DebugLines = new DebugLine[2000] };
        var first = ClientRenderPublication.Copy(frame);
        var next = ClientRenderPublication.Copy(frame);
        Assert.NotSame(first.GameplayLabels, next.GameplayLabels);
        Assert.NotSame(first.DebugLines, next.DebugLines);
        Assert.InRange(ClientRenderPublication.CurrentThreadCachedElements, 0, 3);
    }

    [Fact]
    public async Task ConcurrentProducerAndBlockedReaderKeepOldFramesAcrossChangesAndShutdown()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ClientRenderFrame? retained = null;
        using var host = new ClientRenderHost(frame =>
        {
            retained ??= frame;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        });
        var caller = Frame();
        Assert.True(host.Publish(caller));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        try
        {
            await Task.Run(() =>
            {
                for (int i = 2; i < 128; i++)
                {
                    ((EntityId[])caller.InformationLayer.SelectedEntities)[0] = new(1, (uint)i);
                    Assert.True(host.Publish(caller));
                }
            }, TestContext.Current.CancellationToken);
            Assert.Equal(new EntityId(1, 1), retained!.Value.InformationLayer.SelectedEntities.Single());
        }
        finally { release.Set(); }
        host.Dispose();
        Assert.Equal(new EntityId(1, 1), retained!.Value.InformationLayer.SelectedEntities.Single());
    }

    [Fact]
    public void FailedCaptureCannotReplaceLatestFrameOrStrandTheMailboxGate()
    {
        var publication = new ClientRenderPublication();
        var original = ClientRenderPublication.Copy(Frame());
        lock (publication.Gate) publication.StoreCopied(original);
        var invalid = Frame() with { GameplayLabels = null! };
        Assert.Throws<ArgumentNullException>(() => ClientRenderPublication.Copy(invalid));
        Assert.Same(original.GameplayLines, publication.ReadLatest()!.Value.GameplayLines);
        lock (publication.Gate) publication.StoreCopied(ClientRenderPublication.Copy(Frame()));
        Assert.Equal("status", original.GameplayLabels[0].Text);
    }

    private static ClientRenderFrame Frame() => default(ClientRenderFrame) with
    {
        GameplayLines = [new(Vector3.One, Vector3.UnitX, Vector4.One)],
        GameplayLabels = [new(Vector3.One, "status", Vector4.One)],
        DebugLines = [new(Vector3.One, Vector3.UnitY, Vector4.One)],
        DebugLabels = [new(Vector3.One, "debug", Vector4.One)],
        InformationLayer = default(RtsInformationLayerView) with { SelectedEntities = new EntityId[] { new(1, 1) } }
    };
}
