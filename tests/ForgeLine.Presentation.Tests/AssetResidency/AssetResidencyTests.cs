using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Presentation.Tests.AssetResidency;

public sealed class AssetResidencyTests
{
    [Fact]
    public void TerrainAndInstanceFacadesShareResourcesUntilTheirLastOwner()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var catalog = fixture.Catalog();
        var terrain = new RuntimeWorldAssetResources(graphics, catalog);
        var instances = new RuntimeWorldAssetResources(graphics, catalog);
        terrain.PrepareRequiredAssets(CancellationToken.None);
        Assert.True(terrain.TryGetMesh("mesh.residency.main", out var first));
        Assert.True(instances.TryGetMesh("mesh.residency.main", out var second));
        Assert.Same(first.VertexBuffer, second.VertexBuffer);
        Assert.Same(terrain.ResolveMaterial("material.residency.main").BaseColorTexture,
            instances.ResolveMaterial("material.residency.main").BaseColorTexture);
        Assert.Equal(2, instances.Residency.Owners);
        terrain.Dispose();
        Assert.All(graphics.Buffers, b => Assert.False(b.Disposed));
        Assert.All(graphics.Textures, t => Assert.False(t.Disposed));
        instances.Dispose();
        Assert.All(graphics.Buffers, b => Assert.True(b.Disposed));
        Assert.All(graphics.Textures, t => Assert.True(t.Disposed));
        instances.Dispose();
    }

    [Fact]
    public void PreparedTexturePublicationUsesTheExistingDeferredSameQueueUploadContract()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        using var resources = new RuntimeWorldAssetResources(graphics,fixture.Catalog());
        resources.PrepareRequiredAssets(CancellationToken.None);
        var texture = resources.ResolveMaterial("material.residency.main").BaseColorTexture;
        Assert.True(texture.Description.DeferUploadWait);
        Assert.Equal(AssetResidencyState.Ready,resources.Residency.State);
    }

    [Fact]
    public void PreparedWarmAccessNeedsNeitherFilesNorAdditionalGpuResourcesOrAllocations()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        using var resources = new RuntimeWorldAssetResources(graphics, fixture.Catalog());
        resources.PrepareRequiredAssets(CancellationToken.None);
        Assert.Equal(AssetResidencyState.Ready, resources.Residency.State);
        fixture.DeleteContainers();
        int buffers = graphics.Buffers.Count, textures = graphics.Textures.Count;
        long reads = resources.Residency.ContainerReads;
        for (int i = 0; i < 128; i++) Frame();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) Frame();
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(buffers, graphics.Buffers.Count); Assert.Equal(textures, graphics.Textures.Count);
        Assert.Equal(reads, resources.Residency.ContainerReads);
        void Frame() { resources.TryGetMesh("mesh.residency.main", out _); resources.ResolveMaterial("material.residency.main"); }
    }

    [Fact]
    public void ProfileAndDeviceGenerationsNeverShareTextureWrappers()
    {
        using var fixture = new ResidencyFixture(); using var firstDevice = new ResidencyDevice(); using var otherDevice = new ResidencyDevice();
        var catalog = fixture.Catalog();
        using var full = new RuntimeWorldAssetResources(firstDevice,catalog,TextureDetailQuality.Full);
        using var half = new RuntimeWorldAssetResources(firstDevice,catalog,(TextureDetailQuality)1);
        using var other = new RuntimeWorldAssetResources(otherDevice,catalog,TextureDetailQuality.Full);
        var fullTexture = full.ResolveMaterial("material.residency.main").BaseColorTexture;
        var halfTexture = half.ResolveMaterial("material.residency.main").BaseColorTexture;
        Assert.NotSame(fullTexture, halfTexture);
        Assert.Equal(8, fullTexture.Description.Width); Assert.Equal(4, halfTexture.Description.Width);
        Assert.NotSame(fullTexture, other.ResolveMaterial("material.residency.main").BaseColorTexture);
        Assert.Throws<InvalidOperationException>(() => new RuntimeWorldAssetResources(firstDevice,catalog,(TextureDetailQuality)2));
        Assert.False(((ResidencyDevice.Texture)fullTexture).Disposed);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingOrHashDamagedRequiredContainerNeverPublishesReady(bool corrupt)
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var catalog = fixture.Catalog();
        string path = Path.Combine(fixture.Root,"mesh.residency.main.flasset");
        if (corrupt) { var bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path,bytes); }
        else File.Delete(path);
        using var resources = new RuntimeWorldAssetResources(graphics,catalog);
        Assert.Equal(AssetResidencyState.Failed, resources.Residency.State);
        Assert.Throws<InvalidDataException>(() => resources.PrepareRequiredAssets(CancellationToken.None));
        Assert.Empty(graphics.Buffers);
    }

    [Fact]
    public void CancelledBoundedLoadingDoesNotPublishPartialReadiness()
    {
        using var fixture = new ResidencyFixture();
        for (int i = 0; i < 40; i++) fixture.AddMaterial("material.residency.extra_"+i);
        var prepared = new PreparedAssetSet(fixture.Catalog());
        Assert.False(prepared.PrepareStep(CancellationToken.None));
        Assert.InRange(prepared.ContainerReads,1,16);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => prepared.PrepareStep(cancel.Token));
        Assert.False(prepared.Complete); Assert.Throws<InvalidOperationException>(prepared.RequireSuccess);
        while (!prepared.PrepareStep(CancellationToken.None)) { }
        prepared.RequireSuccess();
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void WrongTextureUsageOrColorSpaceFailsBeforeGpuPublication(bool wrongUsage)
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var record = fixture.Records[0];
        var texture = RuntimeTextureData.FromRgba8(1,1,[0,0,0,255], RuntimeTextureColorSpace.Linear,
            wrongUsage ? RuntimeTextureUsage.Normal : RuntimeTextureUsage.Color);
        string path = Path.Combine(fixture.Root,record.RuntimePath);
        RuntimeAssetFile.Write(path,RuntimeAssetType.Texture,[],texture.ToPayload());
        fixture.Records[0] = RuntimeAssetIntegrity.SealRecord(path,record);
        using var resources = new RuntimeWorldAssetResources(graphics,fixture.Catalog());
        Assert.Throws<InvalidDataException>(() => resources.PrepareRequiredAssets(CancellationToken.None));
        Assert.Equal(AssetResidencyState.Failed,resources.Residency.State);
        Assert.Empty(graphics.Buffers);
        Assert.Equal(5,graphics.Textures.Count); // Only the existing explicit fallback textures.
    }

    [Fact]
    public void ExhaustedResidentBudgetNeverEvictsAnAlreadyReferencedTexture()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var original = RuntimeTextureData.FromRgba8(8,8,new byte[256], RuntimeTextureColorSpace.Srgb,RuntimeTextureUsage.Color);
        fixture.Add("texture.residency.other",RuntimeAssetType.Texture,original.ToPayload());
        fixture.Add("material.residency.other",RuntimeAssetType.Material,
            """{"baseColorTexture":"texture.residency.other"}"""u8.ToArray(),["texture.residency.other"]);
        using var store = new ResidentAssetStore(graphics,fixture.Catalog(),TextureDetailQuality.Full,400);
        var pinned = store.ResolveMaterial("material.residency.main").BaseColorTexture;
        Assert.True(store.ResolveMaterial("material.residency.other").UsesDevelopmentFallback);
        Assert.False(((ResidencyDevice.Texture)pinned).Disposed);
        Assert.Same(pinned,store.ResolveMaterial("material.residency.main").BaseColorTexture);
        Assert.Throws<InvalidDataException>(() => store.PrepareRequiredAssets(CancellationToken.None));
        Assert.Equal(AssetResidencyState.Failed,store.State);
        Assert.False(((ResidencyDevice.Texture)pinned).Disposed);
    }

    [Fact]
    public void LastOwnerReleaseAllowsFreshRestartGeneration()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var catalog = fixture.Catalog(); long generation;
        using (var previous = new RuntimeWorldAssetResources(graphics,catalog))
        { previous.PrepareRequiredAssets(CancellationToken.None); generation = previous.Residency.Generation; }
        using var next = new RuntimeWorldAssetResources(graphics,catalog);
        Assert.True(next.Residency.Generation > generation);
        next.PrepareRequiredAssets(CancellationToken.None);
        Assert.Equal(AssetResidencyState.Ready,next.Residency.State);
        Assert.Equal(1,next.Residency.Owners);
    }

    [Fact]
    public void FailedNativeRetirementStillReleasesOtherResourcesAndClosesTheLeaseOnce()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var catalog = fixture.Catalog();
        var resources = new RuntimeWorldAssetResources(graphics,catalog);
        resources.PrepareRequiredAssets(CancellationToken.None);
        graphics.Buffers[0].ThrowOnDispose = true;
        Assert.Throws<InvalidOperationException>(resources.Dispose);
        Assert.All(graphics.Buffers, b => Assert.True(b.Disposed));
        Assert.All(graphics.Textures, t => Assert.True(t.Disposed));
        resources.Dispose();
        using var replacement = new RuntimeWorldAssetResources(graphics,catalog);
        Assert.Equal(1,replacement.Residency.Owners);
    }

    [Fact]
    public void OversizedDeclaredContainerFailsBeforeItsFileIsRead()
    {
        using var fixture = new ResidencyFixture(false);
        fixture.Records[0] = fixture.Records[0] with { RuntimeBytes = PreparedAssetSet.MaximumContainerBytes + 1 };
        var prepared = new PreparedAssetSet(fixture.Catalog());
        while (!prepared.PrepareStep(CancellationToken.None)) { }
        Assert.Throws<InvalidDataException>(prepared.RequireSuccess);
        Assert.Equal(2,prepared.ContainerReads);
    }

    [Fact]
    public void LastRetirementDropsPreparedCpuPayloadEvenIfTheDisposedStoreIsStillReferenced()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        var store = new ResidentAssetStore(graphics,fixture.Catalog());
        store.PrepareRequiredAssets(CancellationToken.None);
        var prepared = store.Prepared;
        Assert.True(prepared.PayloadBytes > 0);
        store.Dispose();
        Assert.Equal(0,prepared.PayloadBytes);
        Assert.Empty(prepared.Meshes); Assert.Empty(prepared.Textures); Assert.Empty(prepared.Materials);
    }

    [Fact]
    public void OwnerThreadGuardPreventsForeignGpuPublication()
    {
        using var fixture = new ResidencyFixture(); using var graphics = new ResidencyDevice();
        using var resources = new RuntimeWorldAssetResources(graphics,fixture.Catalog());
        Exception? failure = null;
        var thread = new Thread(() => failure = Record.Exception(() => resources.ResolveMaterial("material.residency.main")));
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Empty(graphics.Buffers);
    }
}
