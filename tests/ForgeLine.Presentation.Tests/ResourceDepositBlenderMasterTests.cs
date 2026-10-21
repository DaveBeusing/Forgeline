using System.Numerics;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ResourceDepositBlenderMasterTests
{
    [Fact]
    public void ExistingTallSourceOnlyDepositsAndOtherWorldPropsKeepTheirTransforms()
    {
        var simulation = new SimulationCoordinator();
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots));
        var source = new WorldTransform(new Vector3(7, 4, 9), Quaternion.Identity, new Vector3(24, 20, 24));
        foreach (var identity in new[]
        {
            WorldPresentationIdentity.ForResource(ResourceIds.FerrousOre),
            new WorldPresentationIdentity(WorldVisualId.PropRock, WorldPresentationKind.Prop)
        })
        {
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, source);
            simulation.Entities.AddComponent(entity, new VisualIdentity(4000));
            simulation.Entities.AddComponent(entity, identity);
        }
        simulation.AdvanceOneTick();
        Assert.True(snapshots.TryReadLatest(out PresentationSnapshot snapshot));
        Assert.Equal(2, snapshot.Instances.Length);
        foreach (var instance in snapshot.Instances.ToArray())
        {
            Assert.Equal(source.Position, instance.Transform.Position);
            Assert.Equal(source.Rotation, instance.Transform.Rotation);
            Assert.Equal(source.Scale, instance.Transform.Scale);
            Assert.Equal(source, simulation.Entities.GetComponent<WorldTransform>(instance.Entity));
            Assert.False(simulation.Entities.HasComponent<ResourceDeposit>(instance.Entity));
        }
    }

    public static bool OverlayEnabled => Enabled && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_RUNTIME_SEED"));

    [Fact(Skip = "Set qualified mineral exports and the complete canonical runtime seed.", SkipUnless = nameof(OverlayEnabled))]
    public void CompleteReviewCatalogLoadsWithOnlyTheEightBlenderMeshReplacements()
    {
        string root = Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_MASTERS")!;
        string seed = Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_RUNTIME_SEED")!;
        var original = RuntimeAssetCatalog.Load(seed);
        var replacements = new Dictionary<string, (RuntimeAssetRecord Record, string Root)>();
        foreach (string family in new[] { "ferrous_ore", "silicates", "volatiles", "rare_elements" })
        {
            string source = Path.Combine(root, family, "second/runtime");
            var catalog = RuntimeAssetCatalog.Load(source);
            foreach (var record in catalog.Manifest.Assets)
            {
                var before = original.Get(AssetId.Parse(record.Id));
                if (record.Type != RuntimeAssetType.Mesh)
                {
                    Assert.Equal(before.RuntimeHash, record.RuntimeHash);
                    continue;
                }
                Assert.Equal(before.MaterialReferences, record.MaterialReferences);
                Assert.Equal(before.Lods, record.Lods);
                Assert.Equal(before.CollisionReference, record.CollisionReference);
                replacements.Add(record.Id, (record, source));
            }
        }
        Assert.Equal(8, replacements.Count);
        string target = Path.Combine(root, "overlay/runtime");
        Assert.False(Directory.Exists(target), "Use a fresh complete review overlay.");
        Directory.CreateDirectory(target);
        foreach (var record in original.Manifest.Assets)
        {
            bool replaced = replacements.TryGetValue(record.Id, out var replacement);
            var selected = replaced ? replacement.Record : record;
            string source = replaced ? replacement.Root : seed;
            string path = Path.Combine(target, selected.RuntimePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(Path.Combine(source, selected.RuntimePath), path);
        }
        var manifest = RuntimeAssetIntegrity.SealManifest(original.Manifest with
        {
            Assets = original.Manifest.Assets.Select(record => replacements.TryGetValue(record.Id, out var replacement) ? replacement.Record : record).ToArray()
        });
        File.WriteAllBytes(Path.Combine(target, RuntimeAssetCatalog.ManifestFileName),
            JsonSerializer.SerializeToUtf8Bytes(manifest, RuntimeAssetCatalog.CreateJsonOptions()));
        Assert.True(RuntimeAssetQualification.Run(target, requireIntegrity: true).Success);
        Assert.Equal(original.Manifest.Assets.Count, RuntimeAssetCatalog.Load(target).Manifest.Assets.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryMineralPublishesExistingStatesWithStableVisualScale(int family)
    {
        ResourceId[] resources = [ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Volatiles, ResourceIds.RareElements];
        ResourceDepositPresentationState[] expected = [ResourceDepositPresentationState.Untouched, ResourceDepositPresentationState.Active, ResourceDepositPresentationState.Depleted];
        double[] quantities = [1000, 650, 0];
        float[] heights = [8, 12, 4, 14];
        var simulation = new SimulationCoordinator();
        var snapshots = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(snapshots));
        for (int state = 0; state < quantities.Length; state++)
        {
            Vector3 position = new(state * 20, 3, 0);
            var bounds = new AxisAlignedBounds(position - new Vector3(5), position + new Vector3(5));
            EntityId entity = simulation.Entities.CreateEntity();
            simulation.Entities.AddComponent(entity, ResourceDeposit.Restore(resources[family], bounds, 1000, quantities[state], 10));
            simulation.Entities.AddComponent(entity, new WorldTransform(position, Quaternion.Identity, new Vector3(12, 4, 12)));
            simulation.Entities.AddComponent(entity, new VisualIdentity(4000));
            simulation.Entities.AddComponent(entity, WorldPresentationIdentity.ForResource(resources[family]));
        }
        simulation.AdvanceOneTick();
        Assert.True(snapshots.TryReadLatest(out PresentationSnapshot snapshot));
        var instances = snapshot.Instances.ToArray();
        Assert.Equal(3, instances.Length);
        foreach (var instance in instances)
        {
            int state = (int)(instance.Transform.Position.X / 20);
            Assert.Equal(expected[state], instance.WorldFeature.ResourceState);
            Assert.True(instance.WorldFeature.IsInspectable);
            Assert.Equal(new Vector3(12, heights[family], 12), instance.Transform.Scale);
            Assert.Equal(1, instance.Transform.Position.Y - instance.Transform.Scale.Y * .5f, 5);
            var source = simulation.Entities.GetComponent<WorldTransform>(instance.Entity);
            Assert.Equal(new Vector3(state * 20, 3, 0), source.Position);
            Assert.Equal(new Vector3(12, 4, 12), source.Scale);
            var deposit = simulation.Entities.GetComponent<ResourceDeposit>(instance.Entity);
            Assert.Equal(quantities[state], deposit.RemainingQuantity);
            Assert.Equal(1000, deposit.TotalQuantity);
            Assert.Equal(new AxisAlignedBounds(source.Position - new Vector3(5), source.Position + new Vector3(5)), deposit.Bounds);
            Assert.Equal(WorldPresentationIdentity.ForResource(resources[family]).Visual, instance.WorldFeature.Visual);
        }
    }

    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_MASTERS"));

    [Theory(Skip = "Run build/resource-deposits/Qualify-Masters.ps1 with real Blender first.", SkipUnless = nameof(Enabled))]
    [InlineData("ferrous_ore")]
    [InlineData("silicates")]
    [InlineData("volatiles")]
    [InlineData("rare_elements")]
    public void ReopenedMastersPreserveCompiledLodsBoundsAndMaterialChannels(string family)
    {
        string root = Path.Combine(Environment.GetEnvironmentVariable("FORGELINE_RESOURCE_MASTERS")!, family);
        var baseline = RuntimeAssetCatalog.Load(Path.Combine(root, "baseline/runtime"));
        Assert.Equal(2, baseline.Manifest.Assets.Count(x => x.Type == RuntimeAssetType.Mesh));
        foreach (string variant in new[] { "first", "second", "reopened" })
        {
            var candidate = RuntimeAssetCatalog.Load(Path.Combine(root, variant, "runtime"));
            Assert.Equal(baseline.Manifest.Assets.Select(x => x.Id).Order(), candidate.Manifest.Assets.Select(x => x.Id).Order());
            foreach (var before in baseline.Manifest.Assets)
            {
                var id = AssetId.Parse(before.Id);
                var after = candidate.Get(id);
                Assert.Equal(before.Type, after.Type);
                Assert.Equal(before.Dependencies, after.Dependencies);
                Assert.Equal(before.MaterialReferences, after.MaterialReferences);
                Assert.Equal(before.TextureReferences, after.TextureReferences);
                Assert.Equal(before.Lods, after.Lods);
                Assert.Equal(before.CollisionReference, after.CollisionReference);
                Assert.Equal(before.Sockets, after.Sockets);
                Assert.Equal(before.MechanicalParts, after.MechanicalParts);
                if (before.Type != RuntimeAssetType.Mesh)
                {
                    Assert.Equal(before.RuntimeHash, after.RuntimeHash);
                    continue;
                }
                var a = RuntimeMeshData.FromPayload(baseline.Read(id).Payload);
                var b = RuntimeMeshData.FromPayload(candidate.Read(id).Payload);
                Assert.Equal(a.Attributes, b.Attributes);
                Assert.Equal(a.MaterialIds, b.MaterialIds);
                Assert.Equal(a.Sections, b.Sections);
                Assert.Equal(a.Indices, b.Indices);
                Assert.Equal(a.Vertices.Count, b.Vertices.Count);
                for (int i = 0; i < a.Vertices.Count; i++)
                {
                    var x = a.Vertices[i];
                    var y = b.Vertices[i];
                    Assert.True(MathF.Max(MathF.Abs(x.X - y.X), MathF.Max(MathF.Abs(x.Y - y.Y), MathF.Abs(x.Z - y.Z))) <= .00001f, before.Id);
                    Assert.True(Vector3.Distance(new(x.NormalX, x.NormalY, x.NormalZ), new(y.NormalX, y.NormalY, y.NormalZ)) <= .00002f, before.Id);
                    Assert.True(MathF.Abs(x.U - y.U) <= .000001f && MathF.Abs(x.V - y.V) <= .000001f, before.Id);
                    Assert.True(Vector3.Distance(new(x.TangentX, x.TangentY, x.TangentZ), new(y.TangentX, y.TangentY, y.TangentZ)) <= .00002f, before.Id);
                    Assert.Equal(x.TangentW, y.TangentW);
                }
            }
        }
    }
}
