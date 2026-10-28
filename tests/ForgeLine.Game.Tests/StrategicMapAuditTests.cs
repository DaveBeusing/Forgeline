using System.Numerics;
using System.Text.Json;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class StrategicMapAuditTests
{
    [Theory]
    [InlineData(CentralDivideScenario.CompositionKey)]
    [InlineData(SplitBasinBattlefield.CompositionKey)]
    public void RegisteredCompiledMapsAuditWithoutGraphicsAndRepeatExactly(string scenario)
    {
        var composition = ScenarioRegistry.Default.Resolve(scenario);
        var terrain = composition.CreateTerrain(composition.Battlefield);
        var first = Audit(composition.Battlefield, terrain);
        var second = Audit(composition.Battlefield, terrain);
        Assert.Equal(0, first.ErrorCount);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal(18, first.Openings.Count);
        Assert.Equal(6, first.BuildAreas.Count);
        Assert.All(first.BuildAreas, area => Assert.True(area.ReachablePermittedSquareMeters > 0));
        Assert.Contains(first.Routes, r => r.State == "Baseline" && r.TargetKind == "Resource" && r.AccessibleDistanceMeters > 0);
        Assert.Contains(first.Routes, r => r.State == "AllCrossingsClosed" && r.TargetKind == "Start" && r.AccessibleDistanceMeters is null);
        Assert.NotEqual("Fair", first.Verdict);
        Assert.All(first.Diagnostics.Where(d => d.State == "Baseline" && d.Code == "ExpansionDistance"),
            diagnostic => Assert.Contains(composition.Battlefield.Resources, r => r.Contested &&
                r.Center.X == diagnostic.X && r.Center.Z == diagnostic.Z));
    }

    [Fact]
    public void BlockedSpawnCannotBeProjectedIntoAValidOpening()
    {
        var map = SplitBasinBattlefield.Create();
        var point = map.Starts[0].Position;
        var broken = Copy(map, obstacles: [.. map.StaticNavigationObstacles,
            new(point - new Vector3(80, 100, 80), point + new Vector3(80, 100, 80))]);
        var report = Audit(broken, Terrain(map));
        Assert.Equal(3, report.Diagnostics.Count(d => d.Code == "BlockedSpawn" && d.Player == map.Starts[0].Player.Value));
        Assert.Equal("Invalid", report.Verdict);
        Assert.All(report.Openings.Where(o => o.Player == map.Starts[0].Player.Value),
            opening => Assert.Null(opening.AccessibleDistanceMeters));
    }

    [Fact]
    public void UnreachableVitalResourcesFailEvenWhenEuclideanDistanceIsShort()
    {
        var map = SplitBasinBattlefield.Create();
        var obstacles = map.StaticNavigationObstacles.Concat(map.Resources.Where(r => r.ResourceId == ResourceIds.FerrousOre)
            .Select(r => new AxisAlignedBounds(r.Center - new Vector3(180, 100, 180), r.Center + new Vector3(180, 100, 180)))).ToArray();
        var report = Audit(Copy(map, obstacles: obstacles), Terrain(map));
        Assert.Contains(report.Diagnostics, d => d.Code == "MissingVitalOpening" && d.Target == $"resource:{ResourceIds.FerrousOre.Value}");
        Assert.All(report.Openings.Where(o => o.Resource == ResourceIds.FerrousOre.Value),
            opening => Assert.Null(opening.AccessibleDistanceMeters));
    }

    [Fact]
    public void UnevenOpeningQuantityAndExtractionProduceExplicitWarnings()
    {
        var map = SplitBasinBattlefield.Create();
        string key = map.Resources.Where(r => !r.Contested && r.ResourceId == ResourceIds.FerrousOre)
            .OrderBy(r => r.Center.X).First().Key;
        var resources = map.Resources.Select(r => r.Key == key ? r with
            { TotalQuantity = r.TotalQuantity / 10, ExtractionRatePerSecond = r.ExtractionRatePerSecond / 10 } : r).ToArray();
        var report = Audit(Copy(map, resources: resources), Terrain(map));
        Assert.Equal(0, report.ErrorCount);
        Assert.Equal(3, report.Diagnostics.Count(d => d.Code == "OpeningQuantity"));
        Assert.Equal(3, report.Diagnostics.Count(d => d.Code == "OpeningExtraction"));
        Assert.Equal("ReviewRequired", report.Verdict);
    }

    [Fact]
    public void CrossingLossMeasuresDetourAndRealRoadBottleneck()
    {
        var map = SplitBasinBattlefield.Create();
        var report = Audit(map, Terrain(map));
        var bridge = map.Crossings.Single(c => map.GetRoadEdge(c.LogisticsEdgeKey).CapacityPerSecond == 160);
        var baseline = report.Routes.Single(r => r.State == "Baseline" && r.Movement == "Wheeled" &&
            r.TargetKind == "Start" && r.Player == map.Starts[0].Player.Value);
        var detour = report.Routes.Single(r => r.State == $"Closed:{bridge.Key}" && r.Movement == "Wheeled" &&
            r.TargetKind == "Start" && r.Player == map.Starts[0].Player.Value);
        Assert.True(detour.AccessibleDistanceMeters > baseline.AccessibleDistanceMeters + 500);
        Assert.All(report.RoadCapacities.Where(c => c.State == "Baseline"), c => Assert.Equal(120, c.WidestPathCapacityPerSecond));
        Assert.All(report.RoadCapacities.Where(c => c.State == $"Closed:{bridge.Key}"), c => Assert.Equal(90, c.WidestPathCapacityPerSecond));
        Assert.All(report.RoadCapacities.Where(c => c.State == "AllCrossingsClosed"), c => Assert.Equal(0, c.WidestPathCapacityPerSecond));
    }

    [Fact]
    public void WarningExceptionsRetainEvidenceAndCannotWaiveErrorsOrStaleIds()
    {
        var map = SplitBasinBattlefield.Create();
        var terrain = Terrain(map);
        var initial = Audit(map, terrain);
        var warning = initial.Diagnostics.First(d => d.Severity == "Warning");
        var accepted = Audit(map, terrain,
            acceptedWarnings: new Dictionary<string, string> { [warning.Id] = "Deliberate combined crossing cut; reviewed authored geography." });
        Assert.Equal(initial.WarningCount - 1, accepted.WarningCount);
        Assert.Equal(1, accepted.AcceptedWarningCount);
        Assert.Contains(accepted.Diagnostics, d => d.Id == warning.Id && d.Severity == "Warning" && d.AcceptedReason is not null);
        Assert.Throws<ArgumentException>(() => Audit(map, terrain,
            acceptedWarnings: new Dictionary<string, string> { ["stale"] = "Rationale" }));
        var broken = Copy(map, resources: map.Resources.Where(r => r.ResourceId != ResourceIds.FerrousOre).ToArray());
        var error = Audit(broken, terrain).Diagnostics.First(d => d.Severity == "Error");
        Assert.Throws<ArgumentException>(() => Audit(broken, terrain,
            acceptedWarnings: new Dictionary<string, string> { [error.Id] = "Cannot waive invalidity." }));
    }

    [Fact]
    public void BudgetCancellationAndInvalidRulesCannotProduceSuccess()
    {
        var map = SplitBasinBattlefield.Create();
        var terrain = Terrain(map);
        Assert.Throws<InvalidOperationException>(() => Audit(map, terrain, new() { MaximumRouteQueries = 1 }));
        Assert.Throws<OperationCanceledException>(() => StrategicMapAudit.Analyze(map, terrain, cancellationToken: new(true)));
        Assert.Throws<ArgumentException>(() => Audit(map, terrain, new() { AsymmetryWarningRatio = double.NaN }));
        Assert.Throws<ArgumentException>(() => Audit(map, terrain, new() { VitalResources = Array.Empty<ResourceId>() }));
    }

    [Fact]
    public void AuthoredEnumerationOrderDoesNotChangeReport()
    {
        var map = SplitBasinBattlefield.Create();
        var reordered = new BattlefieldDefinition(map.Metadata, map.Starts.Reverse().ToArray(),
            map.Resources.Reverse().ToArray(), map.WorldObjects.ToArray(), map.Sites.Reverse().ToArray(),
            map.RoadNodes.Reverse().ToArray(), map.RoadEdges.Reverse().ToArray(), map.Crossings.Reverse().ToArray(),
            map.Objectives.ToArray(), map.StaticNavigationObstacles.ToArray(), map.TerrainVisual)
            { MapRevision = map.MapRevision, TacticalAreas = map.TacticalAreas };
        var terrain = Terrain(map);
        Assert.Equal(JsonSerializer.Serialize(Audit(map, terrain)),
            JsonSerializer.Serialize(Audit(reordered, terrain)));
    }

    private static StrategicMapAuditReport Audit(BattlefieldDefinition map, TerrainWorld terrain,
        StrategicMapAuditRules? rules = null, IReadOnlyDictionary<string, string>? acceptedWarnings = null) =>
        StrategicMapAudit.Analyze(map, terrain, rules, acceptedWarnings, TestContext.Current.CancellationToken);

    private static TerrainWorld Terrain(BattlefieldDefinition map) =>
        BattlefieldTerrainFactory.Create(map, SplitBasinBattlefield.CreateTerrainRecipe());

    private static BattlefieldDefinition Copy(BattlefieldDefinition map,
        BattlefieldResourceDepositDefinition[]? resources = null, AxisAlignedBounds[]? obstacles = null) =>
        new(map.Metadata, map.Starts.ToArray(), resources ?? map.Resources.ToArray(),
            map.WorldObjects.ToArray(), map.Sites.ToArray(), map.RoadNodes.ToArray(), map.RoadEdges.ToArray(),
            map.Crossings.ToArray(), map.Objectives.ToArray(), obstacles ?? map.StaticNavigationObstacles.ToArray(), map.TerrainVisual)
            { MapRevision = map.MapRevision, TacticalAreas = map.TacticalAreas };
}
