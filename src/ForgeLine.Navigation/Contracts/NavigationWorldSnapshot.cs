using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Navigation;

public readonly record struct NavigationSnapshotRevision(NavigationVersion Geometry, ulong Costs)
{
    public bool IsValid => Geometry.IsValid;
}

public readonly record struct NavigationRegionRevision(NavigationSectorCoordinate Region, NavigationVersion Revision);
public readonly record struct NavigationCellPenalty(NavigationCellCoordinate Cell, float AdditionalCost);

/// <summary>Pins existing immutable geometry and prepared graph; owns copied revision/cost arrays.</summary>
public sealed class NavigationWorldSnapshot : ITerrainQuery
{
    private readonly TerrainWorld? _terrain;

    public NavigationWorldSnapshot(
        NavigationWorld world,
        NavigationSectorGraph preparedGraph,
        NavigationCapabilities capabilities,
        TerrainWorld? terrain,
        ulong costRevision,
        ulong knowledgeScope,
        NavigationModuleAvailability costAvailability,
        ReadOnlySpan<NavigationRegionRevision> regions,
        ReadOnlySpan<NavigationCellPenalty> penalties)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(preparedGraph);
        if (!Enum.IsDefined(capabilities.MovementClass) ||
            !ReferenceEquals(preparedGraph.Grid, world.Grid) ||
            preparedGraph.MovementClass != capabilities.MovementClass)
            throw new ArgumentException("The prepared graph must belong to this geometry and capability profile.", nameof(preparedGraph));
        if (!Enum.IsDefined(costAvailability) ||
            (costAvailability == NavigationModuleAvailability.Available && costRevision == 0) ||
            (costAvailability != NavigationModuleAvailability.Available && !penalties.IsEmpty))
            throw new ArgumentException("Unavailable cost data cannot supply traffic penalties.", nameof(costAvailability));
        if (regions.Length > checked(preparedGraph.Width * preparedGraph.Height) || penalties.Length > world.Grid.CellCount)
            throw new ArgumentException("Snapshot data exceeds the geometry capacity.");
        var regionKeys = new HashSet<NavigationSectorCoordinate>();
        foreach (NavigationRegionRevision region in regions)
        {
            if (!region.Revision.IsValid || region.Revision > world.Version ||
                region.Region.X < 0 || region.Region.Z < 0 ||
                region.Region.X >= preparedGraph.Width || region.Region.Z >= preparedGraph.Height ||
                !regionKeys.Add(region.Region))
                throw new ArgumentException("Region revisions must be unique and belong to this world.", nameof(regions));
        }
        var costKeys = new HashSet<NavigationCellCoordinate>();
        foreach (NavigationCellPenalty penalty in penalties)
        {
            if (!world.Grid.Contains(penalty.Cell) || !float.IsFinite(penalty.AdditionalCost) ||
                penalty.AdditionalCost < 0 || !costKeys.Add(penalty.Cell))
                throw new ArgumentException("Cost cells must be unique, finite and non-negative.", nameof(penalties));
        }
        Grid = world.Grid;
        SectorGraph = preparedGraph;
        Capabilities = capabilities;
        Revision = new NavigationSnapshotRevision(world.Version, costRevision);
        KnowledgeScope = knowledgeScope;
        CostAvailability = costAvailability;
        Regions = Array.AsReadOnly(regions.ToArray());
        Penalties = Array.AsReadOnly(penalties.ToArray());
        // Copy the chunk container, retaining existing immutable heightfields.
        _terrain = terrain is null ? null : new TerrainWorld(terrain.Settings, terrain.Chunks);
        WorldBounds = terrain?.WorldBounds ?? new AxisAlignedBounds(
            new Vector3(Grid.Origin.X, 0, Grid.Origin.Y),
            new Vector3(Grid.Origin.X + Grid.Width * Grid.Settings.CellSizeMeters, 0,
                Grid.Origin.Y + Grid.Height * Grid.Settings.CellSizeMeters));
    }

    public NavigationSnapshotRevision Revision { get; }
    public NavigationCapabilities Capabilities { get; }
    public NavigationGrid Grid { get; }
    public NavigationSectorGraph SectorGraph { get; }
    public ulong KnowledgeScope { get; }
    public NavigationModuleAvailability CostAvailability { get; }
    public IReadOnlyList<NavigationRegionRevision> Regions { get; }
    public IReadOnlyList<NavigationCellPenalty> Penalties { get; }
    public bool HasTerrain => _terrain is not null;
    public AxisAlignedBounds WorldBounds { get; }

    public bool TrySampleHeight(float worldX, float worldZ, out float height)
    {
        height = default;
        return CanSample(worldX, worldZ) && _terrain!.TrySampleHeight(worldX, worldZ, out height);
    }

    public bool TrySampleNormal(float worldX, float worldZ, out Vector3 normal)
    {
        normal = default;
        return CanSample(worldX, worldZ) && _terrain!.TrySampleNormal(worldX, worldZ, out normal);
    }

    private bool CanSample(float x, float z) => _terrain is not null && float.IsFinite(x) && float.IsFinite(z) &&
        x >= WorldBounds.Minimum.X && x <= WorldBounds.Maximum.X &&
        z >= WorldBounds.Minimum.Z && z <= WorldBounds.Maximum.Z;
}
