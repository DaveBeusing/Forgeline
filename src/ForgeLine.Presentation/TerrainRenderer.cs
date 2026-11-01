using System.Runtime.InteropServices;
using ForgeLine.Presentation.TerrainResidency;
using ForgeLine.Graphics.RenderContracts;
using System.Diagnostics;
using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Game;
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

public sealed partial class TerrainRenderer : IDisposable
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
    private bool[] _required;
    private bool[] _mainRequired, _scratchRequired, _scratchMain;
    private readonly bool[] _prefetchTried, _prefetchRejected;
    private readonly TerrainMeshSettings _meshSettings;
    private readonly TerrainPresentationProfile _profile;
    private readonly TerrainResidencySettings _residency;
    private long _generation, _uploads, _evictions;
    private int _requiredCount;
    private double _residencyMilliseconds;
    private Matrix4x4? _preparedView, _preparedShadow, _planView, _planShadow;
    private int _preparedWidth, _preparedHeight, _planWidth, _planHeight, _planCursor, _missing;
    private TerrainChunkRenderResource? _oldest, _newest;
    private Vector3 _lastTarget, _prefetchTarget;
    private bool _hasTarget, _prefetchExhausted;
    private long _evictionVisits, _preparationSteps, _lastShadowTriangles;
    private int _lastTextureBindings, _lastMaterialBindings, _reducedMainChunks, _reducedShadowChunks;
    private readonly int[] _mainLods, _shadowLods;
    private RtsCamera? _lodCamera;
    private readonly Func<int, CancellationToken, PreparedTerrainChunk> _buildCpu;
    private readonly RuntimeWorldAssetResources? _runtimeAssets;
    private readonly IGraphicsTexture? _fallbackBaseColor;
    private readonly IGraphicsTexture? _fallbackNormal;
    private readonly IGraphicsTexture? _fallbackOrm;
    private readonly SceneLightingSettings _lighting;
    private readonly TerrainSurfaceDetailSettings _surfaceDetail;

    private readonly TerrainPreparationSettings _preparation;
    private readonly TerrainPreparedSource _source;
    private readonly TerrainPreparedCache _prepared;
    private readonly TerrainPreparationWorker _worker = new();
    private readonly Dictionary<uint, TerrainPalette> _palettes = [];
    private IGraphicsBuffer? _sharedIndices;
    private long _meshBuilds, _splatBuilds, _geometryUploadBytes, _controlUploadBytes;
    private long _geometryEvictionBytes, _controlEvictionBytes;
    private double _meshMilliseconds, _splatMilliseconds, _uploadMilliseconds, _materialMilliseconds;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _disposed;

    public TerrainRenderer(
        BattlefieldTerrainVisualDefinition visual, IGraphicsDevice graphics, TerrainWorld world,
        IEnvironmentQuery? environmentQuery = null, TerrainInfrastructureMask? infrastructure = null,
        TerrainMeshSettings? meshSettings = null, RuntimeAssetCatalog? runtimeAssets = null,
        SceneLightingSettings? lighting = null, TerrainSurfaceDetailSettings? surfaceDetail = null,
        TextureDetailQuality textureDetail = TextureDetailQuality.Full, TerrainResidencySettings? residency = null,
        WorldEnvironmentSettings? environment = null, TerrainPreparationSettings? preparation = null)
        : this(graphics, world, meshSettings,
            TerrainVisualProfileResolver.Resolve(visual, world, environmentQuery, infrastructure, runtimeAssets),
            runtimeAssets, lighting, surfaceDetail, textureDetail, residency, environment, preparation)
    {
    }

    public TerrainRenderer(
        IGraphicsDevice graphics,
        TerrainWorld world,
        TerrainMeshSettings? meshSettings = null,
        TerrainPresentationProfile? presentationProfile = null,
        RuntimeAssetCatalog? runtimeAssets = null,
        SceneLightingSettings? lighting = null,
        TerrainSurfaceDetailSettings? surfaceDetail = null, TextureDetailQuality textureDetail = TextureDetailQuality.Full,
        TerrainResidencySettings? residency = null, WorldEnvironmentSettings? environment = null,
        TerrainPreparationSettings? preparation = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(world);
        _graphics = graphics;
        _chunks = world.Chunks;
        _resident = new TerrainChunkRenderResource?[_chunks.Count];
        _required = new bool[_chunks.Count];
        _mainRequired = new bool[_chunks.Count]; _scratchRequired = new bool[_chunks.Count]; _scratchMain = new bool[_chunks.Count];
        _prefetchTried = new bool[_chunks.Count]; _prefetchRejected = new bool[_chunks.Count];
        _mainLods = new int[_chunks.Count]; _shadowLods = new int[_chunks.Count];
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
        _preparation = preparation ?? new(); _preparation.Validate();
        _source = new(world, resolvedMeshSettings, resolvedProfile, _preparation);
        _buildCpu = _source.Build;
        _chunks = _source.Chunks;
        if (_source.Patterns.Bytes >= _preparation.MaximumPreparedBytes)
            throw new ArgumentOutOfRangeException(nameof(preparation), "Shared terrain indices exceed the CPU preparation budget.");
        _prepared = new(_preparation with { MaximumPreparedBytes = _preparation.MaximumPreparedBytes - _source.Patterns.Bytes });

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

            EnsureRequiredBudgets(0);
            long indexStarted = Stopwatch.GetTimestamp();
            _sharedIndices = graphics.CreateInitializedStaticBuffer(
                new((ulong)_source.Patterns.Bytes, StaticGraphicsBufferUsage.Index),
                MemoryMarshal.AsBytes(_source.Patterns.Indices.AsSpan()));
            _geometryUploadBytes += _source.Patterns.Bytes;
            _uploadMilliseconds += Stopwatch.GetElapsedTime(indexStarted).TotalMilliseconds;
            if (!_residency.Enabled)
            {
                EnsureRequiredBudgets(_chunks.Count);
                for (int index = 0; index < _chunks.Count; index++)
                {
                    var resource = LoadChunk(index);
                    _resident[index] = resource;
                    resources.Add(resource); Touch(resource);
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

            _sharedIndices?.Dispose();
            _prepared.Clear(); _worker.Dispose(); _palettes.Clear();
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
                checked(_resources.Count + 1),
                _resources.Count,
                TerrainTextureCount,
                TerrainTextureCount,
                0.0);
    }

    private TerrainChunkRenderResource LoadChunk(int chunkIndex)
    {
        ThrowIfDisposed(); AssetPreparationScope.ThrowIfStopped();
        PreparedTerrainChunk? prepared = _prepared.Get(chunkIndex);
        if (prepared is null)
        {
            prepared = _source.Build(chunkIndex, CancellationToken.None);
            ObserveBuild(prepared); _prepared.Admit(chunkIndex, prepared);
        }
        AssetPreparationScope.ThrowIfStopped();
        TerrainChunk chunk = _chunks[chunkIndex];
        TerrainPalette? palette = null;
        IGraphicsTexture? control = null; IGraphicsBuffer? vertex = null;
        try
        {
            long started = Stopwatch.GetTimestamp();
            palette = AcquirePalette(prepared.Splat.Palette);
            _materialMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            control = _graphics.CreateTexture(prepared.Splat.CreateTextureData());
            long vertexBytes = (long)prepared.Vertices.Length * TerrainRenderVertex.SizeInBytes;
            vertex = _graphics.CreateInitializedStaticBuffer(new((ulong)vertexBytes, StaticGraphicsBufferUsage.Vertex),
                MemoryMarshal.AsBytes(prepared.Vertices.AsSpan()));
            _geometryUploadBytes += vertexBytes; _controlUploadBytes += prepared.ControlBytes;
            _uploadMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            TerrainIndexRange full = _source.Patterns.Ranges[0];
            return new(chunk.Coordinate, chunk.Bounds, vertex, _sharedIndices!, control, palette.Layers,
                full.IndexCount, full.IndexCount / 3, chunkIndex, prepared.ControlBytes)
            { PaletteKey = palette.Key, HeightErrors = prepared.HeightErrors, NormalErrors = prepared.NormalErrors, TintErrors = prepared.TintErrors, ProtectedSurface = prepared.ProtectedSurface };
        }
        catch
        {
            vertex?.Dispose(); control?.Dispose();
            if (palette is not null) ReleasePalette(palette.Key);
            throw;
        }
    }

    private void ObserveBuild(PreparedTerrainChunk data)
    {
        _meshBuilds++; _splatBuilds++; _meshMilliseconds += data.MeshMilliseconds;
        _splatMilliseconds += data.SplatMilliseconds;
    }
    private long VertexBytesPerChunk => checked((long)_meshSettings.VertexSamplesPerSide * _meshSettings.VertexSamplesPerSide * TerrainRenderVertex.SizeInBytes);
    private const long ControlBytesPerChunk = PreparedTerrainChunk.ControlPayloadBytes;
    private int ResidentLimit => (int)Math.Min(_residency.MaximumResidentChunks,
        Math.Min((_preparation.MaximumGeometryBytes - _source.Patterns.Bytes) / VertexBytesPerChunk,
            _preparation.MaximumControlBytes / ControlBytesPerChunk));
    private void EnsureRequiredBudgets(int count)
    {
        if (checked(_source.Patterns.Bytes + VertexBytesPerChunk * count) > _preparation.MaximumGeometryBytes ||
            checked(ControlBytesPerChunk * count) > _preparation.MaximumControlBytes)
            throw new InvalidOperationException("Required terrain exceeds its geometry or control-texture byte budget; coverage cannot be dropped.");
    }
    private TerrainPalette AcquirePalette(IReadOnlyList<TerrainMaterialSlot> slots)
    {
        uint key = (uint)slots[0] | (uint)slots[1] << 8 | (uint)slots[2] << 16 | (uint)slots[3] << 24;
        if (!_palettes.TryGetValue(key, out TerrainPalette? palette))
        { palette = new(key, CreateLayers(slots, _profile)); _palettes.Add(key, palette); }
        palette.References++; return palette;
    }
    private void ReleasePalette(uint key)
    {
        TerrainPalette palette = _palettes[key];
        if (--palette.References == 0) _palettes.Remove(key);
    }
    private sealed class TerrainPalette(uint key, TerrainLayerResource[] layers)
    {
        internal readonly uint Key = key;
        internal readonly TerrainLayerResource[] Layers = layers;
        internal int References;
    }
    private void Evict(TerrainChunkRenderResource resource)
    {
        Unlink(resource); _resources.RemoveAt(FindResourceSlot(resource.ChunkIndex)); _resident[resource.ChunkIndex] = null;
        _geometryEvictionBytes += (long)resource.VertexBuffer.Description.SizeInBytes;
        _controlEvictionBytes += resource.ControlBytes;
        resource.Dispose(); ReleasePalette(resource.PaletteKey); _evictions++;
    }
    public TerrainWorkMetrics WorkMetrics => new()
    {
        MeshBuilds = _meshBuilds, SplatBuilds = _splatBuilds, CacheHits = _prepared.Hits,
        CpuEvictions = _prepared.Evictions, PreparedChunks = _prepared.Count,
        PreparedBytes = _prepared.Bytes + _source.Patterns.CpuBytes, PendingCpuChunks = _worker.PendingCount,
        DiscardedWorkerResults = _worker.Discarded, GeometryUploadBytes = _geometryUploadBytes,
        ControlUploadBytes = _controlUploadBytes, GeometryEvictionBytes = _geometryEvictionBytes,
        ControlEvictionBytes = _controlEvictionBytes, ResidentGeometryBytes = GeometryBytes(),
        ResidentControlBytes = ControlBytes(), RequiredChunks = _residency.Enabled ? _requiredCount : _resources.Count, ResidentChunks = _resources.Count,
        IndexPatterns = _source.Patterns.Ranges.Length, MainTriangles = LastDiagnostics.SubmittedTriangles,
        MeshMilliseconds = _meshMilliseconds, SplatMilliseconds = _splatMilliseconds,
        UploadMilliseconds = _uploadMilliseconds, MaterialMilliseconds = _materialMilliseconds,
        PrepareMilliseconds = _residencyMilliseconds, OptionalCpuMilliseconds = _worker.CpuMilliseconds,
        EvictionCandidateVisits = _evictionVisits, PreparationSteps = _preparationSteps,
        IsPrepared = IsPrepared, TextureBindings = _lastTextureBindings, MaterialBindings = _lastMaterialBindings,
        ShadowTriangles = _lastShadowTriangles, ReducedMainChunks = _reducedMainChunks, ReducedShadowChunks = _reducedShadowChunks
    };

    /// <summary>Drain bounded preparation steps on the render owner before recording. No partial view is published.</summary>
    public void Prepare(RtsCamera camera, int width, int height, GraphicsDirectionalShadowFrame? shadow = null)
    {
        long started = Stopwatch.GetTimestamp();
        while (!PrepareStep(camera, width, height, shadow)) { }
        _residencyMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    public bool IsPrepared => !_disposed && (!_residency.Enabled || _preparedView.HasValue);

    /// <summary>Admit at most the requested new chunks (also a 4 MiB/4 ms soft step limit). False forbids recording this view.</summary>
    public bool PrepareStep(RtsCamera camera, int width, int height, GraphicsDirectionalShadowFrame? shadow = null, int maximumNewChunks = 2)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(camera);
        if (maximumNewChunks is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(maximumNewChunks));
        if (_graphics.IsRecording) throw new InvalidOperationException("Prepare terrain residency before frame recording.");
        AssetPreparationScope.ThrowIfStopped();
        CameraMatrices matrices = camera.GetMatrices(width, height);
        if (shadow is { } frame) frame.Validate();
        if (!_residency.Enabled) return true;
        long started = Stopwatch.GetTimestamp(); _preparationSteps++;
        HarvestOptional();
        Matrix4x4? shadowMatrix = shadow?.ViewProjection;
        if (_planView != matrices.ViewProjection || _planShadow != shadowMatrix || _planWidth != width || _planHeight != height)
        {
            ViewFrustum main = ViewFrustum.FromViewProjection(matrices.ViewProjection);
            ViewFrustum? light = shadowMatrix.HasValue ? ViewFrustum.FromViewProjection(shadowMatrix.Value) : null;
            int required = 0, missing = 0;
            for (int i = 0; i < _chunks.Count; i++)
            {
                bool visible = main.Intersects(_chunks[i].Bounds);
                bool needed = visible || light?.Intersects(_chunks[i].Bounds) == true;
                _scratchMain[i] = visible; _scratchRequired[i] = needed;
                if (needed) { required++; if (_resident[i] is null) missing++; }
            }
            if (required > _residency.MaximumResidentChunks)
                throw new InvalidOperationException($"Terrain view requires {required} chunks; residency limit is {_residency.MaximumResidentChunks}. Required coverage is never dropped.");
            EnsureRequiredBudgets(required); AssetPreparationScope.ThrowIfStopped();
            _preparedView = _preparedShadow = null;
            (_required, _scratchRequired) = (_scratchRequired, _required);
            (_mainRequired, _scratchMain) = (_scratchMain, _mainRequired);
            _requiredCount = required; _missing = missing; _planCursor = 0;
            _planView = matrices.ViewProjection; _planShadow = shadowMatrix; _planWidth = width; _planHeight = height;
            _generation++;
            foreach (var resource in _resources)
                if (_required[resource.ChunkIndex]) { resource.LastRequired = _generation; Touch(resource); }
            Vector3 movement = _hasTarget ? camera.Target - _lastTarget : Vector3.Zero;
            float size = _chunks[0].Heightfield.ChunkSizeMeters;
            if (movement.LengthSquared() > size * size * 4) _worker.Cancel();
            if (movement.LengthSquared() > size * size) movement = Vector3.Normalize(movement) * size;
            _prefetchTarget = camera.Target + movement; _lastTarget = camera.Target; _hasTarget = true;
            Array.Clear(_prefetchTried); _prefetchExhausted = false;
        }
        while (_resources.Count + _missing > ResidentLimit)
        {
            TerrainChunkRenderResource candidate = _oldest ?? throw new InvalidOperationException("No terrain eviction candidate.");
            _evictionVisits++;
            if (_required[candidate.ChunkIndex]) throw new InvalidOperationException("Required terrain cannot be evicted.");
            Evict(candidate);
        }
        int admitted = 0; long bytesBefore = _geometryUploadBytes + _controlUploadBytes;
        while (_missing > 0 && _planCursor < _chunks.Count)
        {
            int index = _planCursor;
            if (!_required[index] || _resident[index] is not null) { _planCursor++; continue; }
            AssetPreparationScope.ThrowIfStopped();
            TerrainChunkRenderResource resource = LoadChunk(index);
            _resident[index] = resource; InsertSorted(resource); Touch(resource);
            resource.LastRequired = _generation; _uploads++; _missing--; _planCursor++; admitted++;
            if (admitted >= maximumNewChunks || _geometryUploadBytes + _controlUploadBytes - bytesBefore >= 4 * 1024 * 1024 ||
                Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 4) break;
        }
        if (_missing == 0)
        {
            _preparedView = matrices.ViewProjection; _preparedShadow = shadowMatrix;
            _preparedWidth = width; _preparedHeight = height; ScheduleOptional();
        }
        _residencyMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return _missing == 0;
    }

    private void HarvestOptional()
    {
        TerrainPreparationWorker.Result? result = _worker.Harvest();
        foreach (int failed in _worker.TakeFailures()) _prefetchRejected[failed] = true;
        if (result is null) return;
        for (int i = 0; i < result.Indices.Length; i++)
        { ObserveBuild(result.Chunks[i]); _prepared.Admit(result.Indices[i], result.Chunks[i]); }
    }
    private void ScheduleOptional()
    {
        if (!_preparation.PrefetchEnabled || _worker.Busy || _prefetchExhausted) return;
        long chunkBytes = VertexBytesPerChunk + ControlBytesPerChunk + _source.Patterns.Ranges.Length * 12;
        if (_preparation.MaximumPreparedChunks < _requiredCount + _preparation.PrefetchChunksPerStep ||
            chunkBytes * (_requiredCount + _preparation.PrefetchChunksPerStep) + _source.Patterns.Bytes > _preparation.MaximumPreparedBytes)
        { _prefetchExhausted = true; return; }
        Span<int> planned = stackalloc int[4]; int count = 0;
        for (int item = 0; item < _preparation.PrefetchChunksPerStep; item++)
        {
            int best = -1; float distance = float.PositiveInfinity;
            foreach (var resource in _resources)
            {
                if (!_mainRequired[resource.ChunkIndex]) continue;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int next = _source.Neighbour(resource.ChunkIndex, dx, dz);
                        if (next < 0 || _required[next] || _prefetchTried[next] || _prefetchRejected[next] || _prepared.Contains(next)) continue;
                        var bounds = _chunks[next].Bounds;
                        float score = Vector3.DistanceSquared((bounds.Minimum + bounds.Maximum) * .5f, _prefetchTarget);
                        if (score < distance) { best = next; distance = score; }
                    }
            }
            if (best < 0) break;
            _prefetchTried[best] = true; planned[count++] = best;
        }
        if (count == 0) { _prefetchExhausted = true; return; }
        _worker.Start(planned[..count].ToArray(), _buildCpu);
    }
    private void InsertSorted(TerrainChunkRenderResource resource)
        => _resources.Insert(FindResourceSlot(resource.ChunkIndex), resource);

    private int FindResourceSlot(int chunkIndex)
    {
        int low = 0, high = _resources.Count;
        while (low < high) { int middle = (low + high) / 2; if (_resources[middle].ChunkIndex < chunkIndex) low = middle + 1; else high = middle; }
        return low;
    }
    private void Touch(TerrainChunkRenderResource resource)
    {
        if (resource.InLru) Unlink(resource);
        resource.Previous = _newest; resource.Next = null; resource.InLru = true;
        if (_newest is not null) _newest.Next = resource; else _oldest = resource;
        _newest = resource;
    }
    private void Unlink(TerrainChunkRenderResource resource)
    {
        if (resource.Previous is { } previous) previous.Next = resource.Next; else _oldest = resource.Next;
        if (resource.Next is { } next) next.Previous = resource.Previous; else _newest = resource.Previous;
        resource.Previous = resource.Next = null; resource.InLru = false;
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
        if (_residency.Enabled && (_preparedView != matrices.ViewProjection || _preparedWidth != context.Width || _preparedHeight != context.Height))
            throw new InvalidOperationException("Terrain camera or viewport changed without preparing residency.");

        if (!ReferenceEquals(_lodCamera, camera)) { Array.Clear(_mainLods); _lodCamera = camera; }
        int textureBindings = 0, materialBindings = 0; _reducedMainChunks = 0;
        TerrainLayerResource[]? boundLayers = null;
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

        constants[16] = BitConverter.Int32BitsToSingle(((int)DebugVisualizationMode << 1) | (DebugChunksEnabled ? 1 : 0));
        constants[17] = camera.Position.X; constants[19] = camera.Position.Y; constants[50] = camera.Position.Z;
        context.SetIndexBuffer(_sharedIndices!, GraphicsIndexFormat.ThirtyTwoBit);
        foreach (TerrainChunkRenderResource resource in _resources)
        {
            if (!frustum.Intersects(
                    resource.Bounds))
            {
                continue;
            }

            visibleChunks++;
            int level = TerrainLodSelector.Select(resource.HeightErrors, resource.NormalErrors, resource.TintErrors,
                TerrainLodProjection.Main(resource.Bounds, matrices, context.Width, context.Height), _preparation,
                _mainLods[resource.ChunkIndex], resource.ProtectedSurface);
            _mainLods[resource.ChunkIndex] = level; if (level > 0) _reducedMainChunks++;
            TerrainIndexRange range = _source.Patterns.Ranges[level];
            submittedTriangles += range.IndexCount / 3;
            bool materialChanged = !ReferenceEquals(boundLayers, resource.Layers);
            if (materialChanged)
            {
                WriteLayerConstants(resource.Layers, constants); context.SetVertexConstants(constants);
                boundLayers = resource.Layers; materialBindings++;
            }
            context.SetVertexBuffer(resource.VertexBuffer, TerrainRenderVertex.SizeInBytes);
            textureBindings += BindTerrainTextures(context, resource, materialChanged);
            context.DrawIndexed(range.IndexCount, range.FirstIndex);
            drawCalls++;
        }

        double submissionMilliseconds =
            Stopwatch.GetElapsedTime(
                    startedAt)
                .TotalMilliseconds;

        _lastTextureBindings = textureBindings; _lastMaterialBindings = materialBindings;
        LastDiagnostics =
            new TerrainRenderDiagnostics(
                _chunks.Count,
                visibleChunks,
                _chunks.Count -
                visibleChunks,
                submittedTriangles,
                drawCalls,
                checked(_resources.Count + 1),
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

        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Dispose terrain on its creating render owner.");
        foreach (TerrainChunkRenderResource resource in _resources)
        {
            resource.Dispose();
        }
        _resources.Clear(); Array.Clear(_resident); _oldest = _newest = null; _lodCamera = null; _preparedView = _preparedShadow = null;
        _worker.Dispose(); _prepared.Clear(); _palettes.Clear(); _source.Patterns.ReleaseCpuIndices();
        _sharedIndices?.Dispose(); _sharedIndices = null;
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
        int draws = 0; long triangles = 0; _reducedShadowChunks = 0;
        context.SetIndexBuffer(_sharedIndices!, GraphicsIndexFormat.ThirtyTwoBit);
        TerrainLodProjection lightProjection = TerrainLodProjection.Shadow(frame.ViewProjection, _graphics.DirectionalShadows.Resolution);
        foreach (TerrainChunkRenderResource resource in _resources)
        {
            if (draws >= maximumDraws) break;
            if (!frustum.Intersects(resource.Bounds)) continue;
            int level = TerrainLodSelector.Select(resource.HeightErrors, resource.NormalErrors, resource.TintErrors,
                lightProjection, _preparation, _shadowLods[resource.ChunkIndex], resource.ProtectedSurface);
            _shadowLods[resource.ChunkIndex] = level; if (level > 0) _reducedShadowChunks++;
            TerrainIndexRange range = _source.Patterns.Ranges[level];
            context.SetVertexBuffer(resource.VertexBuffer, TerrainRenderVertex.SizeInBytes);
            context.DrawIndexed(range.IndexCount, range.FirstIndex); triangles += range.IndexCount / 3;
            draws++;
        }
        _lastShadowTriangles = triangles;
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

    private static int BindTerrainTextures(
        IGraphicsCommandContext context,
        TerrainChunkRenderResource resource, bool bindMaterial)
    {
        context.SetPixelTexture(
            0,
            resource.ControlTexture);
        if (!bindMaterial) return 1;
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
        return TerrainTextureCount;
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

    internal readonly record struct TerrainRenderVertex(
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
        long bytes = _sharedIndices is null ? 0 : (long)_sharedIndices.Description.SizeInBytes;
        foreach (var resource in _resources) bytes += checked((long)resource.VertexBuffer.Description.SizeInBytes);
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
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Terrain GPU resources belong to their creating render owner.");
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

        internal TerrainChunkRenderResource? Previous, Next;
        internal bool InLru;
        internal bool ProtectedSurface { get; init; }
        internal uint PaletteKey { get; init; }
        internal float[] HeightErrors { get; init; } = [];
        internal float[] NormalErrors { get; init; } = [];
        internal float[] TintErrors { get; init; } = [];
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
            VertexBuffer.Dispose();
        }
    }
}
