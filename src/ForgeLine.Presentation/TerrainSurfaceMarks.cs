using System.Numerics;

namespace ForgeLine.Presentation;

public enum TerrainSurfaceMarkKind { TireTrack, TrackedVehicle, RoadWear }

/// <summary>Authored public ground wear, not an entity trail or combat-damage event.</summary>
public readonly record struct TerrainSurfaceMark(TerrainSurfaceMarkKind Kind, Vector2 Start, Vector2 End,
    float WidthMeters, float Strength = .35f)
{
    public TerrainBlendRegion ToBlendRegion()
    {
        Vector2 delta = End - Start;
        float length = delta.Length();
        if (!Enum.IsDefined(Kind) || !float.IsFinite(Start.X) || !float.IsFinite(Start.Y) ||
            !float.IsFinite(End.X) || !float.IsFinite(End.Y) || !float.IsFinite(length) || length is < .25f or > 2000 ||
            !float.IsFinite(WidthMeters) || WidthMeters is < .25f or > 32 ||
            !float.IsFinite(Strength) || Strength is < 0 or > .65f)
            throw new ArgumentOutOfRangeException(nameof(WidthMeters), "Ground wear requires a finite bounded authored footprint.");
        TerrainMaterialSlot slot = Kind switch
        {
            TerrainSurfaceMarkKind.TrackedVehicle => TerrainMaterialSlot.Mud,
            TerrainSurfaceMarkKind.RoadWear => TerrainMaterialSlot.IndustrialGround,
            _ => TerrainMaterialSlot.Dirt
        };
        return new(slot, (Start + End) * .5f, new(length * .5f, WidthMeters * .5f),
            Math.Max(1, WidthMeters * .5f), MathF.Atan2(delta.Y, delta.X), Strength);
    }
}

public interface ITerrainSurfaceMarkSource
{
    ReadOnlyMemory<TerrainSurfaceMark> Marks { get; }
}

public sealed class TerrainSurfaceMarks : ITerrainSurfaceMarkSource
{
    public const int MaximumMarks = 64;
    private readonly TerrainSurfaceMark[] _marks;

    public TerrainSurfaceMarks(ReadOnlySpan<TerrainSurfaceMark> marks)
    {
        if (marks.Length > MaximumMarks) throw new ArgumentOutOfRangeException(nameof(marks));
        foreach (var mark in marks) _ = mark.ToBlendRegion();
        _marks = marks.ToArray();
    }

    public ReadOnlyMemory<TerrainSurfaceMark> Marks => _marks;
}
