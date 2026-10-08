using BenchmarkDotNet.Attributes;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Navigation.Benchmarks;

[MemoryDiagnoser]
public class CentralDividePathfindingBenchmarks
{
    private BattlefieldDefinition _battlefield = null!;
    private TerrainWorld _terrain = null!;
    private NavigationCapabilities _tracked;
    private System.Numerics.Vector3 _westStart;
    private System.Numerics.Vector3 _eastStart;
    private System.Numerics.Vector3 _northwestExpansion;
    private HierarchicalPathfinder _baseline = null!;
    private HierarchicalPathfinder _northBridgeUnavailable = null!;

    [GlobalSetup]
    public void Setup()
    {
        _battlefield =
            CentralDivideBattlefield.Create();
        _terrain =
            CentralDivideTerrainFactory.Create(
                _battlefield);

        var gridSettings =
            new NavigationGridSettings
            {
                CellSizeMeters = 16.0f,
                StaticObstacleClearanceMeters = 0.5f
            };
        var sectorSettings =
            new NavigationSectorSettings
            {
                SectorSizeCells = 8
            };

        _baseline =
            new HierarchicalPathfinder(
                NavigationWorld.Build(
                    _terrain,
                    _battlefield.CreateNavigationObstacles(),
                    gridSettings,
                    sectorSettings));

        var crossingAvailability =
            _battlefield.Crossings.ToDictionary(
                static crossing => crossing.Key,
                static crossing =>
                    !string.Equals(
                        crossing.Key,
                        "crossing.north_bridge",
                        StringComparison.Ordinal),
                StringComparer.Ordinal);

        _northBridgeUnavailable =
            new HierarchicalPathfinder(
                NavigationWorld.Build(
                    _terrain,
                    _battlefield.CreateNavigationObstacles(
                        crossingAvailability),
                    gridSettings,
                    sectorSettings));

        _tracked =
            NavigationCapabilities.For(
                NavigationMovementClass.Tracked);
        _westStart =
            _battlefield.Starts[0].Position;
        _eastStart =
            _battlefield.Starts[1].Position;
        _northwestExpansion =
            _battlefield.Sites.Single(
                static site =>
                    site.Key ==
                    "northwest.expansion").Position;
    }

    [Benchmark(Baseline = true)]
    public NavigationSearchResult CrossMapRoute()
    {
        return _baseline.FindPath(
            _westStart,
            _eastStart,
            _tracked,
            projectBlockedEndpoints: true);
    }

    [Benchmark]
    public NavigationSearchResult ExpansionRoute()
    {
        return _baseline.FindPath(
            _eastStart,
            _northwestExpansion,
            _tracked,
            projectBlockedEndpoints: true);
    }

    [Benchmark]
    public NavigationSearchResult AlternateRouteAfterBridgeLoss()
    {
        return _northBridgeUnavailable.FindPath(
            _westStart,
            _eastStart,
            _tracked,
            projectBlockedEndpoints: true);
    }
}
