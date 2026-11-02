using ForgeLine.Graphics;
using ForgeLine.Presentation.RenderContracts;

namespace ForgeLine.Presentation;

internal sealed class ChunkDecorationRenderer(SimpleInstanceRenderer renderer) : IDisposable
{
    private readonly ChunkDecorationResidency _residency = new();
    private ChunkDecorationSnapshot? _source;
    private ForgeLine.Simulation.SimulationSessionId _session;
    internal ChunkDecorationDiagnostics Diagnostics { get; private set; }
    internal void Prepare(RtsCamera camera, RenderWorld source, in RenderPreparationRequest request,
        ChunkDecorationRenderSettings settings)
    {
        var snapshot = source.CurrentSnapshot ?? throw new InvalidOperationException("No presentation snapshot.");
        var decorations = snapshot.ChunkDecorations ?? throw new InvalidOperationException("No decoration snapshot.");
        _residency.Prepare(decorations, snapshot.SessionId, camera, request, settings);
        if (!ReferenceEquals(decorations, _source)) renderer.ResetDecorationBindings();
        renderer.Prepare(camera, _residency.World, request with { Alpha = 1 });
        _source = decorations; _session = snapshot.SessionId;
        Diagnostics = _residency.Diagnostics;
    }
    internal void Validate(IGraphicsCommandContext context, RtsCamera camera, RenderWorld source,
        System.Numerics.Matrix4x4? shadow = null)
    {
        if (!ReferenceEquals(source.CurrentSnapshot?.ChunkDecorations, _source) || source.CurrentSnapshot?.SessionId != _session)
            throw new InvalidOperationException("Prepare the current decoration snapshot before recording.");
        _residency.AssertPrepared(camera, context.Width, context.Height, shadow);
    }
    internal void Render(IGraphicsCommandContext context, RtsCamera camera, RenderWorld source, double? seconds)
    {
        Validate(context, camera, source);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        renderer.Render(context, camera, _residency.World, 1, seconds);
        Diagnostics = _residency.Diagnostics with { Shadow = Diagnostics.Shadow,
            CullingAndAdmissionMilliseconds = renderer.DecorationCullingMilliseconds,
            UploadPackingMilliseconds = renderer.DecorationUploadMilliseconds,
            SubmissionMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, Work = renderer.WorkMetrics, Render = renderer.LastDiagnostics,
            UploadBytes = renderer.SubmissionMetrics.UploadBytes,
            InstanceBufferBytes = renderer.ResidentInstanceBufferBytes, SharedAssets = renderer.MaterialDiagnostics };
    }
    internal void RenderShadow(IGraphicsCommandContext context, RtsCamera camera, RenderWorld source,
        GraphicsDirectionalShadowFrame frame, int casters, int draws, double? seconds)
    {
        Validate(context, camera, source, frame.ViewProjection);
        renderer.RenderShadow(context, camera, _residency.World, 1, frame, casters, draws, seconds);
        Diagnostics = Diagnostics with { Shadow = renderer.LastShadowDiagnostics };
    }
    internal GraphicsPostFrameBatch[] CapturePostFrameIntent(GraphicsPostFrameRoster roster, int capturedSlot)
        => renderer.CapturePostFrameMainIntent(roster, capturedSlot);
    internal void Observe(GraphicsDiagnostics diagnostics) => renderer.ObserveFrameDiagnostics(diagnostics);
    internal void Submit() => renderer.Submit();
    internal void Retire() => renderer.Retire();
    public void Dispose() => renderer.Dispose();
}
