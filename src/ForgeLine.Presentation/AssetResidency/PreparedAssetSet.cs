using System.Diagnostics;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

internal enum AssetResidencyState { Prepared, Uploading, Ready, Failed, Retiring }
internal readonly record struct AssetResidencyProgress(long Generation, AssetResidencyState State, int Assets,
    long ContainerReads, long PayloadBytes, double PreparationMilliseconds, double PublicationMilliseconds, long ResidentMeshBytes,
    long ResidentTextureMipBytes, int Owners);

/// <summary>Bounded startup work. No device calls, tasks, or source-format decoding.</summary>
internal sealed class PreparedAssetSet
{
    internal const long MaximumPayloadBytes = 128 * 1024 * 1024;
    internal const long MaximumContainerBytes = 16 * 1024 * 1024;
    internal const long StepBytes = 4 * 1024 * 1024;
    internal static readonly TimeSpan StepTime = TimeSpan.FromMilliseconds(4);
    private readonly RuntimeAssetCatalog _catalog;
    private readonly Queue<AssetId> _pending = new();
    private readonly HashSet<AssetId> _planned = [];
    private readonly Dictionary<AssetId, Exception> _failures = [];
    internal readonly Dictionary<AssetId, RuntimeMeshData> Meshes = [];
    internal readonly Dictionary<AssetId, RuntimeMaterialData> Materials = [];
    internal readonly Dictionary<AssetId, RuntimeTextureData> Textures = [];
    internal readonly Dictionary<AssetId, RuntimeSkinData> Skins = [];
    internal readonly Dictionary<AssetId, RuntimeAnimationData> Animations = [];
    internal long ContainerReads { get; private set; }
    internal long PayloadBytes { get; private set; }
    internal double PreparationMilliseconds { get; private set; }
    internal bool Complete => _pending.Count == 0;
    internal bool Successful => Complete && _failures.Count == 0;
    internal int AssetCount => _planned.Count;
    internal PreparedAssetSet(RuntimeAssetCatalog catalog)
    {
        _catalog = catalog;
        // A closed superset of every reachable visual state in this immutable catalog.
        // UI/audio-only assets are not roots. LOD, skin, damage/attachment material and clip
        // dependencies are followed, including payload references in legacy containers.
        foreach (var record in catalog.Manifest.Assets.OrderBy(static r => r.Id, StringComparer.Ordinal))
            if (record.Type is RuntimeAssetType.Mesh or RuntimeAssetType.Material) Enqueue(AssetId.Parse(record.Id));
    }
    private void Enqueue(AssetId id)
    {
        if (_planned.Contains(id)) return;
        if (_planned.Count >= 4096) throw new InvalidDataException("Visual dependency plan exceeds 4096 assets.");
        _planned.Add(id); _pending.Enqueue(id);
    }
    internal bool PrepareStep(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        long stepBytes = 0;
        int count = 0;
        try
        {
            while (_pending.TryPeek(out var id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_catalog.TryGet(id, out var record) || record is null)
                {
                    _pending.Dequeue(); count++;
                    _failures.Add(id, new InvalidDataException($"Visual dependency '{id}' is missing."));
                    continue;
                }
                if (count > 0 && (stepBytes + record.RuntimeBytes > StepBytes || Stopwatch.GetElapsedTime(started) >= StepTime || count >= 16)) break;
                _pending.Dequeue(); count++;
                if (record.RuntimeBytes > MaximumContainerBytes)
                {
                    _failures.Add(id, new InvalidDataException($"Asset '{id}' exceeds the bounded container policy."));
                    continue;
                }
                try
                {
                    var content = _catalog.Read(id); ContainerReads++;
                    if (content.Payload.LongLength > MaximumContainerBytes || checked(PayloadBytes + content.Payload.LongLength) > MaximumPayloadBytes)
                        throw new InvalidDataException("Prepared visual payload exceeds the bounded startup policy.");
                    PayloadBytes += content.Payload.LongLength; stepBytes += content.Payload.LongLength;
                    foreach (string dependency in record.Dependencies) Enqueue(AssetId.Parse(dependency));
                    foreach (var lod in record.Lods) Enqueue(AssetId.Parse(lod.AssetId));
                    switch (record.Type)
                    {
                        case RuntimeAssetType.Mesh:
                            var mesh = RuntimeMeshData.FromPayload(content.Payload); Meshes.Add(id, mesh);
                            foreach (var material in mesh.MaterialIds) Enqueue(AssetId.Parse(material));
                            break;
                        case RuntimeAssetType.Material:
                            var materialData = RuntimeMaterialData.FromPayload(content.Payload); Materials.Add(id, materialData);
                            foreach (var texture in new[] { materialData.BaseColorTexture, materialData.NormalTexture, materialData.OrmTexture, materialData.EmissiveTexture })
                                if (texture is { } textureId) Enqueue(textureId);
                            break;
                        case RuntimeAssetType.Texture: Textures.Add(id, RuntimeTextureData.FromPayload(content.Payload)); break;
                        case RuntimeAssetType.Skin: Skins.Add(id, RuntimeSkinData.FromPayload(content.Payload)); break;
                        case RuntimeAssetType.Animation: Animations.Add(id, RuntimeAnimationData.FromPayload(content.Payload)); break;
                        default: throw new InvalidDataException($"Unsupported visual dependency type '{record.Type}'.");
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or KeyNotFoundException)
                { _failures.Add(id, exception); }
            }
        }
        finally { PreparationMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
        return Complete;
    }
    internal void RequireSuccess()
    {
        if (!Complete) throw new InvalidOperationException("Visual preparation is incomplete.");
        if (_failures.Count > 0)
        {
            var failure = _failures.OrderBy(static x => x.Key.Value, StringComparer.Ordinal).First();
            throw new InvalidDataException($"Required visual asset '{failure.Key}' failed preparation.", failure.Value);
        }
    }
    internal T Get<T>(AssetId id, Dictionary<AssetId, T> values)
    {
        if (_failures.TryGetValue(id, out var failure)) throw new InvalidDataException($"Visual asset '{id}' failed preparation.", failure);
        return values.TryGetValue(id, out var value) ? value : throw new InvalidDataException($"Visual asset '{id}' has no prepared {typeof(T).Name}.");
    }
}
