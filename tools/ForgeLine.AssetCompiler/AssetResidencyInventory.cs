using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

public sealed record ResidencyTextureInventory(int Width, int Height, string Format, string ColorSpace,
    string Usage, int Mips, int EffectiveFirstMip, long LogicalMipBytes, long EffectiveUploadMipBytes,
    long Rgba8EquivalentBytes);
public sealed record ResidencyInventoryEntry(string Id, string Type, string ContentSignature,
    string Source, string[] Dependencies, string[] MaterialConsumers, long ContainerBytes,
    long PayloadBytes, ResidencyTextureInventory? Texture);
public sealed record AssetResidencyInventoryReport(string ContentSignature, int MipSkip,
    long ContainerBytes, long PayloadBytes, long CompiledGameplayTextureBytes,
    long CompiledStudioTextureBytes, long LogicalTextureMipBytes, long EffectiveUploadMipBytes,
    bool GameplayTextureBudgetPassed, long GameplayTextureBudgetBytes,
    long? ObservedDefaultHeapBytes, long? ObservedStagingBytes, long? ObservedDxgiBudgetBytes,
    IReadOnlyList<ResidencyInventoryEntry> Assets);

/// <summary>Offline container/mip inventory; GPU observations remain unavailable until a native run.</summary>
public static class AssetResidencyInventory
{
    public const long GameplayTextureBudgetBytes = 56 * 1024 * 1024;
    public static AssetResidencyInventoryReport Create(RuntimeAssetCatalog catalog, int mipSkip = 0)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentOutOfRangeException.ThrowIfNegative(mipSkip);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mipSkip, 3);
        var consumers = new Dictionary<AssetId, List<string>>();
        foreach (var material in catalog.Manifest.Assets.Where(static x => x.Type == RuntimeAssetType.Material))
        {
            var data = RuntimeMaterialData.FromPayload(catalog.Read(AssetId.Parse(material.Id)).Payload);
            foreach (var id in new[] { data.BaseColorTexture, data.NormalTexture, data.OrmTexture, data.EmissiveTexture })
                if (id is { } texture)
                {
                    if (!consumers.TryGetValue(texture, out var list)) consumers.Add(texture, list = []);
                    if (!list.Contains(material.Id, StringComparer.Ordinal)) list.Add(material.Id);
                }
        }
        var entries = new List<ResidencyInventoryEntry>(catalog.Manifest.Assets.Count);
        long total = 0, payload = 0, gameplay = 0, studio = 0, logical = 0, upload = 0;
        foreach (var record in catalog.Manifest.Assets.OrderBy(static x => x.Id, StringComparer.Ordinal))
        {
            var id = AssetId.Parse(record.Id);
            var content = catalog.Read(id);
            // The supported FLAS header has five Int32 fields; payload is not container-compressed.
            long container = checked(20L + content.Metadata.LongLength + content.Payload.LongLength);
            total = checked(total + container); payload = checked(payload + content.Payload.LongLength);
            ResidencyTextureInventory? texture = null;
            if (record.Type == RuntimeAssetType.Texture)
            {
                var data = RuntimeTextureData.FromPayload(content.Payload);
                int first = Math.Min(mipSkip, data.Mips.Count - 1);
                while (first > 0 && data.Format == RuntimeTextureFormat.Bc7Unorm &&
                    (data.Mips[first].Width % 4 != 0 || data.Mips[first].Height % 4 != 0)) first--;
                long mipBytes = 0, uploaded = 0, rgba = 0;
                for (int level = 0; level < data.Mips.Count; level++)
                {
                    var mip = data.Mips[level];
                    long bytes = checked((long)mip.RowPitch * (data.Format == RuntimeTextureFormat.Bc7Unorm ? (mip.Height + 3) / 4 : mip.Height));
                    mipBytes = checked(mipBytes + bytes);
                    if (level >= first) uploaded = checked(uploaded + bytes);
                    rgba = checked(rgba + (long)mip.Width * mip.Height * 4);
                }
                texture = new(data.Width, data.Height, data.Format.ToString(), data.ColorSpace.ToString(),
                    data.Usage.ToString(), data.Mips.Count, first, mipBytes, uploaded, rgba);
                logical = checked(logical + mipBytes); upload = checked(upload + uploaded);
                if (record.Id.StartsWith("branding.undefined_behavior.splash.", StringComparison.Ordinal)) studio = checked(studio + container);
                else gameplay = checked(gameplay + container);
            }
            entries.Add(new(record.Id, record.Type.ToString(), record.RuntimeHash, record.SourcePath,
                record.Dependencies.ToArray(), consumers.TryGetValue(id, out var users) ? users.Order(StringComparer.Ordinal).ToArray() : [],
                container, content.Payload.LongLength, texture));
        }
        return new(catalog.Manifest.ContentSignature, mipSkip, total, payload, gameplay, studio, logical, upload,
            gameplay <= GameplayTextureBudgetBytes, GameplayTextureBudgetBytes, null, null, null, entries);
    }
}
