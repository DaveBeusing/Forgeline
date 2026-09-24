namespace ForgeLine.Presentation;

public readonly record struct DebugDrawRenderDiagnostics(
    int SubmittedLines,
    int RenderedLines,
    int DroppedLines,
    int DrawCalls);
