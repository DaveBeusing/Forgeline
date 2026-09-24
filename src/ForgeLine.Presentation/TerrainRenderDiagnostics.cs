namespace ForgeLine.Presentation;

public readonly record struct TerrainRenderDiagnostics(
    int TotalChunks,
    int VisibleChunks,
    int CulledChunks,
    long SubmittedTriangles,
    int DrawCalls,
    int UploadedChunkBuffers);
