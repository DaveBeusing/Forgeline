using System.Numerics;
using System.Security.Cryptography;
using ForgeLine.World;

namespace ForgeLine.Game;

public enum ScatterAccessKind : byte { Dock = 1, ConveyorPort = 2, ConstructionApproach = 3 }
public readonly record struct ScatterAccessReservation(ScatterAccessKind Kind, AxisAlignedBounds Bounds);

/// <summary>Frozen gameplay exclusions for offline decoration, never a navigation or ECS owner.</summary>
public sealed class BattlefieldScatterClearance : IScatterClearance
{
    private readonly ScatterClearanceIndex _index;
    private readonly StrategicReservationMask? _mask;
    private readonly long _width, _height;

    public BattlefieldScatterClearance(BattlefieldDefinition map, StrategicReservationMask? reservations = null,
        IEnumerable<ScatterAccessReservation>? access = null, IEnumerable<AxisAlignedBounds>? yards = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        _width = Millimeters(map.Metadata.WidthMeters, false); _height = Millimeters(map.Metadata.HeightMeters, false);
        if (_width <= 0 || _height <= 0) throw new ArgumentException("Scatter map extent must be positive.", nameof(map));
        var areas = new List<ScatterArea>();
        void Add(ScatterArea area)
        {
            if (areas.Count == ScatterClearanceIndex.MaximumAreas) throw new ArgumentException("Gameplay clearance count exceeds its budget.");
            areas.Add(area);
        }
        void Rectangle(AxisAlignedBounds bounds, int padding, ScatterAreaKind kind = ScatterAreaKind.Rectangle) =>
            Add(new(kind, Millimeters(bounds.Minimum.X, false), Millimeters(bounds.Minimum.Z, false),
                Millimeters(bounds.Maximum.X, true), Millimeters(bounds.Maximum.Z, true), padding));
        foreach (var start in map.Starts) Rectangle(start.BuildArea, 12_000);
        foreach (var site in map.Sites) Rectangle(site.BuildArea, 12_000);
        foreach (var crossing in map.Crossings) Rectangle(crossing.PassageBounds, 32_000);
        foreach (var obstacle in map.StaticNavigationObstacles) Rectangle(obstacle, 0);
        foreach (var resource in map.Resources)
        {
            int radius = Radius(resource.HalfExtents.X * 2, resource.HalfExtents.Z * 2, 24_002);
            Add(new(ScatterAreaKind.Corridor, Millimeters(resource.Center.X, false), Millimeters(resource.Center.Z, false),
                Millimeters(resource.Center.X, false), Millimeters(resource.Center.Z, false), radius));
        }
        var nodes = map.RoadNodes.ToDictionary(n => n.Key, n => n.Position, StringComparer.Ordinal);
        foreach (var edge in map.RoadEdges)
        {
            Vector3 a = nodes[edge.SourceNodeKey], b = nodes[edge.DestinationNodeKey];
            Add(new(ScatterAreaKind.Corridor, Millimeters(a.X, false), Millimeters(a.Z, false),
                Millimeters(b.X, false), Millimeters(b.Z, false), 28_002));
        }
        // Existing authored dressing also owns physical presentation space.
        foreach (var item in map.WorldObjects)
            Add(new(ScatterAreaKind.Corridor, Millimeters(item.Position.X, false), Millimeters(item.Position.Z, false),
                Millimeters(item.Position.X, false), Millimeters(item.Position.Z, false), Radius(item.Scale.X, item.Scale.Z, 2)));
        if (access is not null)
            foreach (var item in access)
            {
                if (!Enum.IsDefined(item.Kind)) throw new ArgumentException("Unknown gameplay-access kind.", nameof(access));
                Rectangle(item.Bounds, item.Kind == ScatterAccessKind.ConveyorPort ? 8000 : 12000);
            }
        if (yards is not null) foreach (var yard in yards) Rectangle(yard, 0, ScatterAreaKind.Yard);
        _index = new(areas); _mask = reservations;
        var writer = new WorldCanonicalWriter(); writer.WriteUInt32(1); writer.WriteInt64(_width); writer.WriteInt64(_height);
        writer.WriteBytes(Convert.FromHexString(_index.Sha256));
        writer.WriteInt32(reservations?.Width ?? 0); writer.WriteInt32(reservations?.Height ?? 0);
        if (reservations is not null)
        {
            if (reservations.Width < 1 || reservations.Height < 1 || (long)reservations.Width * reservations.Height > 1_000_000)
                throw new ArgumentException("Strategic clearance raster exceeds its budget.", nameof(reservations));
            for (int z = 0; z < reservations.Height; z++) for (int x = 0; x < reservations.Width; x++)
                writer.WriteUInt32((uint)reservations.GetCell(x, z));
        }
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(writer.ToArray()));
    }

    public string Sha256 { get; }
    public int IndexedAreaCount => _index.AreaCount;
    public bool IsYard(long xMillimeters, long zMillimeters, int radiusMillimeters) => _index.IsYard(xMillimeters, zMillimeters, radiusMillimeters);
    public bool OverlapsYard(long xMillimeters, long zMillimeters, int radiusMillimeters) => _index.OverlapsYard(xMillimeters, zMillimeters, radiusMillimeters);

    public bool IsBlocked(long xMillimeters, long zMillimeters, int radiusMillimeters)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusMillimeters);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radiusMillimeters, 16000);
        if (xMillimeters - radiusMillimeters < 0 || zMillimeters - radiusMillimeters < 0 ||
            xMillimeters + radiusMillimeters >= _width || zMillimeters + radiusMillimeters >= _height ||
            _index.IsBlocked(xMillimeters, zMillimeters, radiusMillimeters)) return true;
        if (_mask is null) return false;
        const int size = StrategicReservationMask.CellSizeMeters * 1000;
        long maxX = (xMillimeters + radiusMillimeters) / size, maxZ = (zMillimeters + radiusMillimeters) / size;
        if (maxX >= _mask.Width || maxZ >= _mask.Height) return true;
        for (int z = (int)((zMillimeters - radiusMillimeters) / size); z <= maxZ; z++)
        for (int x = (int)((xMillimeters - radiusMillimeters) / size); x <= maxX; x++)
            if (_mask.GetCell(x, z) != StrategicReservation.None) return true;
        return false;
    }

    public static BattlefieldScatterClearance FromLayout(ProceduralStrategicLayout layout,
        IEnumerable<ScatterAccessReservation>? access = null, IEnumerable<AxisAlignedBounds>? yards = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new(layout.Definition, layout.Reservations, access, yards);
    }

    private static long Millimeters(float meters, bool upper)
    {
        if (!float.IsFinite(meters) || Math.Abs((double)meters) > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(meters));
        return checked((long)(upper ? Math.Ceiling(meters * 1000.0) : Math.Floor(meters * 1000.0)));
    }

    private static int Radius(float width, float depth, int padding)
    {
        if (!float.IsFinite(width) || !float.IsFinite(depth) || width < 0 || depth < 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        double radius = Math.Sqrt((double)width * width + (double)depth * depth) * 500 + padding;
        if (radius > 128000) throw new ArgumentOutOfRangeException(nameof(width), "Authored footprint exceeds the clearance radius limit.");
        return (int)Math.Ceiling(radius);
    }
}
