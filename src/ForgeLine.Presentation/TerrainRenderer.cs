using System.Runtime.InteropServices;
using ForgeLine.Graphics.RenderContracts;
using System.Diagnostics;
using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum TerrainDebugVisualizationMode : byte
{
    None = 0,
    ControlRed = 1,
    ControlGreen = 2,
    ControlBlue = 3,
    ControlAlpha = 4,
    MaterialPalette = 5
}

public sealed class TerrainRenderer : IDisposable
{
    private const int TerrainBaseRootConstantCount = 36;
    private const int TerrainRootConstantCount =
        TerrainBaseRootConstantCount +
        SceneLightingSettings.ShaderConstantCount + 1;
    private const int TerrainTextureCount = 13;
    private const int LayerCount = TerrainChunkSplatData.LayerCount;

    private readonly IGraphicsPipeline _pipeline;
    private readonly IGraphicsPipeline? _shadowPipeline;
    private readonly List<TerrainChunkRenderResource> _resources;
    private readonly IGraphicsDevice _graphics;
    private readonly IReadOnlyList<TerrainChunk> _chunks;
    private readonly TerrainChunkRenderResource?[] _resident;
    private readonly bool[] _required;
    private readonly TerrainMeshSettings _meshSettings;
    private readonly TerrainPresentationProfile _profile;
    private readonly TerrainResidencySettings _residency;
    private long _generation, _uploads, _evictions;
    private int _requiredCount;
    private double _residencyMilliseconds;
    private Matrix4x4? _preparedView, _preparedShadow;
    private readonly RuntimeWorldAssetResources? _runtimeAssets;
    private readonly IGraphicsTexture? _fallbackBaseColor;
    private readonly IGraphicsTexture? _fallbackNormal;
    private readonly IGraphicsTexture? _fallbackOrm;
    private readonly SceneLightingSettings _lighting;
    private readonly TerrainSurfaceDetailSettings _surfaceDetail;

    private bool _disposed;

    public TerrainRenderer(
        IGraphicsDevice graphics,
        TerrainWorld world,
        TerrainMeshSettings? meshSettings = null,
        TerrainPresentationProfile? presentationProfile = null,
        RuntimeAssetCatalog? runtimeAssets = null,
        SceneLightingSettings? lighting = null,
        TerrainSurfaceDetailSettings? surfaceDetail = null, TextureDetailQuality textureDetail = TextureDetailQuality.Full,
        TerrainResidencySettings? residency = null, WorldEnvironmentSettings? environment = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(world);
        _graphics = graphics;
        _chunks = world.Chunks;
        _resident = new TerrainChunkRenderResource?[_chunks.Count];
        _required = new bool[_chunks.Count];
        _residency = residency ?? TerrainResidencySettings.Eager;
        _residency.Validate();
        WorldEnvironmentSettings appearance = environment ?? new();
        appearance.Validate();

        _lighting =
            lighting ??
            SceneLightingSettings.Default;
        _lighting.Validate();
        _surfaceDetail = surfaceDetail ?? TerrainSurfaceDetailSettings.Default;
        _surfaceDetail.Validate();

        TerrainMeshSettings resolvedMeshSettings =
            meshSettings ??
            new TerrainMeshSettings();
        TerrainPresentationProfile resolvedProfile =
            presentationProfile ??
            TerrainPresentationProfile.CreateCentralDivide(
                runtimeAssets);
        resolvedMeshSettings.Validate();
        _meshSettings = resolvedMeshSettings;
        _profile = resolvedProfile;

        var resources = new List<TerrainChunkRenderResource>(Math.Min(world.Chunks.Count, _residency.MaximumResidentChunks));
        try
        {
            _pipeline =
                TerrainMaterialPipeline.CreateTerrainPipeline(
                    graphics, _surfaceDetail, appearance);
            if (graphics.DirectionalShadows.Enabled) _shadowPipeline = DirectionalShadowCasterPipeline.Create(graphics, false);
            _runtimeAssets =
                runtimeAssets is null
                    ? null
                    : new RuntimeWorldAssetResources(
                        graphics,
                        runtimeAssets, textureDetail);

            if (_runtimeAssets is null)
            {
                _fallbackBaseColor =
                    CreateSolidTexture(
                        graphics,
                        255,
                        255,
                        255,
                        255,
                        GraphicsTextureColorSpace.Srgb);
                _fallbackNormal =
                    CreateSolidTexture(
                        graphics,
                        128,
                        128,
                        255,
                        255,
                        GraphicsTextureColorSpace.Linear);
                _fallbackOrm =
                    CreateSolidTexture(
                        graphics,
                        255,
                        255,
                        0,
                        255,
                        GraphicsTextureColorSpace.Linear);
            }

            if (!_residency.Enabled)
            {
                for (int index = 0; index < _chunks.Count; index++)
                {
                    var resource = LoadChunk(index);
                    _resident[index] = resource;
                    resources.Add(resource);
                    _uploads++;
                }
            }
            _resources =
                resources;
        }
        catch
        {
            foreach (TerrainChunkRenderResource resource in resources)
            {
                resource.Dispose();
            }

            _fallbackOrm?.Dispose();
            _fallbackNormal?.Dispose();
            _fallbackBaseColor?.Dispose();
            _runtimeAssets?.Dispose();
            _pipeline?.Dispose();
            _shadowPipeline?.Dispose();
            throw;
        }

        LastDiagnostics =
            new TerrainRenderDiagnostics(
                _chunks.Count,
                0,
                _chunks.Count,
                0,
                0,
                checked(
                    _resources.Count *
                    2),
                _resources.Count,
                TerrainTextureCount,
                TerrainTextureCount,
                0.0);
    }

    private TerrainChunkRenderResource LoadChunk(int chunkIndex)
    {
        TerrainChunk chunk = _chunks[chunkIndex];
        TerrainMeshData mesh =
            TerrainMeshGenerator.Generate(
                chunk,
                _meshSettings);
        TerrainChunkSplatData splat =
            TerrainSplatMapBuilder.Build(
                chunk,
                _profile);
        TerrainRenderVertex[] renderVertices =
            CreateRenderVertices(
                mesh.Vertices);
        IGraphicsTexture controlTexture =
            _graphics.CreateTexture(
                splat.CreateTextureData());
        IGraphicsBuffer? vertexBuffer = null;
        IGraphicsBuffer? indexBuffer = null;
        try
        {
            TerrainLayerResource[] layers =
                CreateLayers(
                    splat.Palette,
                    _profile);

            ulong vertexBytes =
                checked(
                    (ulong)renderVertices.Length *
                    TerrainRenderVertex.SizeInBytes);
            ulong indexBytes =
                checked(
                    (ulong)mesh.Indices.Length *
                    sizeof(uint));

            vertexBuffer = _graphics.CreateInitializedStaticBuffer(
                new(vertexBytes, StaticGraphicsBufferUsage.Vertex), MemoryMarshal.AsBytes(renderVertices.AsSpan()));
            indexBuffer = _graphics.CreateInitializedStaticBuffer(
                new(indexBytes, StaticGraphicsBufferUsage.Index), MemoryMarshal.AsBytes(mesh.Indices.AsSpan()));

            return
                new TerrainChunkRenderResource(
                    chunk.Coordinate,
                    mesh.Bounds,
                    vertexBuffer,
                    indexBuffer,
                    controlTexture,
                    layers,
                    mesh.Indices.Length,
                    mesh.TriangleCount, chunkIndex, splat.Mips.Sum(static mip => (long)mip.Pixels.Length));
        }
        catch
        {
            indexBuffer?.Dispose();
            vertexBuffer?.Dispose();
            controlTexture.Dispose();
            throw;
        }
    }

    /// <summary>Call on the render owner before frame recording, with the world viewport and shadow coverage.</summary>
    public void Prepare(RtsCamera camera, int width, int height, GraphicsDirectionalShadowFrame? shadow = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(camera);
        if (_graphics.IsRecording) throw new InvalidOperationException("Prepare terrain residency before frame recording.");
        if (!_residency.Enabled) return;
        long started = Stopwatch.GetTimestamp();
        ViewFrustum view = ViewFrustum.FromViewProjection(camera.GetMatrices(width, height).ViewProjection);
        ViewFrustum? shadowView = null;
        if (shadow is { } frame) { frame.Validate(); shadowView = ViewFrustum.FromViewProjection(frame.ViewProjection); }
        int required = 0, missing = 0;
        for (int index = 0; index < _chunks.Count; index++)
        {
            bool needed = view.Intersects(_chunks[index].Bounds) || shadowView?.Intersects(_chunks[index].Bounds) == true;
            _required[index] = needed;
            if (!needed) continue;
            required++;
            if (_resident[index] is null) missing++;
        }
        if (required > _residency.MaximumResidentChunks)
            throw new InvalidOperationException($"Terrain view requires {required} chunks; residency limit is {_residency.MaximumResidentChunks}. Increase the limit before rendering; required coverage is never dropped.");
        _requiredCount = required;
        long generation = ++_generation;
        foreach (var resource in _resources)
            if (_required[resource.ChunkIndex]) resource.LastRequired = generation;
        while (_resources.Count + missing > _residency.MaximumResidentChunks)
        {
            TerrainChunkRenderResource? oldest = null;
            foreach (var resource in _resources)
                if (!_required[resource.ChunkIndex] && (oldest is null || resource.LastRequired < oldest.LastRequired)) oldest = resource;
            if (oldest is null) throw new InvalidOperationException("Terrain residency has no safe eviction candidate.");
            _resources.Remove(oldest);
            _resident[oldest.ChunkIndex] = null;
            oldest.Dispose();
            _evictions++;
        }
        for (int index = 0; index < _chunks.Count; index++)
        {
            if (!_required[index] || _resident[index] is not null) continue;
            var resource = LoadChunk(index);
            resource.LastRequired = generation;
            _resident[index] = resource;
            _resources.Add(resource);
            _uploads++;
        }
        if (missing > 0) _resources.Sort(static (a, b) => a.ChunkIndex.CompareTo(b.ChunkIndex));
        _preparedView = camera.GetMatrices(width, height).ViewProjection;
        _preparedShadow = shadow?.ViewProjection;
        _residencyMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
    public bool DebugChunksEnabled { get; set; } = true;

    public TerrainDebugVisualizationMode DebugVisualizationMode { get; set; }

    public SceneLightingSettings Lighting =>
        _lighting;

    public TerrainRenderDiagnostics LastDiagnostics { get; private set; }

    public RuntimeMaterialDiagnostics MaterialDiagnostics =>
        _runtimeAssets?.MaterialDiagnostics ??
        default;

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);

        long startedAt =
            Stopwatch.GetTimestamp();
        CameraMatrices matrices =
            camera.GetMatrices(
                context.Width,
                context.Height);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(
                matrices.ViewProjection);
        if (_residency.Enabled && _preparedView != matrices.ViewProjection)
            throw new InvalidOperationException("Terrain camera or viewport changed without preparing residency.");

        int visibleChunks = 0;
        int drawCalls = 0;
        long submittedTriangles = 0;

        context.SetPipeline(
            _pipeline);

        Span<float> constants =
            stackalloc float[
                TerrainRootConstantCount];
        WriteMatrix(
            matrices.ViewProjection,
            constants);
        _lighting.WriteShaderConstants(
            constants[
                TerrainBaseRootConstantCount..]);

        foreach (TerrainChunkRenderResource resource in _resources)
        {
            if (!frustum.Intersects(
                    resource.Bounds))
            {
                continue;
            }

            visibleChunks++;
            submittedTriangles +=
                resource.TriangleCount;

            constants[16] = BitConverter.Int32BitsToSingle(((int)DebugVisualizationMode << 1) | (DebugChunksEnabled ? 1 : 0));
            constants[17] = camera.Position.X;
            constants[18] = 0;
            constants[19] = camera.Position.Y;
            constants[50] = camera.Position.Z;
            WriteLayerConstants(
                resource.Layers,
                constants);

            context.SetVertexConstants(
                constants);
            context.SetVertexBuffer(
                resource.VertexBuffer,
                TerrainRenderVertex.SizeInBytes);
            context.SetIndexBuffer(
                resource.IndexBuffer,
                GraphicsIndexFormat.ThirtyTwoBit);

            BindTerrainTextures(
                context,
                resource);

            context.DrawIndexed(
                resource.IndexCount);
            drawCalls++;
        }

        double submissionMilliseconds =
            Stopwatch.GetElapsedTime(
                    startedAt)
                .TotalMilliseconds;

        LastDiagnostics =
            new TerrainRenderDiagnostics(
                _chunks.Count,
                visibleChunks,
                _chunks.Count -
                visibleChunks,
                submittedTriangles,
                drawCalls,
                checked(
                    _resources.Count *
                    2),
                _resources.Count,
                TerrainTextureCount,
                TerrainTextureCount,
                submissionMilliseconds)
            {
                SurfaceDetailEnabled = _surfaceDetail.Enabled,
                RootSignatureDwords = _shadowPipeline is null ? TerrainRootConstantCount + TerrainTextureCount : 18,
                ResidentChunks = _resources.Count,
                RequiredChunks = _residency.Enabled ? _requiredCount : _resources.Count,
                ResidentGeometryBytes = GeometryBytes(),
                ResidentControlBytes = ControlBytes(),
                ChunkUploads = _uploads, ChunkEvictions = _evictions,
                ResidencyCpuMilliseconds = _residencyMilliseconds
            };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (TerrainChunkRenderResource resource in _resources)
        {
            resource.Dispose();
        }

        _fallbackOrm?.Dispose();
        _fallbackNormal?.Dispose();
        _fallbackBaseColor?.Dispose();
        _runtimeAssets?.Dispose();
        _pipeline.Dispose();
        _shadowPipeline?.Dispose();
        _disposed = true;
    }

    public int RenderShadow(IGraphicsCommandContext context, GraphicsDirectionalShadowFrame frame, int maximumDraws)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        frame.Validate();
        if (_residency.Enabled && _preparedShadow != frame.ViewProjection)
            throw new InvalidOperationException("Prepare terrain shadow coverage before frame recording.");
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDraws);
        context.SetPipeline(_shadowPipeline ?? throw new NotSupportedException("Terrain shadows were not configured at construction."));
        Span<float> constants = stackalloc float[16];
        WriteMatrix(frame.ViewProjection, constants);
        context.SetVertexConstants(constants);
        ViewFrustum frustum = ViewFrustum.FromViewProjection(frame.ViewProjection);
        int draws = 0;
        foreach (TerrainChunkRenderResource resource in _resources)
        {
            if (draws >= maximumDraws) break;
            if (!frustum.Intersects(resource.Bounds)) continue;
            context.SetVertexBuffer(resource.VertexBuffer, TerrainRenderVertex.SizeInBytes);
            context.SetIndexBuffer(resource.IndexBuffer, GraphicsIndexFormat.ThirtyTwoBit);
            context.DrawIndexed(resource.IndexCount);
            draws++;
        }
        return draws;
    }

    private TerrainLayerResource[] CreateLayers(
        IReadOnlyList<TerrainMaterialSlot> palette,
        TerrainPresentationProfile profile)
    {
        var layers =
            new TerrainLayerResource[
                LayerCount];

        for (int index = 0;
             index < layers.Length;
             index++)
        {
            TerrainMaterialSlot slot =
                palette[index];
            TerrainMaterialDefinition definition =
                profile.GetMaterial(
                    slot);
            RuntimeMaterialResources material =
                _runtimeAssets is null
                    ? CreateFallbackMaterial(
                        definition)
                    : _runtimeAssets.ResolveMaterial(
                        definition.AssetId);
            float tileMeters =
                float.IsFinite(
                    definition.TextureTileMeters) &&
                definition.TextureTileMeters >
                    0.0f
                    ? definition.TextureTileMeters
                    : 128.0f;
            Vector2 worldUvScale =
                material.UvScale /
                tileMeters;

            layers[index] =
                new TerrainLayerResource(
                    slot,
                    material,
                    worldUvScale);
        }

        return layers;
    }

    private RuntimeMaterialResources CreateFallbackMaterial(
        in TerrainMaterialDefinition definition) =>
        new(
            AssetId.Parse(
                definition.AssetId),
            _fallbackBaseColor!,
            _fallbackNormal!,
            _fallbackOrm!,
            _fallbackBaseColor!,
            new Vector4(
                definition.BaseColor,
                1.0f),
            definition.Roughness,
            definition.Metallic,
            0.0f,
            Vector2.One,
            false);

    private static void BindTerrainTextures(
        IGraphicsCommandContext context,
        TerrainChunkRenderResource resource)
    {
        context.SetPixelTexture(
            0,
            resource.ControlTexture);

        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            RuntimeMaterialResources material =
                resource.Layers[layer]
                    .Material;

            context.SetPixelTexture(
                1 +
                layer,
                material.BaseColorTexture);
            context.SetPixelTexture(
                5 +
                layer,
                material.NormalTexture);
            context.SetPixelTexture(
                9 +
                layer,
                material.OrmTexture);
        }
    }

    private static TerrainRenderVertex[] CreateRenderVertices(
        TerrainVertex[] vertices)
    {
        var result =
            new TerrainRenderVertex[
                vertices.Length];

        for (int index = 0;
             index < result.Length;
             index++)
        {
            TerrainVertex vertex =
                vertices[index];
            float macro =
                CalculateMacroVariation(
                    vertex.Position);

            result[index] =
                new TerrainRenderVertex(
                    vertex.Position,
                    vertex.Normal,
                    vertex.LocalUv,
                    new Vector4(
                        macro,
                        macro,
                        macro,
                        1.0f));
        }

        return result;
    }

    internal static float CalculateMacroVariation(
        Vector3 position)
    {
        float broad =
            MathF.Sin(
                position.X *
                    0.0019f +
                position.Z *
                    0.0013f);
        float secondary =
            MathF.Sin(
                position.X *
                    0.0047f -
                position.Z *
                    0.0039f +
                MathF.Sin(
                    position.Z *
                        0.0011f));

        return Math.Clamp(
            1.0f +
            broad *
                0.055f +
            secondary *
                0.025f,
            0.90f,
            1.08f);
    }

    private static void WriteLayerConstants(
        IReadOnlyList<TerrainLayerResource> layers,
        Span<float> constants)
    {
        if (layers.Count !=
            LayerCount)
        {
            throw new InvalidOperationException(
                $"Terrain rendering requires {LayerCount} active material layers.");
        }

        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            TerrainLayerResource resource =
                layers[layer];
            RuntimeMaterialResources material =
                resource.Material;
            int uvOffset =
                20 +
                layer *
                2;
            constants[uvOffset] =
                resource.WorldUvScale.X;
            constants[uvOffset + 1] =
                resource.WorldUvScale.Y;

            constants[28 + layer] =
                BitConverter.UInt32BitsToSingle(
                    PackRgb10(
                        material.BaseColorFactor));
            constants[32 + layer] =
                BitConverter.UInt32BitsToSingle(
                    PackRg16(
                        material.RoughnessFactor,
                        material.MetallicFactor));
        }

        uint packedPalette =
            (uint)layers[0].Slot |
            ((uint)layers[1].Slot << 8) |
            ((uint)layers[2].Slot << 16) |
            ((uint)layers[3].Slot << 24);
        constants[18] =
            BitConverter.UInt32BitsToSingle(
                packedPalette);
    }

    private static uint PackRgb10(
        in Vector4 color)
    {
        uint red =
            PackUnorm(
                color.X,
                1_023);
        uint green =
            PackUnorm(
                color.Y,
                1_023);
        uint blue =
            PackUnorm(
                color.Z,
                1_023);

        return red |
            (green << 10) |
            (blue << 20);
    }

    private static uint PackRg16(
        float first,
        float second)
    {
        uint x =
            PackUnorm(
                first,
                65_535);
        uint y =
            PackUnorm(
                second,
                65_535);

        return x |
            (y << 16);
    }

    private static uint PackUnorm(
        float value,
        uint maximum)
    {
        float clamped =
            Math.Clamp(
                value,
                0.0f,
                1.0f);

        return checked(
            (uint)MathF.Round(
                clamped *
                maximum));
    }

    private readonly record struct TerrainRenderVertex(
        Vector3 Position,
        Vector3 Normal,
        Vector2 LocalUv,
        Vector4 MacroTint)
    {
        public const int SizeInBytes = 48;
    }

    private static IGraphicsTexture CreateSolidTexture(
        IGraphicsDevice graphics,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        GraphicsTextureColorSpace colorSpace) =>
        graphics.CreateTexture(
            new GraphicsTextureData(
                new GraphicsTextureDescription(
                    1,
                    1,
                    GraphicsTextureFormat.Rgba8Unorm,
                    colorSpace,
                    1),
                [
                    new GraphicsTextureMipData(
                        1,
                        1,
                        4,
                        [
                            red,
                            green,
                            blue,
                            alpha
                        ])
                ]));

    private static void WriteMatrix(
        Matrix4x4 matrix,
        Span<float> destination)
    {
        destination[0] = matrix.M11;
        destination[1] = matrix.M12;
        destination[2] = matrix.M13;
        destination[3] = matrix.M14;
        destination[4] = matrix.M21;
        destination[5] = matrix.M22;
        destination[6] = matrix.M23;
        destination[7] = matrix.M24;
        destination[8] = matrix.M31;
        destination[9] = matrix.M32;
        destination[10] = matrix.M33;
        destination[11] = matrix.M34;
        destination[12] = matrix.M41;
        destination[13] = matrix.M42;
        destination[14] = matrix.M43;
        destination[15] = matrix.M44;
    }

    private long GeometryBytes()
    {
        long bytes = 0;
        foreach (var resource in _resources) bytes += checked((long)(resource.VertexBuffer.Description.SizeInBytes + resource.IndexBuffer.Description.SizeInBytes));
        return bytes;
    }

    private long ControlBytes()
    {
        long bytes = 0;
        foreach (var resource in _resources) bytes += resource.ControlBytes;
        return bytes;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private readonly record struct TerrainLayerResource(
        TerrainMaterialSlot Slot,
        RuntimeMaterialResources Material,
        Vector2 WorldUvScale);

    private sealed class TerrainChunkRenderResource : IDisposable
    {
        internal TerrainChunkRenderResource(
            ChunkCoordinate coordinate,
            AxisAlignedBounds bounds,
            IGraphicsBuffer vertexBuffer,
            IGraphicsBuffer indexBuffer,
            IGraphicsTexture controlTexture,
            TerrainLayerResource[] layers,
            int indexCount,
            int triangleCount, int chunkIndex, long controlBytes)
        {
            ChunkIndex = chunkIndex; ControlBytes = controlBytes;
            Coordinate =
                coordinate;
            Bounds =
                bounds;
            VertexBuffer =
                vertexBuffer;
            IndexBuffer =
                indexBuffer;
            ControlTexture =
                controlTexture;
            Layers =
                layers ??
                throw new ArgumentNullException(
                    nameof(layers));
            IndexCount =
                indexCount;
            TriangleCount =
                triangleCount;
        }

        internal int ChunkIndex { get; }
        internal long ControlBytes { get; }
        internal long LastRequired { get; set; }
        internal ChunkCoordinate Coordinate { get; }

        internal AxisAlignedBounds Bounds { get; }

        internal IGraphicsBuffer VertexBuffer { get; }

        internal IGraphicsBuffer IndexBuffer { get; }

        internal IGraphicsTexture ControlTexture { get; }

        internal TerrainLayerResource[] Layers { get; }

        internal int IndexCount { get; }

        internal int TriangleCount { get; }

        public void Dispose()
        {
            ControlTexture.Dispose();
            IndexBuffer.Dispose();
            VertexBuffer.Dispose();
        }
    }
}
