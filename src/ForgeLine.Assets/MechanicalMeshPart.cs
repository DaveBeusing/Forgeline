namespace ForgeLine.Assets;

public enum MechanicalMotionKind
{
    Static,
    TurretYaw,
    Cannon,
    Wheel,
    Fan,
    Radar,
    Door,
    Crane
}

/// <summary>A triangle range in the existing mesh, in mesh-local coordinates.</summary>
public sealed record MechanicalMeshPart(
    string Name,
    int FirstIndex,
    int IndexCount,
    int Parent,
    MechanicalMotionKind Motion,
    string? Socket,
    float Rate = 1,
    float Travel = 0);

public static class MechanicalMeshValidation
{
    public const int MaximumParts = 32;

    public static void Validate(IReadOnlyList<MechanicalMeshPart> parts,
        IReadOnlyList<AssetSocket> sockets, int? indexCount = null)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(sockets);
        if (parts.Count > MaximumParts)
            throw new InvalidDataException("Mechanical mesh exceeds the 32-part limit.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        int next = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            MechanicalMeshPart part = parts[i];
            if (part is null || string.IsNullOrWhiteSpace(part.Name) || !names.Add(part.Name) ||
                part.FirstIndex != next || part.IndexCount <= 0 || part.IndexCount % 3 != 0 ||
                part.Parent < -1 || part.Parent >= i || !Enum.IsDefined(part.Motion) ||
                !float.IsFinite(part.Rate) || MathF.Abs(part.Rate) > 20 ||
                !float.IsFinite(part.Travel) || MathF.Abs(part.Travel) > 2)
                throw new InvalidDataException("Mechanical parts require unique names, contiguous triangles, finite bounded motion and preceding parents.");
            if (part.Motion != MechanicalMotionKind.Static &&
                !sockets.Any(socket => socket.IsValid && socket.Name == part.Socket))
                throw new InvalidDataException($"Mechanical part '{part.Name}' requires a valid authored socket.");
            next = checked(next + part.IndexCount);
        }
        if (parts.Count > 0 && indexCount.HasValue && next != indexCount.Value)
            throw new InvalidDataException("Mechanical parts must cover the complete compiled mesh exactly once.");
    }
}
