using System.Text.Json;
using ForgeLine.AssetCompiler;
using Xunit;

namespace ForgeLine.Assets.Tests.AssetResidency;

public sealed class AssetResidencyInventoryTests
{
    [Theory]
    [InlineData(RuntimeTextureFormat.Rgba8Unorm, 340, 4, 3)]
    [InlineData(RuntimeTextureFormat.Bc7Unorm, 112, 48, 1)]
    public void ContainerPayloadLogicalAndEffectiveMipBytesRemainDistinct(RuntimeTextureFormat format, long logical, long upload, int first)
    {
        using var fixture = new InventoryFixture();
        var mips = new List<RuntimeTextureMipLevel>();
        for (int size = 8; size > 0; size /= 2)
        {
            int pitch = format == RuntimeTextureFormat.Bc7Unorm ? ((size + 3) / 4) * 16 : size * 4;
            int rows = format == RuntimeTextureFormat.Bc7Unorm ? (size + 3) / 4 : size;
            mips.Add(new(size, size, pitch, new byte[pitch * rows]));
        }
        fixture.Add("texture.inventory.color", new(8, 8, format, RuntimeTextureColorSpace.Srgb, RuntimeTextureUsage.Color, mips));
        var report = AssetResidencyInventory.Create(fixture.Catalog(), 3);
        var asset = Assert.Single(report.Assets);
        Assert.True(asset.ContainerBytes > asset.PayloadBytes);
        Assert.True(asset.PayloadBytes > logical);
        Assert.Equal(logical, asset.Texture!.LogicalMipBytes);
        Assert.Equal(upload, asset.Texture.EffectiveUploadMipBytes);
        Assert.Equal(first, asset.Texture.EffectiveFirstMip);
        Assert.Equal(340, asset.Texture.Rgba8EquivalentBytes);
        Assert.Null(report.ObservedDefaultHeapBytes);
        Assert.Null(report.ObservedStagingBytes);
        Assert.Null(report.ObservedDxgiBudgetBytes);
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(4096, false)]
    public void GameplayGateUsesFullCompiledContainerEvenWhenProfileSkipsMips(int size, bool withinBudget)
    {
        using var fixture = new InventoryFixture();
        fixture.Add("texture.inventory.gameplay", RuntimeTextureData.FromRgba8(size, size, new byte[size * size * 4], RuntimeTextureColorSpace.Srgb, RuntimeTextureUsage.Color));
        var catalog = fixture.Catalog();
        var report = AssetResidencyInventory.Create(catalog, 3);
        Assert.Equal(withinBudget, report.GameplayTextureBudgetPassed);
        Assert.Equal(56 * 1024 * 1024, report.GameplayTextureBudgetBytes);
        Assert.Equal(Assert.Single(report.Assets).ContainerBytes, report.CompiledGameplayTextureBytes);
    }

    [Fact]
    public void StudioContainerExclusionMatchesQualificationWithoutExcludingOrdinaryDisplayImages()
    {
        using var fixture = new InventoryFixture();
        var texture = RuntimeTextureData.FromRgba8(1, 1, [1, 2, 3, 255], RuntimeTextureColorSpace.Linear, RuntimeTextureUsage.DisplayImage);
        fixture.Add("branding.undefined_behavior.splash.inventory", texture);
        fixture.Add("texture.inventory.display", texture);
        var report = AssetResidencyInventory.Create(fixture.Catalog());
        Assert.Equal(report.CompiledStudioTextureBytes, report.CompiledGameplayTextureBytes);
        Assert.Equal(report.CompiledStudioTextureBytes + report.CompiledGameplayTextureBytes, report.ContainerBytes);
    }

    [Theory]
    [InlineData(-1)] [InlineData(4)]
    public void UnsupportedProfilesAreRejected(int skip)
    {
        using var fixture = new InventoryFixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => AssetResidencyInventory.Create(fixture.Catalog(), skip));
    }

    private sealed class InventoryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "asset-inventory-" + Guid.NewGuid().ToString("N"));
        private readonly List<RuntimeAssetRecord> _records = [];
        internal InventoryFixture() => Directory.CreateDirectory(_root);
        internal void Add(string id, RuntimeTextureData texture)
        {
            string path = id + ".flasset";
            RuntimeAssetFile.Write(Path.Combine(_root, path), RuntimeAssetType.Texture, [1, 2, 3], texture.ToPayload());
            _records.Add(new() { Id = id, Type = RuntimeAssetType.Texture, SourcePath = "textures/source.png", RuntimePath = path,
                SourceHash = "source", BuildHash = "build", RuntimeVersion = 1, CompilerVersion = "1.12.0" });
        }
        internal RuntimeAssetCatalog Catalog()
        {
            File.WriteAllText(Path.Combine(_root, "manifest.json"), JsonSerializer.Serialize(new RuntimeAssetManifest
                { CompilerVersion = "1.12.0", Assets = _records }, RuntimeAssetCatalog.CreateJsonOptions()));
            return RuntimeAssetCatalog.Load(_root);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
