namespace ForgeLine.Presentation.TerrainResidency;

/// <summary>Render-owned budgets; authoritative terrain and global quality settings are unaffected.</summary>
public sealed record TerrainPreparationSettings
{
    public bool GeometryLodEnabled { get; init; } = true;
    public bool PrefetchEnabled { get; init; } = true;
    public int MaximumPreparedChunks { get; init; } = 256;
    public long MaximumPreparedBytes { get; init; } = 32 * 1024 * 1024;
    public long MaximumChunkPreparationBytes { get; init; } = 8 * 1024 * 1024;
    public long MaximumGeometryBytes { get; init; } = 128 * 1024 * 1024;
    public long MaximumControlBytes { get; init; } = 8 * 1024 * 1024;
    public int PrefetchChunksPerStep { get; init; } = 2;
    public float MaximumPixelError { get; init; } = .5f;
    public float MaximumNormalDeviation { get; init; } = .02f;
    public float MaximumTintDeviation { get; init; } = .002f;

    internal void Validate()
    {
        if (MaximumPreparedChunks is < 1 or > 4096 || MaximumPreparedBytes is <= 0 or > 256 * 1024 * 1024 ||
            MaximumChunkPreparationBytes is <= 0 or > 16 * 1024 * 1024 || MaximumChunkPreparationBytes > MaximumPreparedBytes ||
            MaximumGeometryBytes is <= 0 or > 512 * 1024 * 1024 || MaximumControlBytes is <= 0 or > 32 * 1024 * 1024 || PrefetchChunksPerStep is < 1 or > 4 ||
            !float.IsFinite(MaximumPixelError) || MaximumPixelError is <= 0 or > 1 ||
            !float.IsFinite(MaximumNormalDeviation) || MaximumNormalDeviation is < 0 or > .1f ||
            !float.IsFinite(MaximumTintDeviation) || MaximumTintDeviation is < 0 or > .01f)
            throw new ArgumentOutOfRangeException(nameof(TerrainPreparationSettings));
    }
}

public readonly record struct TerrainWorkMetrics
{
    public long MeshBuilds { get; init; }
    public long SplatBuilds { get; init; }
    public long CacheHits { get; init; }
    public long CpuEvictions { get; init; }
    public int PreparedChunks { get; init; }
    public long PreparedBytes { get; init; }
    public int PendingCpuChunks { get; init; }
    public long DiscardedWorkerResults { get; init; }
    public long GeometryUploadBytes { get; init; }
    public long ControlUploadBytes { get; init; }
    public long GeometryEvictionBytes { get; init; }
    public long ControlEvictionBytes { get; init; }
    public long ResidentGeometryBytes { get; init; }
    public long ResidentControlBytes { get; init; }
    public int RequiredChunks { get; init; }
    public int ResidentChunks { get; init; }
    public int IndexPatterns { get; init; }
    public int TextureBindings { get; init; }
    public int MaterialBindings { get; init; }
    public int ReducedMainChunks { get; init; }
    public int ReducedShadowChunks { get; init; }
    public long MainTriangles { get; init; }
    public long ShadowTriangles { get; init; }
    public bool IsPrepared { get; init; }
    public long EvictionCandidateVisits { get; init; }
    public long PreparationSteps { get; init; }
    public double OptionalCpuMilliseconds { get; init; }
    public double MeshMilliseconds { get; init; }
    public double SplatMilliseconds { get; init; }
    public double UploadMilliseconds { get; init; }
    public double MaterialMilliseconds { get; init; }
    public double PrepareMilliseconds { get; init; }
}
