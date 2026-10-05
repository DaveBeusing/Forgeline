using BenchmarkDotNet.Attributes;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.World;

namespace ForgeLine.Rendering.Benchmarks;

[MemoryDiagnoser]
public class TerrainBenchmarks : IDisposable
{
    private TerrainChunk _chunk = null!;
    private TerrainRenderer _renderer = null!;
    private TerrainRenderer _highCoverageRenderer = null!;
    private RtsCamera _closeCamera = null!;
    private RtsCamera _camera = null!;
    private RtsCamera _strategicCamera = null!;
    private NullGraphicsCommandContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        var settings = new WorldGridSettings();
        _chunk = DevelopmentTerrainFactory.CreateChunk(
            new ChunkCoordinate(0, 0),
            settings);

        TerrainWorld world =
            DevelopmentTerrainFactory.CreateRepresentativeWorld(
                settings,
                chunkRadius: 3);

        var graphics = new NullGraphicsDevice();
        _renderer = new TerrainRenderer(graphics, world);
        TerrainWorld highCoverageWorld =
            DevelopmentTerrainFactory.CreateRepresentativeWorld(
                settings,
                chunkRadius: 8);
        _highCoverageRenderer =
            new TerrainRenderer(
                graphics,
                highCoverageWorld);
        _closeCamera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialDistance = 120.0f,
                MaximumDistance = 1_200.0f
            });
        _camera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialDistance = 420.0f,
                MaximumDistance = 1_200.0f
            });
        _strategicCamera = new RtsCamera(
            new RtsCameraSettings
            {
                InitialDistance = 900.0f,
                MaximumDistance = 1_200.0f
            });
        _context = new NullGraphicsCommandContext();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _renderer?.Dispose();
        _highCoverageRenderer?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public TerrainMeshData GenerateTerrainChunkMesh() =>
        TerrainMeshGenerator.Generate(_chunk);

    [Benchmark]
    public TerrainRenderDiagnostics SubmitCloseTacticalTerrain()
    {
        _renderer.Render(
            _context,
            _closeCamera);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public TerrainRenderDiagnostics SubmitVisibleTerrainChunks()
    {
        _renderer.Render(
            _context,
            _camera);
        return _renderer.LastDiagnostics;
    }

    [Benchmark]
    public TerrainRenderDiagnostics SubmitStrategicHighCoverageTerrain()
    {
        _highCoverageRenderer.Render(
            _context,
            _strategicCamera);
        return _highCoverageRenderer.LastDiagnostics;
    }

    private sealed class NullGraphicsDevice : IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description) =>
            new NullGraphicsPipeline(description);

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            new NullGraphicsBuffer(description);

        public void RenderFrame(
            GraphicsColor clearColor,
            Action<IGraphicsCommandContext>? recordCommands = null)
        {
            recordCommands?.Invoke(new NullGraphicsCommandContext());
        }

        public void Resize(int width, int height)
        {
        }

        public void WaitForIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NullGraphicsPipeline : IGraphicsPipeline
    {
        internal NullGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            Description = description;
        }

        public GraphicsPipelineDescription Description { get; }

        public void Dispose()
        {
        }
    }

    private sealed class NullGraphicsBuffer : IGraphicsBuffer
    {
        internal NullGraphicsBuffer(
            GraphicsBufferDescription description)
        {
            Description = description;
        }

        public GraphicsBufferDescription Description { get; }

        public void SetData<T>(
            ReadOnlySpan<T> data,
            int offsetInBytes = 0)
            where T : unmanaged
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NullGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex => 0;

        public void SetViewport(
            float x,
            float y,
            float width,
            float height)
        {
        }

        public void SetScissor(
            int left,
            int top,
            int right,
            int bottom)
        {
        }

        public void SetPipeline(IGraphicsPipeline pipeline)
        {
        }

        public void SetVertexBuffer(
            IGraphicsBuffer buffer,
            int strideInBytes,
            int offsetInBytes = 0,
            int inputSlot = 0)
        {
        }

        public void SetIndexBuffer(
            IGraphicsBuffer buffer,
            GraphicsIndexFormat format,
            int offsetInBytes = 0)
        {
        }

        public void SetVertexConstants(ReadOnlySpan<float> values)
        {
        }

        public void Draw(int vertexCount, int startVertex = 0)
        {
        }

        public void DrawIndexed(
            int indexCount,
            int startIndex = 0,
            int baseVertex = 0)
        {
        }

        public void DrawIndexedInstanced(
            int indexCount,
            int instanceCount,
            int startIndex = 0,
            int baseVertex = 0,
            int startInstance = 0)
        {
        }
    }
}
