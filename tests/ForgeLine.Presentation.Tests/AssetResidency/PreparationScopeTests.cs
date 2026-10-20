using Xunit;

namespace ForgeLine.Presentation.Tests.AssetResidency;

public sealed class PreparationScopeTests
{
    [Fact]
    public void HostStopCancelsRealStoreConstructionBeforeGpuAllocation()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        using var scope = new AssetPreparationScope(static () => true);
        Assert.Throws<OperationCanceledException>(() => new RuntimeWorldAssetResources(graphics,fixture.Catalog()));
        Assert.Empty(graphics.Buffers); Assert.Empty(graphics.Textures);
    }
    [Fact]
    public void HostStopDuringGpuPublicationRetiresPartialResourcesAndAllowsRestart()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var catalog = fixture.Catalog();
        var resources = new RuntimeWorldAssetResources(graphics,catalog);
        using (var scope = new AssetPreparationScope(() => graphics.Buffers.Count > 0))
            Assert.Throws<OperationCanceledException>(() => resources.PrepareRequiredAssets(CancellationToken.None));
        Assert.Equal(AssetResidencyState.Failed,resources.Residency.State);
        Assert.NotEmpty(graphics.Buffers);
        resources.Dispose();
        Assert.All(graphics.Buffers,b => Assert.True(b.Disposed));
        Assert.All(graphics.Textures,t => Assert.True(t.Disposed));
        using var next = new RuntimeWorldAssetResources(graphics,catalog);
        next.PrepareRequiredAssets(CancellationToken.None);
        Assert.Equal(AssetResidencyState.Ready,next.Residency.State);
    }

    [Fact]
    public void ReconstructedHostDoesNotInheritThePreviousHostsStopState()
    {
        using var old = new AssetPreparationScope(static () => true);
        using (var next = new AssetPreparationScope(static () => false)) AssetPreparationScope.ThrowIfStopped();
        Assert.Throws<OperationCanceledException>(AssetPreparationScope.ThrowIfStopped);
    }
    [Fact]
    public void WrongThreadAndWrongOrderNeverRemoveTheActiveScope()
    {
        var first = new AssetPreparationScope(static () => false);
        var last = new AssetPreparationScope(static () => true);
        Assert.Throws<InvalidOperationException>(first.Dispose);
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(last.Dispose));
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Throws<OperationCanceledException>(AssetPreparationScope.ThrowIfStopped);
        last.Dispose(); first.Dispose();
        AssetPreparationScope.ThrowIfStopped();
    }
}
