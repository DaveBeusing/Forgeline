using System.Collections.Concurrent;
using ForgeLine.World;

namespace ForgeLine.Navigation;

public sealed class NavigationWorld
{
    private readonly ConcurrentDictionary<
        NavigationCapabilities,
        Lazy<NavigationSectorGraph>> _graphs = new();

    public NavigationWorld(
        NavigationGrid grid,
        NavigationSectorSettings? sectorSettings = null,
        NavigationVersion? version = null)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        SectorSettings =
            sectorSettings ?? new NavigationSectorSettings();
        SectorSettings.Validate(Grid);

        Version = version ?? NavigationVersion.Initial;
        if (!Version.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }
    }

    public NavigationGrid Grid { get; }

    public NavigationSectorSettings SectorSettings { get; }

    public NavigationVersion Version { get; }

    public int SectorGraphCacheEntryCount => _graphs.Count;

    public NavigationSectorGraph GetSectorGraph(
        in NavigationCapabilities capabilities)
    {
        NavigationCapabilities key = capabilities;
        var candidate =
            new Lazy<NavigationSectorGraph>(
                () => NavigationSectorGraphBuilder.Build(
                    Grid,
                    key,
                    SectorSettings),
                LazyThreadSafetyMode.ExecutionAndPublication);

        return _graphs.GetOrAdd(
            key,
            candidate).Value;
    }

    public static NavigationWorld Build(
        TerrainWorld terrain,
        IEnumerable<AxisAlignedBounds>? staticObstacles = null,
        NavigationGridSettings? gridSettings = null,
        NavigationSectorSettings? sectorSettings = null,
        NavigationVersion? version = null)
    {
        return new NavigationWorld(
            NavigationGridBuilder.Build(
                terrain,
                staticObstacles,
                gridSettings),
            sectorSettings,
            version);
    }
}
