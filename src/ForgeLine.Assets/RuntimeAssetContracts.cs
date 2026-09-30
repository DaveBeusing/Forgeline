namespace ForgeLine.Assets;

public enum RuntimeAssetType
{
    Mesh = 1,
    Texture = 2,
    Material = 3,
}

public readonly record struct AssetBounds(
    float MinX,
    float MinY,
    float MinZ,
    float MaxX,
    float MaxY,
    float MaxZ)
{
    public bool IsValid =>
        float.IsFinite(MinX) &&
        float.IsFinite(MinY) &&
        float.IsFinite(MinZ) &&
        float.IsFinite(MaxX) &&
        float.IsFinite(MaxY) &&
        float.IsFinite(MaxZ) &&
        MinX <= MaxX &&
        MinY <= MaxY &&
        MinZ <= MaxZ;
}

public readonly record struct AssetSocket(string Name, float X, float Y, float Z)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        float.IsFinite(X) &&
        float.IsFinite(Y) &&
        float.IsFinite(Z);
}

public readonly record struct AssetLodReference(int Level, string AssetId, float MaxDistance);

public sealed record RuntimeAssetRecord
{
    public required string Id { get; init; }

    public required RuntimeAssetType Type { get; init; }

    public required string SourcePath { get; init; }

    public required string RuntimePath { get; init; }

    public required string SourceHash { get; init; }

    public required string BuildHash { get; init; }

    public required int RuntimeVersion { get; init; }

    public required string CompilerVersion { get; init; }

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public IReadOnlyList<string> MaterialReferences { get; init; } = [];

    public IReadOnlyList<string> TextureReferences { get; init; } = [];

    public IReadOnlyList<AssetLodReference> Lods { get; init; } = [];

    public string? CollisionReference { get; init; }

    public IReadOnlyList<string> AnimationReferences { get; init; } = [];

    public IReadOnlyList<AssetSocket> Sockets { get; init; } = [];

    public AssetBounds? Bounds { get; init; }
}

public sealed record RuntimeAssetManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required string CompilerVersion { get; init; }

    public IReadOnlyList<RuntimeAssetRecord> Assets { get; init; } = [];
}
