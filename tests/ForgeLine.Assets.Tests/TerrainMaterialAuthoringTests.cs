using System.Numerics;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class TerrainMaterialAuthoringTests
{
    [Fact]
    public void RefinedPaletteCompilesCompleteMaterialsWithReusableTextures()
    {
        string runtime = Path.Combine(Path.GetTempPath(), "forgeline-terrain-materials-" + Guid.NewGuid().ToString("N"));
        try
        {
            var compilation = ProductionAssetFixture.CompileTo(runtime);
            Assert.True(compilation.Success);
            var catalog = RuntimeAssetCatalog.Load(runtime);
            var scales = new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["grass_ground"] = 1, ["dirt"] = 1, ["mud"] = 1.5f, ["rock"] = 1,
                ["gravel"] = 2, ["industrial_ground"] = 1.5f, ["concrete"] = 1.5f, ["scorched"] = 1.25f
            };
            var originalPayloads = new Dictionary<AssetId, byte[]>();
            foreach (var pair in scales)
            {
                AssetId id = AssetId.Parse("material.world.terrain." + pair.Key);
                byte[] first = catalog.Read(id).Payload;
                originalPayloads.Add(id, first);
                var material = RuntimeMaterialData.FromPayload(first);
                Assert.Equal(new Vector2(pair.Value), material.UvScale);
                Assert.True(material.BaseColorTexture.HasValue && material.NormalTexture.HasValue && material.OrmTexture.HasValue);
                foreach (var texture in new[] { material.BaseColorTexture, material.NormalTexture, material.OrmTexture })
                    Assert.Equal(RuntimeAssetType.Texture, catalog.Manifest.Assets.Single(asset => asset.Id == texture!.Value.Value).Type);
                Assert.Equal(first, catalog.Read(id).Payload);
            }
            var second = ProductionAssetFixture.CompileTo(runtime);
            Assert.True(second.Success);
            Assert.Equal(compilation.CompiledCount, second.CompiledCount);
            var reloaded = RuntimeAssetCatalog.Load(runtime);
            foreach (var name in scales.Keys)
            {
                var id = AssetId.Parse("material.world.terrain." + name);
                Assert.Equal(originalPayloads[id], reloaded.Read(id).Payload);
            }
        }
        finally
        {
            if (Directory.Exists(runtime)) Directory.Delete(runtime, recursive: true);
        }
    }

    [Fact]
    public void AuthoredUvScaleCompilesDeterministicallyAndRejectsInvalidScale()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-terrain-uv-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source");
        string runtime = Path.Combine(root, "runtime");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "surface.asset.json"), """{"id":"material.test.terrain","type":"material","source":"surface.material.json"}""");
            string path = Path.Combine(source, "surface.material.json");
            File.WriteAllText(path, """{"uvScale":[1.5,2]}""");
            Assert.True(AssetPipelineCompiler.Compile(source, runtime, clean: true).Success);
            AssetId id = AssetId.Parse("material.test.terrain");
            byte[] first = RuntimeAssetCatalog.Load(runtime).Read(id).Payload;
            Assert.True(AssetPipelineCompiler.Compile(source, runtime, clean: true).Success);
            byte[] second = RuntimeAssetCatalog.Load(runtime).Read(id).Payload;
            Assert.Equal(first, second);
            Assert.Equal(new Vector2(1.5f, 2), RuntimeMaterialData.FromPayload(second).UvScale);
            File.WriteAllText(path, """{"uvScale":[0,2]}""");
            Assert.False(AssetPipelineCompiler.Compile(source, runtime, clean: true).Success);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
