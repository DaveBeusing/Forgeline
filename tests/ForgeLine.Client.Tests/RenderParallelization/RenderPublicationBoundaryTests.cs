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
}
