using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ProceduralStrategicLayoutTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(17UL)]
    [InlineData(ulong.MaxValue)]
    public void GeneratedLayoutsPassUnchangedHeadlessGates(ulong seed)
    {
        var terrain = Terrain();
        var result = Generate(terrain, Recipe(seed));
        Assert.True(result.Succeeded, string.Join("; ", result.Attempts));
        var layout = result.Layout!;
        var map = layout.Definition;
        Assert.Equal(0, layout.Audit.ErrorCount);
        Assert.Equal(0, layout.Audit.WarningCount);
        Assert.Equal(18, layout.Audit.Openings.Count);
        Assert.All(layout.Audit.Openings, o => Assert.InRange(o.AccessibleDistanceMeters!.Value, 0, 450));
        Assert.All(layout.Audit.BuildAreas, a => Assert.True(a.ReachablePermittedSquareMeters > 0));
        Assert.Equal(2, map.Starts.Count);
        Assert.Equal(6, map.Sites.Count);
        Assert.Equal(12, map.Resources.Count);
        Assert.Empty(map.WorldObjects);
        Assert.True(layout.Geography.TerrainElevationRangeMeters >= 20);
        Assert.All(map.Starts, s => Assert.True(layout.Reservations.IsReserved(s.BuildArea)));
        Assert.All(map.Resources, r => Assert.True(layout.Reservations.IsReserved(
            new(r.Center - r.HalfExtents, r.Center + r.HalfExtents))));
        Assert.Contains(Enumerable.Range(0, layout.Reservations.Height), z =>
            Enumerable.Range(0, layout.Reservations.Width).Any(x => layout.Reservations.GetCell(x, z) == StrategicReservation.None));
        var world = NavigationWorld.Build(terrain, map.StaticNavigationObstacles,
            new NavigationGridSettings { CellSizeMeters = 16 });
        var nodes = map.RoadNodes.ToDictionary(n => n.Key);
        Assert.All(map.RoadEdges, edge =>
        {
            Assert.True(Vector3.Distance(nodes[edge.SourceNodeKey].Position, nodes[edge.DestinationNodeKey].Position) < 24);
            world.Grid.TryWorldToCell(nodes[edge.SourceNodeKey].Position, out var cell);
            Assert.True(world.Grid.IsTraversable(cell, NavigationCapabilities.For(NavigationMovementClass.Wheeled)));
        });
    }

    [Fact]
    public void IdentityRepeatsIncludesTerrainAndSurvivesCallerMutation()
    {
        var terrain = Terrain();
        var first = Generate(terrain, Recipe(71)).Layout!;
        var second = Generate(terrain, Recipe(71)).Layout!;
        Assert.Equal(first.Fingerprint.GetCanonicalBytes(), second.Fingerprint.GetCanonicalBytes());
        var exposed = first.Definition;
        ((BattlefieldStartPosition[])exposed.Starts)[0] = default;
        exposed.TerrainVisual.MaterialAssetIds[0] = "changed";
        Assert.True(first.Definition.Starts[0].Player.IsSpecified);
        Assert.NotEqual("changed", first.Definition.TerrainVisual.MaterialAssetIds[0]);
        var otherTerrain = Generate(Terrain(heightOffset: 1), Recipe(71)).Layout!;
        Assert.NotEqual(first.Fingerprint.Sha256, otherTerrain.Fingerprint.Sha256);
        var otherSeed = Generate(terrain, Recipe(72)).Layout!;
        Assert.NotEqual(first.Definition.Starts[0].Position, otherSeed.Definition.Starts[0].Position);
    }

    [Fact]
    public void IncompatibleInputFailsBeforeAttemptingPlacement()
    {
        var terrain = Terrain();
        foreach (var recipe in new[] { Recipe(1) with { GeographyProfileId = "unknown.v1" },
                     Recipe(1) with { WidthInChunks = 11 }, Recipe(1) with { Origin = new(-1, 0) },
                     Recipe(1) with { Options = 1 } })
        {
            var result = Generate(terrain, recipe);
            Assert.False(result.Succeeded);
            Assert.Null(result.Layout);
            Assert.Equal("InvalidInput", Assert.Single(result.Attempts).Code);
            Assert.Equal(0, result.Attempts[0].Number);
        }
    }

    [Fact]
    public void FlatTerrainCannotBypassElevationGateAndRetriesAreBounded()
    {
        var result = Generate(Terrain(flat: true), Recipe(0), maximumAttempts: 2);
        Assert.False(result.Succeeded);
        Assert.Null(result.Layout);
        Assert.Equal(2, result.Attempts.Count);
        Assert.All(result.Attempts, a => Assert.Contains("elevation", a.Message, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TerrainWallAndSteepStartsProduceNoPartialMap()
    {
        var blocked = Generate(Terrain(wall: true), Recipe(0), maximumAttempts: 1);
        Assert.False(blocked.Succeeded);
        Assert.Null(blocked.Layout);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(blocked.Attempts).Code));
        var steep = Generate(Terrain(steep: true), Recipe(0), maximumAttempts: 1);
        Assert.Equal("PlacementExhausted", Assert.Single(steep.Attempts).Code);
    }

    [Fact]
    public void CancellationIsNotReportedAsCompletedFailureOrSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => ProceduralStrategicLayoutGenerator.Generate(
            Terrain(), Recipe(0), cancellationToken: cancellation.Token));
    }

    private static ProceduralStrategicLayoutResult Generate(TerrainWorld terrain, WorldGenerationRecipe recipe, int maximumAttempts = 8) =>
        ProceduralStrategicLayoutGenerator.Generate(terrain, recipe, maximumAttempts, TestContext.Current.CancellationToken);

    private static WorldGenerationRecipe Recipe(ulong seed) => new()
    {
        Seed = seed, WidthInChunks = 12, HeightInChunks = 12,
        GeographyProfileId = ProceduralStrategicLayoutGenerator.GeographyProfile,
        RulesProfileId = ProceduralStrategicLayoutGenerator.RulesProfile
    };

    private static TerrainWorld Terrain(bool flat = false, bool wall = false, bool steep = false, float heightOffset = 0)
    {
        var chunks = new List<TerrainChunk>();
        for (int cz = 0; cz < 12; cz++)
            for (int cx = 0; cx < 12; cx++)
            {
                var heights = new float[65 * 65];
                for (int z = 0; z < 65; z++)
                    for (int x = 0; x < 65; x++)
                    {
                        int px = cx * 256 + x * 4;
                        heights[z * 65 + x] = heightOffset + (flat ? 0 : px * (steep ? 1f : .01f)) +
                            (wall && px >= 1504 && px <= 1568 ? 2000 : 0);
                    }
                chunks.Add(new(new(cx, cz), new(65, 256, heights)));
            }
        return new(new(), chunks);
    }
}
