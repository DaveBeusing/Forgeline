namespace ForgeLine.Presentation;

public readonly record struct TerrainRenderDiagnostics(
    int TotalChunks,
    int VisibleChunks,
    int CulledChunks,
    long SubmittedTriangles,
    int DrawCalls,
    int UploadedBufferCount,
    int ControlTextureCount = 0,
    int TextureBindingsPerDraw = 0,
    int MaximumTextureSamplesPerPixel = 0,
    double CpuSubmissionMilliseconds = 0.0)
{
    public bool SurfaceDetailEnabled { get; init; }
    public int RootSignatureDwords { get; init; }
    public int ResidentChunks { get; init; }
    public int RequiredChunks { get; init; }
    public long ResidentGeometryBytes { get; init; }
    public long ResidentControlBytes { get; init; }
    public long ChunkUploads { get; init; }
    public long ChunkEvictions { get; init; }
    public double ResidencyCpuMilliseconds { get; init; }
}
