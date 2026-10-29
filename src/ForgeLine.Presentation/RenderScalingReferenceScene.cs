using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

/// <summary>Version-1 render-only same-binding load. No gameplay population or simulated motion.</summary>
public static class RenderScalingReferenceScene
{
    public const int Version = 1;
    public const int MaximumRoots = 100_000;
    public static bool Contains(VisualReferenceSceneKind kind) =>
        kind is >= VisualReferenceSceneKind.Scaling1000High and <= VisualReferenceSceneKind.ScalingTerrainOnly;

    public static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        (int count, bool low) = kind switch
        {
            VisualReferenceSceneKind.Scaling1000High => (1000, false),
            VisualReferenceSceneKind.Scaling1000Low => (1000, true),
            VisualReferenceSceneKind.Scaling10000High => (10000, false),
            VisualReferenceSceneKind.Scaling10000Low => (10000, true),
            VisualReferenceSceneKind.Scaling100000High => (100000, false),
            VisualReferenceSceneKind.Scaling100000Low => (100000, true),
            VisualReferenceSceneKind.ScalingTerrainOnly => (0, false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (!terrain.TrySampleHeight(650, 520, out float height) || !float.IsFinite(height))
            throw new InvalidOperationException("Scaling target requires canonical terrain at 650/520.");
        var target = new Vector3(650, height, 520);
        var previous = new RenderInstance[count];
        var current = new RenderInstance[count];
        int side = (int)Math.Ceiling(Math.Sqrt(count));
        var scale = new Vector3(.1f, .12f, .1f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .01f);
        for (int i = 0; i < count; i++)
        {
            uint hash;
            unchecked
            {
                hash = (uint)i + 2026;
                hash ^= hash >> 16; hash *= 0x7feb352d;
                hash ^= hash >> 15; hash *= 0x846ca68b; hash ^= hash >> 16;
            }
            float x = (i % side - (side - 1) / 2f) * .1f;
            float z = (i / side - (side - 1) / 2f) * .1f;
            x += ((int)(hash & 1023) - 512) / 1024f * .01f;
            z += ((int)((hash >> 10) & 1023) - 512) / 1024f * .01f;
            if (low && i % 10 != 0) x += 4000;
            var position = target + new Vector3(x, 0, z);
            previous[i] = new(new((uint)i + 1, 1), new(position, Quaternion.Identity, scale),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                WorldFeature: new(WorldVisualId.PropCrate, WorldPresentationKind.Prop, default, false));
            current[i] = previous[i] with { Transform = new(position + new Vector3(.02f, 0, .03f), rotation, scale) };
        }
        var before = new PresentationSnapshot(new(31), TimeSpan.FromMilliseconds(50), 0, previous, sessionId: new(1));
        var after = new PresentationSnapshot(new(32), TimeSpan.FromMilliseconds(50), 0, current, sessionId: new(1));
        var binding = WorldPresentationCatalog.Get(WorldVisualId.PropCrate);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes($"render-scaling-v1|2026|{kind}|{binding.MeshAssetId}|{binding.ReducedLodAssetId}|{binding.MaterialAssetId}|31|32|50|1"));
        Span<byte> targetBytes = stackalloc byte[12];
        Write(targetBytes, 0, target.X); Write(targetBytes, 4, target.Y); Write(targetBytes, 8, target.Z);
        digest.AppendData(targetBytes);
        Append(before); Append(after);
        return new(kind, target, after, default, Convert.ToHexString(digest.GetHashAndReset())) { PreviousSnapshot = before };

        void Append(PresentationSnapshot snapshot)
        {
            Span<byte> bytes = stackalloc byte[48];
            foreach (var instance in snapshot.Instances)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, instance.Entity.Index);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes[4..], instance.Entity.Generation);
                var t = instance.Transform;
                Write(bytes, 8, t.Position.X); Write(bytes, 12, t.Position.Y); Write(bytes, 16, t.Position.Z);
                Write(bytes, 20, t.Rotation.X); Write(bytes, 24, t.Rotation.Y); Write(bytes, 28, t.Rotation.Z); Write(bytes, 32, t.Rotation.W);
                Write(bytes, 36, t.Scale.X); Write(bytes, 40, t.Scale.Y); Write(bytes, 44, t.Scale.Z);
                digest.AppendData(bytes);
            }
        }
    }

    private static void Write(Span<byte> bytes, int offset, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes[offset..], BitConverter.SingleToInt32Bits(value));
}
