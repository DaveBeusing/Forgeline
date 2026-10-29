using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Game;

[Flags]
public enum StrategicReservation : byte
{
    None = 0,
    Construction = 1,
    Resource = 2,
    Traffic = 4,
    Obstacle = 8
}

/// <summary>Immutable 16 m exclusion raster, in Z/X order. None denotes unreserved space.</summary>
public sealed class StrategicReservationMask
{
    private readonly StrategicReservation[] _cells;

    internal StrategicReservationMask(int width, int height, StrategicReservation[] cells)
    {
        Width = width;
        Height = height;
        _cells = cells.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public const int CellSizeMeters = 16;

    public StrategicReservation GetCell(int x, int z)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(z);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(z, Height);
        return _cells[z * Width + x];
    }

    /// <summary>Tests all overlapped cells; outside-world footprints are excluded.</summary>
    public bool IsReserved(AxisAlignedBounds footprint)
    {
        if (!float.IsFinite(footprint.Minimum.X) || !float.IsFinite(footprint.Minimum.Z) ||
            !float.IsFinite(footprint.Maximum.X) || !float.IsFinite(footprint.Maximum.Z) ||
            footprint.Minimum.X < 0 || footprint.Minimum.Z < 0 ||
            footprint.Maximum.X >= Width * CellSizeMeters || footprint.Maximum.Z >= Height * CellSizeMeters)
            return true;
        int minX = (int)(footprint.Minimum.X / CellSizeMeters);
        int minZ = (int)(footprint.Minimum.Z / CellSizeMeters);
        int maxX = (int)(footprint.Maximum.X / CellSizeMeters);
        int maxZ = (int)(footprint.Maximum.Z / CellSizeMeters);
        for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
                if (GetCell(x, z) != StrategicReservation.None) return true;
        return false;
    }
}

public sealed record StrategicLayoutAttempt(int Number, string Code, string Message);

/// <summary>A qualified layout, not a registered runtime scenario or compiled terrain artifact.</summary>
public sealed class ProceduralStrategicLayout
{
    private readonly BattlefieldDefinition _definition;

    internal ProceduralStrategicLayout(BattlefieldDefinition definition, StrategicReservationMask reservations,
        WorldGenerationFingerprint fingerprint, StrategicMapAuditReport audit,
        BattlefieldOperationalGeographyReport geography, int attempts)
    {
        _definition = Copy(definition);
        Reservations = reservations;
        Fingerprint = fingerprint;
        Audit = audit;
        Geography = geography;
        Attempts = attempts;
    }

    // BattlefieldDefinition exposes legacy mutable arrays. Never hand out the retained identity's arrays.
    public BattlefieldDefinition Definition => Copy(_definition);
    public StrategicReservationMask Reservations { get; }
    public WorldGenerationFingerprint Fingerprint { get; }
    public StrategicMapAuditReport Audit { get; }
    public BattlefieldOperationalGeographyReport Geography { get; }
    public int Attempts { get; }

    private static BattlefieldDefinition Copy(BattlefieldDefinition map) => new(map.Metadata,
        map.Starts.ToArray(), map.Resources.ToArray(), map.WorldObjects.ToArray(), map.Sites.ToArray(),
        map.RoadNodes.ToArray(), map.RoadEdges.ToArray(), map.Crossings.ToArray(), map.Objectives.ToArray(),
        map.StaticNavigationObstacles.ToArray(), map.TerrainVisual with
        { MaterialAssetIds = map.TerrainVisual.MaterialAssetIds.ToArray() });
}

public sealed class ProceduralStrategicLayoutResult
{
    internal ProceduralStrategicLayoutResult(ProceduralStrategicLayout? layout, IEnumerable<StrategicLayoutAttempt> attempts)
    {
        Layout = layout;
        Attempts = Array.AsReadOnly(attempts.ToArray());
    }

    public bool Succeeded => Layout is not null;
    public ProceduralStrategicLayout? Layout { get; }
    public IReadOnlyList<StrategicLayoutAttempt> Attempts { get; }
}
