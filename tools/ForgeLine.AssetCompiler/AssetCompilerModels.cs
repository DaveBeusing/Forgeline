using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

public enum AssetCompilerDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed record AssetCompilerDiagnostic(
    string Code,
    AssetCompilerDiagnosticSeverity Severity,
    string Message,
    string? AssetId = null,
    string? SourcePath = null);

public sealed record AssetCompilationResult(
    bool Success,
    int CompiledCount,
    int SkippedCount,
    IReadOnlyList<AssetCompilerDiagnostic> Diagnostics);

public sealed record SourceAssetDefinition
{
    public required string Id { get; init; }

    public required RuntimeAssetType Type { get; init; }

    public required string Source { get; init; }

    public RuntimeTextureUsage TextureUsage { get; init; } =
        RuntimeTextureUsage.Color;

    public RuntimeTextureColorSpace TextureColorSpace { get; init; } =
        RuntimeTextureColorSpace.Srgb;

    public float Scale { get; init; } = 1f;

    public IReadOnlyList<string> Dependencies { get; init; } = [];

    public IReadOnlyList<string> MaterialReferences { get; init; } = [];

    public IReadOnlyList<string> TextureReferences { get; init; } = [];

    public IReadOnlyList<AssetLodReference> Lods { get; init; } = [];

    public string? CollisionReference { get; init; }

    public bool CollisionRequired { get; init; }

    public IReadOnlyList<string> AnimationReferences { get; init; } = [];

    public IReadOnlyList<AssetSocket> Sockets { get; init; } = [];
}

public sealed record MaterialSourceDefinition
{
    public string? BaseColorTexture { get; init; }

    public string? NormalTexture { get; init; }

    public string? OrmTexture { get; init; }

    public string? EmissiveTexture { get; init; }

    public float[] BaseColorFactor { get; init; } = [1f, 1f, 1f, 1f];

    public float MetallicFactor { get; init; } = 1f;

    public float RoughnessFactor { get; init; } = 1f;

    public float EmissiveMultiplier { get; init; } = 1f;

    public float[] UvScale { get; init; } = [1f, 1f];

    public IReadOnlyList<string> ReferencedTextureIds =>
        new[] { BaseColorTexture, NormalTexture, OrmTexture, EmissiveTexture }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();
}

internal sealed record ImportedAssetPayload(
    byte[] Payload,
    AssetBounds? Bounds,
    IReadOnlyList<string> AdditionalDependencies);
