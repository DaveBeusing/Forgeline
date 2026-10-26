using ForgeLine.Core;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests.RenderParallelization;

public sealed class RenderPublicationBoundaryTests
{
    [Fact]
    public void RetainedReaderKeepsIndependentArraysAcrossCallerMutationAndNextPublication()
    {
        EntityId[] selected = [new(1, 1)];
        ClientRenderFrame caller = default(ClientRenderFrame) with
        {
            GameplayLines = new DebugLine[1], GameplayLabels = new DebugLabel[1],
            DebugLines = new DebugLine[1], DebugLabels = new DebugLabel[1],
            InformationLayer = default(RtsInformationLayerView) with { SelectedEntities = selected }
        };
        var publication = new ClientRenderPublication();
        Assert.Throws<InvalidOperationException>(() => publication.StoreCopied(caller));
        var copied = ClientRenderPublication.Copy(caller);
        lock (publication.Gate) publication.StoreCopied(copied);
        ClientRenderFrame retained = publication.ReadLatest()!.Value;
        selected[0] = new(2, 1);
        lock (publication.Gate) publication.StoreCopied(ClientRenderPublication.Copy(caller));
        Assert.Equal(new EntityId(1, 1), retained.InformationLayer.SelectedEntities[0]);
        Assert.Equal(new EntityId(2, 1), publication.ReadLatest()!.Value.InformationLayer.SelectedEntities[0]);
        Assert.NotSame(caller.GameplayLines, retained.GameplayLines);
        Assert.NotSame(caller.GameplayLabels, retained.GameplayLabels);
        Assert.NotSame(caller.DebugLines, retained.DebugLines);
        Assert.NotSame(caller.DebugLabels, retained.DebugLabels);
    }
    [Fact]
    public void BorrowedDrawsReuseOnlyUnchangedCopiesAndNeverMutateRetainedFrames()
    {
        var gameplay = new DebugDraw { Enabled = true };
        var debug = new DebugDraw { Enabled = true };
        EntityId[] selection = [new(17, 2)];
        gameplay.Line(System.Numerics.Vector3.Zero, System.Numerics.Vector3.One, System.Numerics.Vector4.One);
        var first = ClientRenderPublication.CopyDraws(default, gameplay, debug, selection);
        var unchanged = ClientRenderPublication.CopyDraws(default, gameplay, debug, selection);
        Assert.Same(first.GameplayLines, unchanged.GameplayLines);
        Assert.Same(first.InformationLayer.SelectedEntities, unchanged.InformationLayer.SelectedEntities);
        gameplay.Clear();
        gameplay.Line(System.Numerics.Vector3.One, System.Numerics.Vector3.Zero, System.Numerics.Vector4.Zero);
        selection[0] = new(17, 3);
        var changed = ClientRenderPublication.CopyDraws(default, gameplay, debug, selection);
        Assert.Equal(System.Numerics.Vector3.Zero, first.GameplayLines[0].Start);
        Assert.Equal(new EntityId(17, 2), first.InformationLayer.SelectedEntities[0]);
        Assert.Equal(System.Numerics.Vector3.One, changed.GameplayLines[0].Start);
        Assert.Equal(new EntityId(17, 3), changed.InformationLayer.SelectedEntities[0]);
    }

    [Fact]
    public void PublicationBackpressureTracksReplacementAndConsumptionWithoutDroppingLatestState()
    {
        var mailbox = new ClientRenderPublication();
        Assert.True(mailbox.NeedsFramePublication);
        lock (mailbox.Gate) mailbox.StoreCopied(default);
        Assert.False(mailbox.NeedsFramePublication);
        lock (mailbox.Gate) mailbox.StoreCopied(default(ClientRenderFrame) with { ViewportWidth = 640 });
        Assert.Equal(1UL, mailbox.Metrics.Replaced);
        Assert.Equal(640, mailbox.ReadLatest()!.Value.ViewportWidth);
        Assert.True(mailbox.NeedsFramePublication);
        Assert.Equal(1UL, mailbox.Metrics.Consumed);
        Assert.False(mailbox.Metrics.Pending);
    }
}
