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
    double CpuSubmissionMilliseconds = 0.0);
