using System.Runtime.InteropServices;
using ForgeLine.Graphics.RenderContracts;
using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed class ResidentAssetStore : IDisposable
{
    private readonly IGraphicsDevice _graphics;
    private readonly RuntimeAssetCatalog _catalog;
    private readonly TextureDetailQuality _textureDetail;
    private readonly Dictionary<AssetId, RuntimeSkinBinding> _skins = [];
    private readonly Dictionary<AssetId, RuntimeAnimationData> _clips = [];
    private readonly Dictionary<AssetId, RuntimeMeshBuffers> _meshes = [];
    private readonly Dictionary<AssetId, RuntimeMaterialResources> _materials = [];
    private readonly Dictionary<AssetId, RuntimeTextureResource> _textures = [];
    private readonly IGraphicsTexture _whiteBaseColor;
    private readonly IGraphicsTexture _flatNormal;
    private readonly IGraphicsTexture _neutralOrm;
    private readonly IGraphicsTexture _blackEmissive;
    private readonly IGraphicsTexture _missingBaseColor;
    private long _materialBindingFailures;
    private long _residentMeshBytes;
    private long _residentClipBytes;
    private long _residentSkinBytes;
    internal const int MaximumLoadedSkins = 8;
    internal const long MaximumSkinCacheBytes = 32 * 1024 * 1024;
    internal const int MaximumLoadedClips = 128;
    internal const long MaximumClipCacheBytes = 16 * 1024 * 1024;
    private bool _disposed;
    private readonly PreparedAssetSet _prepared;
    private long _residentTextureMipBytes;
    private readonly long _residentBudget;
    internal const long MaximumResidentBytes = 96 * 1024 * 1024;
    internal bool RequiredReady { get; private set; }
    internal PreparedAssetSet Prepared => _prepared;
    internal long ResidentTextureMipBytes => _residentTextureMipBytes;

    internal AssetResidencyState State { get; private set; } = AssetResidencyState.Prepared;
    internal double PublicationMilliseconds { get; private set; }
    private readonly int _owner = Environment.CurrentManagedThreadId;
    internal void PrepareRequiredAssets(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (RequiredReady) return;
        long publicationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            _prepared.ValidateBindings();
            State = AssetResidencyState.Uploading;
            var work = _prepared.Meshes.Keys.Select(static id => (Id: id, Mesh: true))
                .Concat(_prepared.Materials.Keys.Select(static id => (Id: id, Mesh: false)))
                .OrderBy(static x => x.Id.Value, StringComparer.Ordinal).ToArray();
            int index = 0;
            while (index < work.Length)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                long bytesBefore = _residentMeshBytes + _residentTextureMipBytes;
                int count = 0;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AssetPreparationScope.ThrowIfStopped();
                    var asset = work[index++]; count++;
                    if (asset.Mesh) TryGetMesh(asset.Id.Value, out _);
                    else if (ResolveMaterial(asset.Id.Value).UsesDevelopmentFallback)
                        throw new InvalidDataException($"Required material '{asset.Id}' has invalid texture bindings.");
                }
                while (index < work.Length && count < 16 &&
                    _residentMeshBytes + _residentTextureMipBytes - bytesBefore < PreparedAssetSet.StepBytes &&
                    System.Diagnostics.Stopwatch.GetElapsedTime(start) < PreparedAssetSet.StepTime);
                // Required startup work drains bounded steps before the first render frame.
                // There is no optional prefetch and no partially ready frame publication.
            }
            cancellationToken.ThrowIfCancellationRequested();
            AssetPreparationScope.ThrowIfStopped();
            RequiredReady = true; State = AssetResidencyState.Ready;
        }
        catch { State = AssetResidencyState.Failed; throw; }
        finally { PublicationMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(publicationStarted).TotalMilliseconds; }
    }
    private void Reserve(long bytes)
    {
        if (checked(_residentMeshBytes + _residentTextureMipBytes + bytes) > _residentBudget)
            throw new InvalidDataException("Required pinned visual assets exceed the 96 MiB resident policy.");
    }

    public ResidentAssetStore(
        IGraphicsDevice graphics,
        RuntimeAssetCatalog catalog, TextureDetailQuality textureDetail = TextureDetailQuality.Full,
        long residentBudget = MaximumResidentBytes)
    {
        _graphics =
            graphics ??
            throw new ArgumentNullException(nameof(graphics));
        _catalog =
            catalog ??
            throw new ArgumentNullException(nameof(catalog));

        if (!Enum.IsDefined(textureDetail)) throw new ArgumentOutOfRangeException(nameof(textureDetail));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(residentBudget);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(residentBudget, MaximumResidentBytes);
        _residentBudget = residentBudget;
        _textureDetail = textureDetail;
        _prepared = new(catalog);
        while (!_prepared.PrepareStep(CancellationToken.None)) { }
        AssetPreparationScope.ThrowIfStopped();


        IGraphicsTexture? whiteBaseColor = null;
        IGraphicsTexture? flatNormal = null;
        IGraphicsTexture? neutralOrm = null;
        IGraphicsTexture? blackEmissive = null;
        IGraphicsTexture? missingBaseColor = null;

        try
        {
            whiteBaseColor =
                CreateSolidTexture(
                    255,
                    255,
                    255,
                    255,
                    GraphicsTextureColorSpace.Srgb);
            flatNormal =
                CreateSolidTexture(
                    128,
                    128,
                    255,
                    255,
                    GraphicsTextureColorSpace.Linear);
            neutralOrm =
                CreateSolidTexture(
                    255,
                    255,
                    0,
                    255,
                    GraphicsTextureColorSpace.Linear);
            blackEmissive =
                CreateSolidTexture(
                    0,
                    0,
                    0,
                    255,
                    GraphicsTextureColorSpace.Srgb);
            missingBaseColor =
                CreateSolidTexture(
                    255,
                    0,
                    255,
                    255,
                    GraphicsTextureColorSpace.Srgb);

            _whiteBaseColor =
                whiteBaseColor!;
            _flatNormal =
                flatNormal!;
            _neutralOrm =
                neutralOrm!;
            _blackEmissive =
                blackEmissive!;
            _missingBaseColor =
                missingBaseColor!;
        }
        catch
        {
            missingBaseColor?.Dispose();
            blackEmissive?.Dispose();
            neutralOrm?.Dispose();
            flatNormal?.Dispose();
            whiteBaseColor?.Dispose();
            throw;
        }
    }

    public RuntimeMaterialDiagnostics MaterialDiagnostics
    {
        get
        {
            ThrowIfDisposed();

            return new RuntimeMaterialDiagnostics(
                _materials.Count,
                _textures.Count,
                _materialBindingFailures) { LoadedMeshCount = _meshes.Count, ResidentMeshBytes = _residentMeshBytes, LoadedAnimationCount = _clips.Count, ResidentAnimationBytes = _residentClipBytes, LoadedSkinCount = _skins.Count, ResidentSkinBytes = _residentSkinBytes };
        }
    }

    public bool TryGetMesh(
        in WorldFeaturePresentationMetadata feature,
        WorldAssetLod lod,
        out RuntimeMeshBuffers mesh) =>
        TryGetMesh(
            feature,
            lod,
            out mesh,
            out _);

    public bool TryGetMesh(
        in WorldFeaturePresentationMetadata feature,
        WorldAssetLod lod,
        out RuntimeMeshBuffers mesh,
        out AssetId assetId)
    {
        if (!feature.IsSpecified)
        {
            mesh = default;
            assetId = default;
            return false;
        }

        WorldPresentationDefinition definition =
            WorldPresentationCatalog.Get(
                feature.Visual);

        return TryGetMesh(
            lod == WorldAssetLod.Reduced
                ? definition.ReducedLodAssetId
                : definition.MeshAssetId,
            out mesh,
            out assetId);
    }

    public bool TryGetMesh(
        string assetId,
        out RuntimeMeshBuffers mesh) =>
        TryGetMesh(
            assetId,
            out mesh,
            out _);

    public bool TryGetMesh(
        string assetId,
        out RuntimeMeshBuffers mesh,
        out AssetId resolvedAssetId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(
            assetId);

        AssetId id =
            AssetId.Parse(
                assetId);

        if (_meshes.TryGetValue(
                id,
                out mesh))
        {
            resolvedAssetId =
                id;
            return true;
        }

        if (!_catalog.TryGet(
                id,
                out RuntimeAssetRecord? record) ||
            record is null ||
            record.Type != RuntimeAssetType.Mesh)
        {
            mesh = default;
            resolvedAssetId = default;
            return false;
        }

        mesh = CreateMeshBuffers(_prepared.Get(id, _prepared.Meshes), record);
        _meshes.Add(id, mesh);
        _residentMeshBytes += checked((long)(mesh.VertexBuffer.Description.SizeInBytes + mesh.IndexBuffer.Description.SizeInBytes));
        resolvedAssetId =
            id;
        return true;
    }

    public RuntimeMaterialResources ResolveMaterial(
        string assetId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(
            assetId);

        AssetId id =
            AssetId.Parse(
                assetId);

        if (_materials.TryGetValue(
                id,
                out RuntimeMaterialResources cached))
        {
            return cached;
        }

        RuntimeMaterialResources material;

        try
        {
            if (!_catalog.TryGet(
                    id,
                    out RuntimeAssetRecord? record) ||
                record is null ||
                record.Type != RuntimeAssetType.Material)
            {
                RecordMaterialBindingFailure(
                    id,
                    "material asset is missing or has the wrong runtime type");
                material =
                    CreateMissingMaterial(
                        id);
            }
            else
            {
                RuntimeMaterialData data = _prepared.Get(id, _prepared.Materials);
                material =
                    CreateMaterialResources(
                        id,
                        data);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            ArgumentException or
            KeyNotFoundException)
        {
            RecordMaterialBindingFailure(
                id,
                exception.Message);
            material =
                CreateMissingMaterial(
                    id);
        }

        _materials.Add(
            id,
            material);
        return material;
    }

    public bool TryGetMaterialTint(
        in WorldFeaturePresentationMetadata feature,
        out Vector4 tint)
    {
        if (!feature.IsSpecified)
        {
            tint = default;
            return false;
        }

        WorldPresentationDefinition definition =
            WorldPresentationCatalog.Get(
                feature.Visual);
        Vector4 baseTint =
            ResolveMaterial(
                definition.MaterialAssetId)
            .BaseColorFactor;

        tint =
            WorldPresentationCatalog.ApplyStateTint(
                feature,
                baseTint);
        return true;
    }

    public bool TryGetMaterialTint(
        string assetId,
        out Vector4 tint)
    {
        tint =
            ResolveMaterial(
                assetId)
            .BaseColorFactor;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        ThrowIfDisposed();
        State = AssetResidencyState.Retiring;
        Exception? failure = null;
        void Release(IDisposable resource)
        {
            try { resource.Dispose(); }
            catch (Exception exception) { failure ??= exception; }
        }
        foreach (var mesh in _meshes.Values) { Release(mesh.IndexBuffer); Release(mesh.VertexBuffer); }
        foreach (var texture in _textures.Values) Release(texture.Texture);
        Release(_missingBaseColor); Release(_blackEmissive); Release(_neutralOrm);
        Release(_flatNormal); Release(_whiteBaseColor);
        _meshes.Clear(); _materials.Clear(); _textures.Clear(); _clips.Clear(); _skins.Clear();
        _prepared.Release();
        _residentMeshBytes = _residentTextureMipBytes = _residentClipBytes = _residentSkinBytes = 0;
        _disposed = true;
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private RuntimeMaterialResources CreateMaterialResources(
        AssetId materialId,
        RuntimeMaterialData data)
    {
        bool hasBindingFailure =
            false;

        IGraphicsTexture baseColor =
            ResolveTexture(
                data.BaseColorTexture,
                RuntimeTextureUsage.Color,
                _whiteBaseColor,
                ref hasBindingFailure);
        IGraphicsTexture normal =
            ResolveTexture(
                data.NormalTexture,
                RuntimeTextureUsage.Normal,
                _flatNormal,
                ref hasBindingFailure);
        IGraphicsTexture orm =
            ResolveTexture(
                data.OrmTexture,
                RuntimeTextureUsage.Orm,
                _neutralOrm,
                ref hasBindingFailure);
        IGraphicsTexture emissive =
            ResolveTexture(
                data.EmissiveTexture,
                RuntimeTextureUsage.Emissive,
                _blackEmissive,
                ref hasBindingFailure);

        Vector4 baseColorFactor =
            hasBindingFailure
                ? new Vector4(
                    1.0f,
                    0.0f,
                    1.0f,
                    1.0f)
                : data.BaseColorFactor;

        if (hasBindingFailure)
        {
            baseColor =
                _missingBaseColor;
        }

        return new RuntimeMaterialResources(
            materialId,
            baseColor,
            normal,
            orm,
            emissive,
            baseColorFactor,
            data.RoughnessFactor,
            data.MetallicFactor,
            data.EmissiveMultiplier,
            data.UvScale,
            hasBindingFailure)
        {
            UvOffset = data.UvOffset
        };
    }

    private RuntimeMaterialResources CreateMissingMaterial(
        AssetId materialId) =>
        new(
            materialId,
            _missingBaseColor,
            _flatNormal,
            _neutralOrm,
            _blackEmissive,
            new Vector4(
                1.0f,
                0.0f,
                1.0f,
                1.0f),
            1.0f,
            0.0f,
            0.0f,
            Vector2.One,
            true);

    private IGraphicsTexture ResolveTexture(
        AssetId? textureId,
        RuntimeTextureUsage expectedUsage,
        IGraphicsTexture fallback,
        ref bool hasBindingFailure)
    {
        if (textureId is null)
        {
            return fallback;
        }

        AssetId id =
            textureId.Value;

        if (_textures.TryGetValue(
                id,
                out RuntimeTextureResource cached))
        {
            if (cached.Usage == expectedUsage)
            {
                return cached.Texture;
            }

            hasBindingFailure =
                true;
            RecordMaterialBindingFailure(
                id,
                $"texture usage {cached.Usage} does not match required usage {expectedUsage}");
            return fallback;
        }

        try
        {
            if (!_catalog.TryGet(
                    id,
                    out RuntimeAssetRecord? record) ||
                record is null ||
                record.Type != RuntimeAssetType.Texture)
            {
                hasBindingFailure =
                    true;
                RecordMaterialBindingFailure(
                    id,
                    "texture asset is missing or has the wrong runtime type");
                return fallback;
            }

            RuntimeTextureData data = _prepared.Get(id, _prepared.Textures);

            RuntimeTextureColorSpace expectedColor = expectedUsage is RuntimeTextureUsage.Color or RuntimeTextureUsage.Emissive
                ? RuntimeTextureColorSpace.Srgb : RuntimeTextureColorSpace.Linear;
            if (data.Usage != expectedUsage || data.ColorSpace != expectedColor)
            {
                hasBindingFailure =
                    true;
                RecordMaterialBindingFailure(
                    id,
                    $"texture usage {data.Usage} does not match required usage {expectedUsage}");
                return fallback;
            }

            IGraphicsTexture texture =
                CreateGraphicsTexture(
                    data);
            _textures.Add(
                id,
                new RuntimeTextureResource(
                    texture,
                    data.Usage));
            return texture;
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            ArgumentException or
            KeyNotFoundException)
        {
            hasBindingFailure =
                true;
            RecordMaterialBindingFailure(
                id,
                exception.Message);
            return fallback;
        }
    }

    private IGraphicsTexture CreateGraphicsTexture(RuntimeTextureData data)
    {
        int skip = Math.Min((int)_textureDetail, data.Mips.Count - 1);
        // BC7 top levels retain block-aligned dimensions; tiny/odd chains keep the nearest usable level.
        while (skip > 0 && data.Format == RuntimeTextureFormat.Bc7Unorm &&
            (data.Mips[skip].Width % 4 != 0 || data.Mips[skip].Height % 4 != 0)) skip--;
        var first = data.Mips[skip];
        long bytes = data.Mips.Skip(skip).Sum(mip => (long)mip.RowPitch * (data.Format == RuntimeTextureFormat.Bc7Unorm ? (mip.Height + 3) / 4 : mip.Height));
        Reserve(bytes);
        var texture = _graphics.CreateTexture(new GraphicsTextureData(
            new GraphicsTextureDescription(first.Width, first.Height,
                data.Format == RuntimeTextureFormat.Bc7Unorm ? GraphicsTextureFormat.Bc7Unorm : GraphicsTextureFormat.Rgba8Unorm,
                data.ColorSpace == RuntimeTextureColorSpace.Srgb ? GraphicsTextureColorSpace.Srgb : GraphicsTextureColorSpace.Linear,
                data.Mips.Count - skip) { DeferUploadWait = true },
            data.Mips.Skip(skip).Select(static mip => new GraphicsTextureMipData(mip.Width, mip.Height, mip.RowPitch, mip.Pixels))));
        _residentTextureMipBytes += bytes;
        return texture;
    }
    private IGraphicsTexture CreateSolidTexture(
        byte red,
        byte green,
        byte blue,
        byte alpha,
        GraphicsTextureColorSpace colorSpace) =>
        _graphics.CreateTexture(
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

    private void RecordMaterialBindingFailure(
        AssetId assetId,
        string reason)
    {
        _materialBindingFailures++;

        Console.WriteLine(
            $"[graphics:material] fallback asset=\"{assetId}\" reason=\"{reason}\"");
    }

    private RuntimeAnimationData ReadAnimation(AssetId id)
    {
        if (_clips.TryGetValue(id, out var cached)) return cached;
        if (_catalog.Get(id).Type != RuntimeAssetType.Animation) throw new InvalidDataException("Skin clip type mismatch.");
        var preparedClip = _prepared.Get(id, _prepared.Animations);
        int payloadLength = preparedClip.ToPayload().Length;
        if (_clips.Count >= MaximumLoadedClips || _residentClipBytes + payloadLength > MaximumClipCacheBytes)
            throw new InvalidDataException("Animation session cache exceeds production bounds.");
        var clip = preparedClip; _clips.Add(id, clip); _residentClipBytes += payloadLength;
        return clip;
    }

    private RuntimeMeshBuffers CreateMeshBuffers(
        RuntimeMeshData data, RuntimeAssetRecord record)
    {
        MechanicalMeshValidation.Validate(record.MechanicalParts, record.Sockets, data.Indices.Count);
        MechanicalPartBinding[] parts = record.MechanicalParts.Select(part =>
        {
            AssetSocket socket = record.Sockets.FirstOrDefault(item => item.Name == part.Socket);
            return new MechanicalPartBinding(part, new Vector3(socket.X, socket.Y, socket.Z));
        }).ToArray();
        RuntimeSkinBinding? skin = null;
        if (record.SkinReference is { } skinReference)
        {
            if (record.MechanicalParts.Count > 0 || record.AnimationReferences.Count > 0) throw new InvalidDataException("Skin conflicts with rigid articulation.");
            var skinId = AssetId.Parse(skinReference);
            if (!_skins.TryGetValue(skinId, out skin))
            {
                var skinRecord = _catalog.Get(skinId);
                if (skinRecord.Type != RuntimeAssetType.Skin) throw new InvalidDataException("Mesh skin type mismatch.");
                var preparedSkin = _prepared.Get(skinId, _prepared.Skins);
                int skinPayloadLength = preparedSkin.ToPayload().Length;
                if (_skins.Count >= MaximumLoadedSkins || _residentSkinBytes + skinPayloadLength > MaximumSkinCacheBytes)
                    throw new InvalidDataException("Skin session cache exceeds production bounds.");
                var skinData = preparedSkin;
                skin = new(skinData, skinRecord.AnimationReferences.Select(reference => ReadAnimation(AssetId.Parse(reference))));
                _skins.Add(skinId, skin); _residentSkinBytes += skinPayloadLength;
            }
            if (!skin.Data.Mesh.ToPayload().AsSpan().SequenceEqual(data.ToPayload())) throw new InvalidDataException("Mesh and skin vertex payload differ.");
        }
        RuntimeAnimationData? clip = null;
        if (record.AnimationReferences.Count > 1) throw new InvalidDataException("Mesh supports one rigid clip.");
        foreach (string clipId in record.AnimationReferences)
        {
            var id = AssetId.Parse(clipId);
            if (!record.Dependencies.Contains(clipId, StringComparer.Ordinal) || _catalog.Get(id).Type != RuntimeAssetType.Animation)
                throw new InvalidDataException("Mesh clip must resolve to an explicit animation dependency.");
            if (!_clips.TryGetValue(id, out clip))
            {
                var preparedClip = _prepared.Get(id, _prepared.Animations);
                int clipPayloadLength = preparedClip.ToPayload().Length;
                if (_clips.Count >= MaximumLoadedClips || _residentClipBytes + clipPayloadLength > MaximumClipCacheBytes)
                    throw new InvalidDataException("Rigid clip session cache exceeds 128 assets or 16MiB.");
                clip = preparedClip;
                _clips.Add(id, clip);
                _residentClipBytes += clipPayloadLength;
            }
            int[] map = RigidAnimationBinding.Validate(clip, record.MechanicalParts, record.Sockets, data);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i] with
                {
                    ClipTranslation = clip.Channels.FirstOrDefault(c => c.Node == map[i] && c.Path == AnimationPath.Translation),
                    ClipRotation = clip.Channels.FirstOrDefault(c => c.Node == map[i] && c.Path == AnimationPath.Rotation)
                };
        }
        MechanicalGeometryBatching.Assign(data, parts);
        RuntimeMeshVertex[] vertices =
            data.Vertices.ToArray();
        uint[] indices =
            data.Indices.ToArray();
        int vertexByteCount =
            checked(
                vertices.Length *
                RuntimeMeshData.VertexStride);
        int indexByteCount =
            checked(
                indices.Length *
                sizeof(uint));

        Reserve(checked((long)vertexByteCount + indexByteCount));
        IGraphicsBuffer vertexBuffer = _graphics.CreateInitializedStaticBuffer(
            new(checked((ulong)vertexByteCount), StaticGraphicsBufferUsage.Vertex), MemoryMarshal.AsBytes(vertices.AsSpan()));
        IGraphicsBuffer? indexBuffer = null;
        try
        {
            indexBuffer = _graphics.CreateInitializedStaticBuffer(
                new(checked((ulong)indexByteCount), StaticGraphicsBufferUsage.Index), MemoryMarshal.AsBytes(indices.AsSpan()));

            int[] materialSlots =
                data.Sections
                    .Select(
                        static section =>
                            section.MaterialSlot)
                    .Distinct()
                    .ToArray();
            bool usesDevelopmentFallback =
                materialSlots.Any(
                    static slot =>
                        slot < 0) && data.MaterialIds.Count > 0 ||
                materialSlots.Length != 1;
            bool hasMaterial =
                !usesDevelopmentFallback &&
                materialSlots.Length == 1 &&
                materialSlots[0] >= 0 &&
                materialSlots[0] < data.MaterialIds.Count;
            AssetId materialId =
                hasMaterial
                    ? AssetId.Parse(
                        data.MaterialIds[
                            materialSlots[0]])
                    : default;

            if (materialSlots.Length > 1)
            {
                Console.WriteLine(
                    "[graphics:mesh] development fallback reason=\"multiple material slots require split draw support\"");
            }

            return new RuntimeMeshBuffers(
                vertexBuffer,
                indexBuffer,
                RuntimeMeshData.VertexStride,
                indices.Length,
                data.Attributes,
                materialId,
                hasMaterial,
                usesDevelopmentFallback,
                data.Sections.Count) { MechanicalParts = parts, Clip = clip, Skin = skin };
        }
        catch
        {
            indexBuffer?.Dispose();
            vertexBuffer.Dispose();
            throw;
        }
    }

    private void ThrowIfDisposed()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Residency belongs to the render owner.");
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

