using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Versioned field topology, independent of camera, frame, simulation RNG and extraction state.
internal static class ResourceDepositFieldComposition
{
    internal const int MaximumClustersPerDeposit = 24;
    internal const int MaximumSnapshotClusters = 4096;
    internal const int MaximumCachedDeposits = 4096;

    private static readonly string[][] Layouts =
    [
        ["layered_shelf", "faulted_ridge", "breccia_fan", "offset_seam"],
        ["cleaved_plate", "crystal_fan", "stepped_bed", "split_rosette"],
        ["sealed_nodule", "fractured_lens", "laminated_pocket", "weathered_saddle"],
        ["inclusion_reef", "split_matrix", "radial_inclusions", "faulted_pocket"]
    ];
    private static readonly string[] Families = ["ferrous_ore", "silicates", "volatiles", "rare_elements"];
    private static readonly Vector3[] TintMinimum = [new(.94f, .92f, .90f), new(.94f, .95f, .94f), new(.93f, .96f, .96f), new(.95f, .93f, .96f)];
    private static readonly Vector3[] TintMaximum = [new(1.04f, 1.02f, 1), new(1.03f, 1.04f, 1.02f), new(1.01f, 1.03f, 1.04f), new(1.03f, 1.01f, 1.04f)];

    internal static bool Supports(ResourceId resource) => Family(resource) >= 0;

    internal static ResourceDepositFieldCluster[] Create(
        ResourceDepositFieldIdentity identity, ResourceId resource, AxisAlignedBounds bounds,
        ITerrainQuery terrain, int budget, RuntimeAssetCatalog? assets)
    {
        int family = Family(resource);
        if (family < 0 || budget <= 0 || !ValidKey(identity.MapKey) || !ValidKey(identity.DepositKey) ||
            !Finite(bounds.Minimum) || !Finite(bounds.Maximum) || !Finite(bounds.Center)) return [];
        Vector3 extent = bounds.Maximum - bounds.Minimum;
        if (!Finite(extent) || extent.X < .5f || extent.Z < .5f) return [];
        int desired = Math.Clamp((int)Math.Min(24, (double)extent.X * extent.Z / 140), 6, MaximumClustersPerDeposit);
        desired = Math.Max(6, desired - (int)(Sample(identity, resource, 0, "field-count") * 5));
        int count = Math.Min(desired, budget);
        var result = new List<ResourceDepositFieldCluster>(count);
        float vein = Sample(identity, resource, 0, "field-vein") * MathF.Tau;
        float cosine = MathF.Cos(vein), sine = MathF.Sin(vein);
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            // Golden-angle disks give every prefix broad field coverage. Alternating
            // core/edge samples retain compact seams and an irregular tapered rim.
            float radial = ordinal == 0 ? 0 : MathF.Sqrt(Sample(identity, resource, ordinal, "field-radius"));
            if (ordinal % 3 == 0) radial *= .55f;
            float angle = ordinal * 2.39996323f + Sample(identity, resource, ordinal, "field-angle") * .65f;
            float u = radial * MathF.Cos(angle), v = radial * MathF.Sin(angle) * .72f;
            float x = (u * cosine - v * sine) * extent.X * .37f;
            float z = (u * sine + v * cosine) * extent.Z * .37f;
            var anchor = new Vector3(bounds.Center.X + x, 0, bounds.Center.Z + z);

            float sizeSample = Sample(identity, resource, ordinal, "size");
            string size = sizeSample < .25f ? "small" : sizeSample < .75f ? "medium" : "large";
            float sizeFactor = sizeSample < .25f ? .65f : sizeSample < .75f ? 1 : 1.25f;
            float diameter = Math.Clamp(MathF.Min(extent.X, extent.Z) * .22f, .25f, 18) * sizeFactor;
            float heightRatio = family switch { 0 => .65f, 1 => .95f, 2 => .38f, _ => 1.05f };
            Vector3 scale = new(diameter * (1 + Sample(identity, resource, ordinal, "field-stretch") * .35f),
                diameter * heightRatio, diameter * .8f);
            float yaw = vein + (Sample(identity, resource, ordinal, "rotation") - .5f) * 1.8f;
            if (!TryGround(bounds, anchor, scale, yaw, terrain, out RenderTransform transform)) continue;

            int bucket = Math.Min(99, (int)(Sample(identity, resource, ordinal, "form") * 100));
            int first = family is 0 or 2 ? 40 : 35;
            int layout = bucket < first ? 0 : bucket < 65 ? 1 : bucket < 85 ? 2 : 3;
            string variant = $"mesh.world.resource.{Families[family]}.{Layouts[family][layout]}.{size}";
            string canonical = $"mesh.world.resource.{Families[family]}";
            string material = $"material.world.resource.{Families[family]}";
            // Pair admission is metadata-only. GPU resources still belong to the render owner.
            string mesh = HasPair(assets, variant, material) ? variant : canonical;
            Vector3 tint = Vector3.Lerp(TintMinimum[family], TintMaximum[family], Sample(identity, resource, ordinal, "tint"));
            // Add only a copied binding selection; preserve existing field topology,
            // form/size/rotation/tint channels and depletion/grounding behavior.
            int sizeIndex = size == "small" ? 0 : size == "medium" ? 1 : 2;
            int surface = Math.Min(3, (int)(Sample(identity, resource, ordinal, "surface") * 4));
            result.Add(new(ordinal, transform, mesh, mesh + "_lod1", new(tint, 1), ordinal % 4 == 0)
            {
                Variant = ResourceDepositVariantCatalog.SelectAuthored(WorldPresentationIdentity.ForResource(resource).Visual,
                    layout * 3 + sizeIndex, surface)
            });
        }
        return result.ToArray();
    }

    internal static ResourceDepositField? WithRemaining(ResourceDepositFieldCluster[] layout, double remaining)
    {
        if (layout.Length == 0 || !double.IsFinite(remaining)) return null;
        float fraction = (float)Math.Clamp(remaining, 0, 1);
        var clusters = new List<ResourceDepositFieldCluster>(layout.Length);
        Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        foreach (var cluster in layout)
        {
            // A stable extraction front removes upper bodies, retaining distributed
            // low crust at all quantities. It never changes anchors or resource truth.
            float threshold = .15f + (cluster.Ordinal % 7) * .09f;
            float presence = Math.Clamp((fraction - threshold) / .16f, 0, 1);
            if (!cluster.Crust && presence <= 0) continue;
            float vertical = cluster.Crust ? .10f + .90f * fraction : .12f + .88f * presence;
            Vector3 up = Vector3.Transform(Vector3.UnitY, cluster.Transform.Rotation);
            float height = cluster.Transform.Scale.Y;
            var transform = cluster.Transform with
            {
                Scale = cluster.Transform.Scale with { Y = height * vertical },
                Position = cluster.Transform.Position - up * (height * (1 - vertical) * .5f)
            };
            var adapted = cluster with { Transform = transform };
            clusters.Add(adapted);
            var bounds = Bounds(transform);
            minimum = Vector3.Min(minimum, bounds.Minimum);
            maximum = Vector3.Max(maximum, bounds.Maximum);
        }
        return clusters.Count == 0 ? null : new(clusters.ToArray(), new(minimum, maximum));
    }

    internal static AxisAlignedBounds Bounds(in RenderTransform transform)
    {
        Vector3 half = transform.Scale * .5f;
        Vector3 extents = Vector3.Abs(Vector3.Transform(new Vector3(half.X, 0, 0), transform.Rotation)) +
            Vector3.Abs(Vector3.Transform(new Vector3(0, half.Y, 0), transform.Rotation)) +
            Vector3.Abs(Vector3.Transform(new Vector3(0, 0, half.Z), transform.Rotation));
        return new(transform.Position - extents, transform.Position + extents);
    }

    internal static float Sample(ResourceDepositFieldIdentity identity, ResourceId resource, int ordinal, string channel)
    {
        // Length-prefixed ASCII and little-endian values; no runtime string hashes.
        // Field schema extends visual/1 with topology channels, anisotropy and depletion.
        byte[] map = Encoding.ASCII.GetBytes(identity.MapKey), deposit = Encoding.ASCII.GetBytes(identity.DepositKey);
        byte[] name = Encoding.ASCII.GetBytes(channel), prefix = "FORGELINE.resource-field/1\0"u8.ToArray();
        byte[] bytes = new byte[prefix.Length + 28 + map.Length + deposit.Length + name.Length];
        prefix.CopyTo(bytes, 0);
        int offset = prefix.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), identity.Salt); offset += 8;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), resource.Value); offset += 4;
        WriteString(map);
        WriteString(deposit);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint)ordinal)); offset += 4;
        WriteString(name);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes, digest);
        return MathF.Min(MathF.BitDecrement(1f), (float)(BinaryPrimitives.ReadUInt32LittleEndian(digest) / 4294967296.0));

        void WriteString(byte[] value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), checked((uint)value.Length)); offset += 4;
            value.CopyTo(bytes, offset); offset += value.Length;
        }
    }

    private static bool TryGround(AxisAlignedBounds field, Vector3 anchor, Vector3 scale, float yaw,
        ITerrainQuery terrain, out RenderTransform transform)
    {
        transform = default;
        float margin = MathF.Min(MathF.Min(anchor.X - field.Minimum.X, field.Maximum.X - anchor.X),
            MathF.Min(anchor.Z - field.Minimum.Z, field.Maximum.Z - anchor.Z));
        float radius = MathF.Min(MathF.Max(scale.X, scale.Z) * .5f, margin * .8f);
        if (radius < .01f) return false;
        // Initial probes fit the horizon. Invalid/missing terrain never fabricates height zero.
        if (!Height(anchor.X, anchor.Z, out float center) ||
            !Height(anchor.X - radius, anchor.Z, out float left) || !Height(anchor.X + radius, anchor.Z, out float right) ||
            !Height(anchor.X, anchor.Z - radius, out float near) || !Height(anchor.X, anchor.Z + radius, out float far)) return false;
        Vector3 normal = Vector3.Normalize(new(left - right, radius * 2, near - far));
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        if (normal.Y >= .85f)
        {
            Vector3 axis = Vector3.Cross(Vector3.UnitY, normal);
            if (axis.LengthSquared() > 1e-8f)
                rotation = Quaternion.Normalize(Quaternion.Concatenate(rotation,
                    Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(normal.Y))));
        }
        else
        {
            // Steep banks retain upright low mineral crust, not leaning towers.
            scale *= .6f;
            scale.Y *= .25f;
        }
        Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);
        var candidate = new RenderTransform(anchor + up * (scale.Y * .5f), rotation, scale);
        Vector3 half = (Bounds(candidate).Maximum - Bounds(candidate).Minimum) * .5f;
        float allowedX = MathF.Min(anchor.X - field.Minimum.X, field.Maximum.X - anchor.X);
        float allowedZ = MathF.Min(anchor.Z - field.Minimum.Z, field.Maximum.Z - anchor.Z);
        // Include tilt of the entire source cube, not just its bottom footprint.
        float shrink = MathF.Min(1, MathF.Min(allowedX / (half.X * 2), allowedZ / (half.Z * 2)));
        if (!float.IsFinite(shrink) || shrink <= 0) return false;
        scale *= shrink;
        float sink = 0;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 baseOffset = Vector3.Transform(new Vector3((corner % 2 == 0 ? -1 : 1) * scale.X * .5f,
                0, (corner < 2 ? -1 : 1) * scale.Z * .5f), rotation);
            if (!Height(anchor.X + baseOffset.X, anchor.Z + baseOffset.Z, out float height)) return false;
            sink = MathF.Min(sink, height - center - baseOffset.Y);
        }
        anchor.Y = center + sink - MathF.Min(.12f, scale.Y * .05f);
        transform = new(anchor + up * (scale.Y * .5f), rotation, scale);
        return Finite(transform.Position) && Finite(transform.Scale);

        bool Height(float x, float z, out float height)
        {
            height = 0;
            return x >= field.Minimum.X && x <= field.Maximum.X && z >= field.Minimum.Z && z <= field.Maximum.Z &&
                terrain.TrySampleHeight(x, z, out height) && float.IsFinite(height);
        }
    }

    private static bool HasPair(RuntimeAssetCatalog? assets, string mesh, string material) =>
        assets is not null && assets.TryGet(AssetId.Parse(material), out var surface) && surface is { Type: RuntimeAssetType.Material } &&
        MeshRecord(assets, mesh, material) && MeshRecord(assets, mesh + "_lod1", material);

    private static bool MeshRecord(RuntimeAssetCatalog assets, string mesh, string material) =>
        assets.TryGet(AssetId.Parse(mesh), out var record) && record is { Type: RuntimeAssetType.Mesh } &&
        record.MaterialReferences.Count == 1 && string.Equals(record.MaterialReferences[0], material, StringComparison.Ordinal);

    private static int Family(ResourceId resource) => resource.Value switch { 1 => 0, 3 => 1, 2 => 2, 8 => 3, _ => -1 };
    private static bool ValidKey(string value) => !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.All(static c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
