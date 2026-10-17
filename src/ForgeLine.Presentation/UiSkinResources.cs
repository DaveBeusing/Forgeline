using System.Runtime.CompilerServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

/// <summary>Creating-thread leases; last release retires textures through the existing device fences.</summary>
public sealed class UiSkinResources : IDisposable
{
    public const string AssetName = "ui.skin.directorate.compiled";
    private static readonly ConditionalWeakTable<IGraphicsDevice, DeviceCache> Cache = new();
    private readonly IGraphicsDevice _device;
    private readonly string _key;
    private readonly Shared _shared;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _disposed;
    public RuntimeUiSkinData Data { get { Check(); return _shared.Data; } }
    public long PageBytes { get { Check(); return _shared.Bytes; } }
    public int TextureCount { get { Check(); return _shared.Textures.Count(t => t is not null); } }
    public int LeaseCount { get { Check(); return _shared.Users; } }
    public int Uploads => TextureCount;
    internal UiSkinSprite Sprite(string id)
    {
        Check();
        return _shared.Sprites.TryGetValue(id, out var s) && _shared.Textures[s.Page] is not null ? s
            : throw new KeyNotFoundException($"Required prepared UI sprite '{id}' is missing. Reinstall the complete player.");
    }
    internal IGraphicsTexture Texture(int page) { Check(); return _shared.Textures[page] ?? throw new InvalidOperationException("UI group was not prepared."); }

    public UiSkinResources(IGraphicsDevice device, RuntimeAssetCatalog catalog, UiSkinGroup group = UiSkinGroup.Core, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device); ArgumentNullException.ThrowIfNull(catalog);
        if (!Enum.IsDefined(group)) throw new ArgumentOutOfRangeException(nameof(group));
        _device = device;
        RuntimeAssetIntegrity.ValidateManifest(catalog.Manifest);
        _key = catalog.Manifest.ContentSignature + ":" + group;
        cancellationToken.ThrowIfCancellationRequested();
        var cache = Cache.GetOrCreateValue(device);
        if (cache.Thread != _thread) throw new InvalidOperationException("UI cache belongs to its creating graphics thread.");
        var entries = cache.Entries;
        if (entries.TryGetValue(_key, out var shared))
        {
            if (shared.Thread != _thread) throw new InvalidOperationException("UI resource leases belong to their creating graphics thread.");
            _shared = shared; shared.Users++; return;
        }
        var id = AssetId.Parse(AssetName);
        var record = catalog.Get(id);
        if (record.Type != RuntimeAssetType.UiSkin || record.RuntimeBytes > RuntimeUiSkinData.MaximumPayloadBytes + 16384)
            throw new InvalidDataException("Required compiled UI skin has an invalid type or size. Reinstall the complete player.");
        var data = RuntimeUiSkinData.FromPayload(catalog.Read(id).Payload);
        var textures = new IGraphicsTexture?[data.Pages.Count];
        long bytes = 0;
        try
        {
            for (int i = 0; i < data.Pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var p = data.Pages[i];
                if (p.Group != group) continue;
                if (!record.Dependencies.Contains(p.Id, StringComparer.Ordinal)) throw new InvalidDataException("Required UI page dependency is absent.");
                var pageId = AssetId.Parse(p.Id);
                if (catalog.Get(pageId).Type != RuntimeAssetType.Texture) throw new InvalidDataException("Required UI page is not a texture.");
                var texture = RuntimeTextureData.FromPayload(catalog.Read(pageId).Payload);
                RuntimeUiSkinData.ValidatePage(p, texture);
                cancellationToken.ThrowIfCancellationRequested();
                var mip = texture.Mips[0];
                textures[i] = device.CreateTexture(new(new(texture.Width, texture.Height, texture.Format == RuntimeTextureFormat.Bc7Unorm ? GraphicsTextureFormat.Bc7Unorm : GraphicsTextureFormat.Rgba8Unorm,
                    GraphicsTextureColorSpace.Linear, 1) { DeferUploadWait = true }, [new(mip.Width, mip.Height, mip.RowPitch, mip.Pixels)]));
                bytes += texture.ResidentByteCount;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (bytes == 0) throw new InvalidDataException("Required UI page group is empty.");
            _shared = new(data, textures, bytes, _thread);
            entries.Add(_key, _shared);
        }
        catch { foreach (var texture in textures) texture?.Dispose(); throw; }
    }
    internal void ValidateDevice(IGraphicsDevice device) { Check(); if (!ReferenceEquals(device, _device)) throw new InvalidOperationException("UI pages belong to another device."); }
    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI resources must stay on their graphics thread.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        Check(); _disposed = true;
        if (--_shared.Users != 0) return;
        foreach (var texture in _shared.Textures) texture?.Dispose();
        if (Cache.TryGetValue(_device, out var cache)) { cache.Entries.Remove(_key); if (cache.Entries.Count == 0) Cache.Remove(_device); }
    }
    private sealed class DeviceCache
    {
        public int Thread { get; } = Environment.CurrentManagedThreadId;
        public Dictionary<string, Shared> Entries { get; } = new(StringComparer.Ordinal);
    }
    private sealed class Shared(RuntimeUiSkinData data, IGraphicsTexture?[] textures, long bytes, int thread)
    {
        public RuntimeUiSkinData Data { get; } = data;
        public IGraphicsTexture?[] Textures { get; } = textures;
        public Dictionary<string, UiSkinSprite> Sprites { get; } = data.Sprites.ToDictionary(s => s.Id, StringComparer.Ordinal);
        public long Bytes { get; } = bytes;
        public int Thread { get; } = thread;
        public int Users { get; set; } = 1;
    }
}
