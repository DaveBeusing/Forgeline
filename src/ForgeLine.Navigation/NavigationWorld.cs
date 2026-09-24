using System.Collections.Concurrent;
using ForgeLine.World;

namespace ForgeLine.Navigation;

public sealed class NavigationWorld
{
    private readonly ConcurrentDictionary<
        NavigationCapabilities,
        NavigationSectorGraph> _graphs = new();

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

    public NavigationSectorGraph GetSectorGraph(
        in NavigationCapabilities capabilities)
    {
        return _graphs.GetOrAdd(
            capabilities,
            static (resolvedCapabilities, state) =>
                NavigationSectorGraphBuilder.Build(
                    state.Grid,
                    resolvedCapabilities,
                    state.SectorSettings),
            this);
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
