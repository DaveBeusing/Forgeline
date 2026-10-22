namespace ForgeLine.Assets;

/// <summary>Offline validation of the bindings consumed by runtime mesh/material rendering.</summary>
internal sealed class RuntimeAssetBindingValidator(RuntimeAssetCatalog catalog)
{
    // Retain descriptions only; decoded texture mip arrays must not accumulate during qualification.
    private readonly Dictionary<AssetId, (RuntimeTextureUsage Usage, RuntimeTextureColorSpace ColorSpace)> _textures = [];
    private readonly Dictionary<AssetId, RuntimeMaterialData> _materials = [];
    private bool Strict => catalog.Manifest.IntegrityVersion != 0;

    internal void ValidateMesh(RuntimeAssetRecord record, RuntimeMeshData mesh)
    {
        if (Strict && !record.MaterialReferences.SequenceEqual(mesh.MaterialIds, StringComparer.Ordinal))
            throw new InvalidDataException($"Mesh '{record.Id}' payload material slots differ from its manifest.");

        foreach (string reference in mesh.MaterialIds)
        {
            var id = RequireReference(record, reference, RuntimeAssetType.Material);
            if (!_materials.TryGetValue(id, out var material))
            {
                material = RuntimeMaterialData.FromPayload(catalog.Read(id).Payload);
                ValidateMaterial(catalog.Get(id), material);
            }
            bool textured = material.BaseColorTexture is not null || material.NormalTexture is not null ||
                material.OrmTexture is not null || material.EmissiveTexture is not null;
            if (textured && !mesh.HasUv0 || material.NormalTexture is not null && !mesh.HasTangents)
                throw new InvalidDataException($"Mesh '{record.Id}' lacks UVs/tangents required by material '{reference}'.");
        }
    }

    internal void ValidateMaterial(RuntimeAssetRecord record, RuntimeMaterialData material)
    {
        Check(material.BaseColorTexture, RuntimeTextureUsage.BaseColor, RuntimeTextureColorSpace.Srgb);
        Check(material.NormalTexture, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear);
        Check(material.OrmTexture, RuntimeTextureUsage.Orm, RuntimeTextureColorSpace.Linear);
        Check(material.EmissiveTexture, RuntimeTextureUsage.Emissive, RuntimeTextureColorSpace.Srgb);
        _materials[AssetId.Parse(record.Id)] = material;

        void Check(AssetId? textureId, RuntimeTextureUsage usage, RuntimeTextureColorSpace colorSpace)
        {
            if (textureId is not { } id) return; // An unauthored slot deliberately uses the renderer's neutral texture.
            RequireReference(record, id.Value, RuntimeAssetType.Texture);
            if (Strict && !record.TextureReferences.Contains(id.Value, StringComparer.Ordinal))
                throw new InvalidDataException($"Material '{record.Id}' omits payload texture '{id}' from its manifest bindings.");
            if (!_textures.TryGetValue(id, out var description))
            {
                var texture = RuntimeTextureData.FromPayload(catalog.Read(id).Payload);
                description = (texture.Usage, texture.ColorSpace);
                _textures.Add(id, description);
            }
            if (description.Usage != usage || description.ColorSpace != colorSpace)
                throw new InvalidDataException($"Material '{record.Id}' texture '{id}' requires {usage}/{colorSpace}, got {description.Usage}/{description.ColorSpace}.");
        }
    }

    private AssetId RequireReference(RuntimeAssetRecord owner, string reference, RuntimeAssetType type)
    {
        if (!AssetId.TryParse(reference, out var id) || !catalog.TryGet(id, out var target) || target is null || target.Type != type ||
            Strict && !owner.Dependencies.Contains(reference, StringComparer.Ordinal))
            throw new InvalidDataException($"Asset '{owner.Id}' payload has an invalid {type} binding '{reference}'.");
        return id;
    }
}
