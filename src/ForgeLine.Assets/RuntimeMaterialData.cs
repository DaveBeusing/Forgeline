using System.Numerics;
using System.Text.Json;

namespace ForgeLine.Assets;

public sealed record RuntimeMaterialData(
    AssetId? BaseColorTexture,
    AssetId? NormalTexture,
    AssetId? OrmTexture,
    AssetId? EmissiveTexture,
    Vector4 BaseColorFactor,
    float MetallicFactor,
    float RoughnessFactor,
    float EmissiveMultiplier,
    Vector2 UvScale)
{
    public Vector2 UvOffset { get; init; }

    public static RuntimeMaterialData FromPayload(
        ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new InvalidDataException(
                "Runtime material payload is empty.");
        }

        using JsonDocument document =
            JsonDocument.Parse(
                payload.ToArray());
        JsonElement root =
            document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Runtime material payload must contain a JSON object.");
        }

        AssetId? baseColorTexture =
            ReadOptionalAssetId(
                root,
                "baseColorTexture");
        AssetId? normalTexture =
            ReadOptionalAssetId(
                root,
                "normalTexture");
        AssetId? ormTexture =
            ReadOptionalAssetId(
                root,
                "ormTexture");
        AssetId? emissiveTexture =
            ReadOptionalAssetId(
                root,
                "emissiveTexture");

        Vector4 baseColorFactor =
            ReadVector4(
                root,
                "baseColorFactor",
                Vector4.One);
        float metallicFactor =
            ReadUnitFactor(
                root,
                "metallicFactor",
                1.0f);
        float roughnessFactor =
            ReadUnitFactor(
                root,
                "roughnessFactor",
                1.0f);
        float emissiveMultiplier =
            ReadNonNegativeFactor(
                root,
                "emissiveMultiplier",
                1.0f);
        Vector2 uvScale =
            ReadPositiveVector2(
                root,
                "uvScale",
                Vector2.One);

        return new RuntimeMaterialData(
            baseColorTexture,
            normalTexture,
            ormTexture,
            emissiveTexture,
            baseColorFactor,
            metallicFactor,
            roughnessFactor,
            emissiveMultiplier,
            uvScale)
        {
            UvOffset = ReadFiniteVector2(root, "uvOffset")
        };
    }

    private static Vector2 ReadFiniteVector2(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value))
            return Vector2.Zero;
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2 ||
            value[0].ValueKind != JsonValueKind.Number || value[1].ValueKind != JsonValueKind.Number ||
            !value[0].TryGetSingle(out float x) || !float.IsFinite(x) ||
            !value[1].TryGetSingle(out float y) || !float.IsFinite(y))
            throw new InvalidDataException($"Runtime material property '{name}' must contain two finite values.");
        return new Vector2(x, y);
    }

    private static AssetId? ReadOptionalAssetId(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(
                propertyName,
                out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Runtime material property '{propertyName}' must be a string.");
        }

        string? raw =
            value.GetString();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!AssetId.TryParse(
                raw,
                out AssetId id))
        {
            throw new InvalidDataException(
                $"Runtime material property '{propertyName}' contains invalid asset ID '{raw}'.");
        }

        return id;
    }

    private static Vector4 ReadVector4(
        JsonElement root,
        string propertyName,
        Vector4 fallback)
    {
        if (!root.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() != 4)
        {
            throw new InvalidDataException(
                $"Runtime material property '{propertyName}' must contain four values.");
        }

        Span<float> channels =
            stackalloc float[4];
        int index = 0;

        foreach (JsonElement channel in
                 value.EnumerateArray())
        {
            if (!channel.TryGetSingle(
                    out float parsed) ||
                !float.IsFinite(
                    parsed) ||
                parsed < 0.0f)
            {
                throw new InvalidDataException(
                    $"Runtime material property '{propertyName}' contains an invalid channel.");
            }

            channels[index++] =
                parsed;
        }

        return new Vector4(
            channels[0],
            channels[1],
            channels[2],
            channels[3]);
    }

    private static float ReadUnitFactor(
        JsonElement root,
        string propertyName,
        float fallback)
    {
        float value =
            ReadFactor(
                root,
                propertyName,
                fallback);

        if (value < 0.0f ||
            value > 1.0f)
        {
            throw new InvalidDataException(
                $"Runtime material property '{propertyName}' must be between 0 and 1.");
        }

        return value;
    }

    private static float ReadNonNegativeFactor(
        JsonElement root,
        string propertyName,
        float fallback)
    {
        float value =
            ReadFactor(
                root,
                propertyName,
                fallback);

        if (value < 0.0f)
        {
            throw new InvalidDataException(
                $"Runtime material property '{propertyName}' must be non-negative.");
        }

        return value;
    }

    private static float ReadFactor(
        JsonElement root,
        string propertyName,
        float fallback)
    {
        if (!root.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetSingle(
                out float parsed) ||
            !float.IsFinite(
                parsed))
        {
            throw new InvalidDataException(
                $"Runtime material property '{propertyName}' must be finite.");
        }

        return parsed;
    }

    private static Vector2 ReadPositiveVector2(
        JsonElement root,
        string propertyName,
        Vector2 fallback)
    {
        if (!root.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return fallback;
        }

        if (value.ValueKind != JsonValueKind.Array ||
            value.GetArrayLength() != 2)
        {
            throw new InvalidDataException(
                $"Runtime material property '{propertyName}' must contain two values.");
        }

        Span<float> channels =
            stackalloc float[2];
        int index = 0;

        foreach (JsonElement channel in
                 value.EnumerateArray())
        {
            if (!channel.TryGetSingle(
                    out float parsed) ||
                !float.IsFinite(
                    parsed) ||
                parsed <= 0.0f)
            {
                throw new InvalidDataException(
                    $"Runtime material property '{propertyName}' must contain finite positive values.");
            }

            channels[index++] =
                parsed;
        }

        return new Vector2(
            channels[0],
            channels[1]);
    }
}
