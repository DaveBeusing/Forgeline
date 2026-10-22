using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Presentation.Tests.AssetResidency;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

// Prepared for manual execution; no results are recorded by this source file.
public sealed class ResourceDepositFieldCompositionTests
{
    private static readonly AxisAlignedBounds FieldBounds = new(new(-50, -3, -40), new(50, 3, 40));
    private static readonly ResourceId[] Families = [ResourceIds.FerrousOre, ResourceIds.Silicates, ResourceIds.Volatiles, ResourceIds.RareElements];

    [Fact]
    public void StableKeysSaltAndOrdinalDetermineLayoutWithoutStateOrCameraInputs()
    {
        var identity = new ResourceDepositFieldIdentity("central-divide", "resource.west", 831);
        foreach (var family in Families)
        {
            var first = Layout(identity, family);
            Assert.Equal(first, Layout(identity, family));
            Assert.NotEqual(first[0].Transform, Layout(identity with { Salt = 832 }, family)[0].Transform);
            Assert.NotEqual(first[0].Transform, Layout(identity with { DepositKey = "resource.east" }, family)[0].Transform);
            Assert.True(first.Select(item => item.Transform.Scale).Distinct().Count() > 1);
            Assert.True(first.Select(item => item.Transform.Rotation).Distinct().Count() > 1);
            // Budget reductions select an identical spatial prefix rather than rerolling.
            Assert.Equal(first.Take(4), Layout(identity, family, budget: 4));
            Assert.All(first, item => Assert.EndsWith("_lod1", item.ReducedMeshAssetId));
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(.25f)]
    [InlineData(1.7f)]
    public void AllFamiliesGroundEveryClusterAndKeepEntireSourceEnvelopeInsideFootprint(float slope)
    {
        var terrain = new PlaneTerrain(slope);
        foreach (var family in Families)
        {
            int samplesBefore = terrain.Samples;
            var layout = Layout(new("map", "deposit"), family, terrain);
            Assert.InRange(terrain.Samples - samplesBefore, 1, 9 * ResourceDepositFieldComposition.MaximumClustersPerDeposit);
            Assert.NotEmpty(layout);
            foreach (var cluster in layout)
            {
                Matrix4x4 matrix = cluster.Transform.ToMatrix();
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 p = Vector3.Transform(new Vector3((corner & 1) == 0 ? -.5f : .5f,
                        (corner & 2) == 0 ? -.5f : .5f, (corner & 4) == 0 ? -.5f : .5f), matrix);
                    Assert.InRange(p.X, FieldBounds.Minimum.X, FieldBounds.Maximum.X);
                    Assert.InRange(p.Z, FieldBounds.Minimum.Z, FieldBounds.Maximum.Z);
                    if ((corner & 2) != 0) continue;
                    Assert.True(terrain.TrySampleHeight(p.X, p.Z, out float height));
                    Assert.True(p.Y <= height + .001f, "A sampled mineral base must not float over the terrain.");
                }
            }
        }
    }

    [Fact]
    public void DepletionRetainsDistributedCrustWithStableGroundAnchorsAndConservativeBounds()
    {
        var layout = Layout(new("map", "deposit"), ResourceIds.FerrousOre);
        var full = ResourceDepositFieldComposition.WithRemaining(layout, 1)!;
        var worked = ResourceDepositFieldComposition.WithRemaining(layout, .25)!;
        var empty = ResourceDepositFieldComposition.WithRemaining(layout, 0)!;
        Assert.NotEmpty(empty.Clusters);
        Assert.True(worked.Clusters.Count < full.Clusters.Count);
        Assert.True(empty.Clusters.Count <= worked.Clusters.Count);
        Assert.All(empty.Clusters, item => Assert.True(item.Crust));
        foreach (var cluster in worked.Clusters.Concat(empty.Clusters))
        {
            var original = layout.Single(item => item.Ordinal == cluster.Ordinal);
            Vector3 oldBase = Vector3.Transform(new(0, -.5f, 0), original.Transform.ToMatrix());
            Vector3 newBase = Vector3.Transform(new(0, -.5f, 0), cluster.Transform.ToMatrix());
            Assert.InRange(Vector3.Distance(oldBase, newBase), 0, .0001f);
        }
        var root = Root(new(10, 1), ResourceIds.FerrousOre) with { ResourceField = full };
        var picking = PresentationBounds.ResolveAxisAlignedBounds(root);
        var rendering = SkinPresentationBounds.Resolve(root);
        Assert.Equal(PresentationBounds.ResolveAxisAlignedBounds(root with { ResourceField = null }).Minimum, picking.Minimum);
        Assert.True(rendering.Minimum.X <= full.Bounds.Minimum.X && rendering.Maximum.X >= full.Bounds.Maximum.X);
        Assert.True(InstanceVisibilityIndex.TrySweptBounds(root, null, out var swept));
        Assert.True(swept.Minimum.Y <= full.Bounds.Minimum.Y && swept.Maximum.Y >= full.Bounds.Maximum.Y);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(4097)]
    public void GlobalBudgetKeepsEveryFamilyAndPreservesSimulationAndSnapshotOwnership(int count)
    {
        var entities = new EntityRegistry();
        var roots = new RenderInstance[count];
        for (int i = 0; i < count; i++)
        {
            var entity = entities.CreateEntity();
            var resource = Families[i % Families.Length];
            entities.AddComponent(entity, new ResourceDeposit(resource, FieldBounds, 1000, 10));
            roots[i] = Root(entity, resource);
        }
        var originals = roots.ToArray();
        var composer = new ResourceDepositFieldComposer(new PlaneTerrain());
        composer.Apply(roots, entities);
        int parts = roots.Sum(item => item.ResourceField?.Clusters.Count ?? 0);
        Assert.InRange(composer.CachedDepositCount, 0, ResourceDepositFieldComposition.MaximumCachedDeposits);
        Assert.InRange(parts, 0, ResourceDepositFieldComposition.MaximumSnapshotClusters);
        if (count <= ResourceDepositFieldComposition.MaximumSnapshotClusters)
            Assert.All(roots, item => Assert.NotNull(item.ResourceField));
        else Assert.All(roots, item => Assert.Null(item.ResourceField));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(originals[i], roots[i] with { ResourceField = null });
            Assert.Equal(1000, entities.GetComponent<ResourceDeposit>(roots[i].Entity).RemainingQuantity);
        }
        if (count > ResourceDepositFieldComposition.MaximumSnapshotClusters) return;
        var retained = roots[0].ResourceField!;
        var retainedValues = retained.Clusters.ToArray();
        composer.Apply(roots, entities);
        Assert.Same(retained, roots[0].ResourceField);
        var deposit = entities.GetComponent<ResourceDeposit>(roots[0].Entity);
        entities.SetComponent(roots[0].Entity, ResourceDeposit.Restore(deposit.ResourceId, deposit.Bounds, 1000, 0, 10));
        composer.Apply(roots, entities);
        Assert.Equal(retainedValues, retained.Clusters);
        Assert.NotSame(retained, roots[0].ResourceField);
        Assert.Equal(count, entities.EntityCount);
    }

    [Fact]
    public void RecycledEntityGenerationCannotReuseAnOldField()
    {
        var entities = new EntityRegistry();
        var original = entities.CreateEntity();
        entities.AddComponent(original, new ResourceDeposit(ResourceIds.FerrousOre, FieldBounds, 1000, 10));
        var composer = new ResourceDepositFieldComposer(new PlaneTerrain());
        RenderInstance[] roots = [Root(original, ResourceIds.FerrousOre)];
        composer.Apply(roots, entities);
        var old = roots[0].ResourceField!;
        Assert.True(entities.DestroyEntity(original));
        var recycled = entities.CreateEntity();
        Assert.Equal(original.Index, recycled.Index);
        Assert.NotEqual(original.Generation, recycled.Generation);
        entities.AddComponent(recycled, new ResourceDeposit(ResourceIds.FerrousOre, FieldBounds, 1000, 10));
        roots[0] = Root(recycled, ResourceIds.FerrousOre);
        composer.Apply(roots, entities);
        Assert.NotEqual(old.Clusters[0].Transform, roots[0].ResourceField!.Clusters[0].Transform);
    }

    [Fact]
    public void ViewerFilteringPrecedesFieldSamplingAndPreservesOneInspectableRoot()
    {
        var simulation = new SimulationCoordinator();
        var terrain = new PlaneTerrain();
        var intelligence = new FactionIntelligenceStore();
        var buffer = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer, intelligence, new(1), resourceTerrain: terrain));
        var entity = simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(entity, new ResourceDeposit(ResourceIds.FerrousOre, FieldBounds, 1000, 10));
        simulation.Entities.AddComponent(entity, new WorldTransform(Vector3.Zero, Quaternion.Identity, new Vector3(40, 16, 30)));
        simulation.Entities.AddComponent(entity, new VisualIdentity(4001));
        simulation.Entities.AddComponent(entity, WorldPresentationIdentity.ForResource(ResourceIds.FerrousOre));
        simulation.Entities.AddComponent(entity, new IntelligenceSignature(new(2), 42));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var hidden));
        Assert.Equal(0, hidden.InstanceCount);
        Assert.Equal(0, terrain.Samples);
        simulation.Entities.SetComponent(entity, new IntelligenceSignature(new(1), 42));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var visible));
        Assert.Equal(1, visible.InstanceCount);
        Assert.True(visible.Instances[0].WorldFeature.IsInspectable);
        Assert.False(visible.Instances[0].Selectable.IsSelectable);
        Assert.NotNull(visible.Instances[0].ResourceField);
    }

    [Fact]
    public void MissingTerrainUnsupportedResourcesAndInvalidKeysUseExistingRootFallback()
    {
        Assert.Empty(Layout(new("map", "deposit"), ResourceIds.FerrousOre, new PlaneTerrain(missing: true)));
        Assert.Empty(Layout(new("map", "deposit"), ResourceIds.Steel));
        Assert.Empty(Layout(new("map", "bad/key"), ResourceIds.FerrousOre));
        Assert.Empty(Layout(new("map", "deposit"), ResourceIds.FerrousOre, budget: 0));
        var huge = ResourceDepositFieldComposition.Create(new("map", "large"), ResourceIds.RareElements,
            new(new(-100000, -3, -100000), new(100000, 3, 100000)), new PlaneTerrain(), 24, null);
        Assert.InRange(huge.Length, 1, 24);
    }

    [Fact]
    public void OptionalVariantRequiresBothLodsWithMatchingFamilyMaterial()
    {
        using var fixture = new ResidencyFixture(sealedManifest: false);
        const string material = "material.world.resource.ferrous_ore";
        fixture.AddMaterial(material);
        string[] forms = ["layered_shelf", "faulted_ridge", "breccia_fan", "offset_seam"];
        string[] sizes = ["small", "medium", "large"];
        byte[] payload = new RuntimeMeshData(RuntimeMeshAttributes.Normal,
            [new(0,0,0,0,1,0,0,0,1,0,0,1), new(.1f,0,0,0,1,0,0,0,1,0,0,1), new(0,0,.1f,0,1,0,0,0,1,0,0,1)],
            [0u,1u,2u], [material], [new(0,3,0)]).ToPayload();
        foreach (string form in forms)
        foreach (string size in sizes)
        {
            fixture.Add($"mesh.world.resource.ferrous_ore.{form}.{size}", RuntimeAssetType.Mesh, payload);
            fixture.Records[^1] = fixture.Records[^1] with { MaterialReferences = [material] };
        }
        var identity = new ResourceDepositFieldIdentity("map", "deposit");
        var highOnly = ResourceDepositFieldComposition.Create(identity, ResourceIds.FerrousOre, FieldBounds, new PlaneTerrain(), 24, fixture.Catalog());
        Assert.All(highOnly, item => Assert.Equal("mesh.world.resource.ferrous_ore", item.MeshAssetId));
        foreach (string form in forms)
        foreach (string size in sizes)
        {
            fixture.Add($"mesh.world.resource.ferrous_ore.{form}.{size}_lod1", RuntimeAssetType.Mesh, payload);
            fixture.Records[^1] = fixture.Records[^1] with { MaterialReferences = [material] };
        }
        var pairs = ResourceDepositFieldComposition.Create(identity, ResourceIds.FerrousOre, FieldBounds, new PlaneTerrain(), 24, fixture.Catalog());
        Assert.All(pairs, item => Assert.StartsWith("mesh.world.resource.ferrous_ore.", item.MeshAssetId));
        Assert.Equal(highOnly.Select(item => item.Transform), pairs.Select(item => item.Transform));
    }

    private static ResourceDepositFieldCluster[] Layout(ResourceDepositFieldIdentity identity, ResourceId resource,
        ITerrainQuery? terrain = null, int budget = 24) =>
        ResourceDepositFieldComposition.Create(identity, resource, FieldBounds, terrain ?? new PlaneTerrain(), budget, null);

    private static RenderInstance Root(EntityId entity, ResourceId resource) => new(entity,
        new(Vector3.Zero, Quaternion.Identity, new(40, 16, 30)), new(4001), RenderMaterialHandle.Default,
        RenderVisibilityMask.World, WorldFeature: new(WorldPresentationIdentity.ForResource(resource).Visual,
            WorldPresentationKind.ResourceDeposit, ResourceDepositPresentationState.Untouched, true));

    private sealed class PlaneTerrain(float slope = 0, bool missing = false) : ITerrainQuery
    {
        public int Samples { get; private set; }
        public AxisAlignedBounds WorldBounds => new(new(-1000000), new(1000000));
        public bool TrySampleHeight(float worldX, float worldZ, out float height)
        {
            Samples++;
            height = 7 + worldX * slope + worldZ * slope * .2f;
            return !missing;
        }
        public bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal)
        {
            normal = Vector3.Normalize(new(-slope, 1, -slope * .2f));
            return !missing;
        }
    }
}
