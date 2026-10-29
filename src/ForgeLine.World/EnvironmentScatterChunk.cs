using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace ForgeLine.World;

/// <summary>28 canonical bytes. Positions are chunk-local X/Z and absolute Y, in millimeters.</summary>
public readonly record struct ScatterInstance(ulong StableId, int LocalXMillimeters, int HeightMillimeters,
    int LocalZMillimeters, ushort SpeciesIndex, ushort YawCentidegrees, ushort ScalePermille,
    byte TintUnorm8, ScatterClassification Classification);

public readonly record struct ScatterTransform(Vector3 PositionMeters, Vector3 SizeMeters, float YawDegrees, float TintMultiplier);

/// <summary>Immutable palette-indexed decoration; never an ECS entity or map-artifact object list.</summary>
public sealed class EnvironmentScatterChunk
{
    private readonly ScatterInstance[] _instances;
    private readonly byte[] _canonical;

    internal EnvironmentScatterChunk(ChunkCoordinate coordinate, EnvironmentScatterProfile profile,
        ScatterInstance[] instances, ReadOnlySpan<byte> header)
    {
        Coordinate = coordinate; Profile = profile; _instances = instances.ToArray();
        _canonical = new byte[checked(header.Length + 12 + instances.Length * 28)];
        header.CopyTo(_canonical);
        var buffer = _canonical.AsSpan(header.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer, coordinate.X);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], coordinate.Z);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], instances.Length);
        buffer = buffer[12..];
        foreach (var item in _instances)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, item.StableId);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], item.LocalXMillimeters);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[12..], item.HeightMillimeters);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[16..], item.LocalZMillimeters);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer[20..], item.SpeciesIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer[22..], item.YawCentidegrees);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer[24..], item.ScalePermille);
            buffer[26] = item.TintUnorm8; buffer[27] = (byte)item.Classification;
            buffer = buffer[28..];
        }
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(_canonical));
    }

    public ChunkCoordinate Coordinate { get; }
    public EnvironmentScatterProfile Profile { get; }
    public ReadOnlySpan<ScatterInstance> Instances => _instances;
    public string Sha256 { get; }
    public byte[] GetCanonicalBytes() => _canonical.ToArray();
    public ScatterTransform GetTransform(int index)
    {
        var i = _instances[index]; var s = Profile.Species[i.SpeciesIndex];
        return new(new((float)(((long)Coordinate.X * 256_000 + i.LocalXMillimeters) / 1000.0), i.HeightMillimeters / 1000f,
                (float)(((long)Coordinate.Z * 256_000 + i.LocalZMillimeters) / 1000.0)),
            new Vector3(s.WidthMillimeters, s.HeightMillimeters, s.DepthMillimeters) * (i.ScalePermille / 1_000_000f),
            i.YawCentidegrees / 100f, i.TintUnorm8 / 255f);
    }

    public WorldGenerationFingerprint CreateDecorationFingerprint(WorldGenerationFingerprint gameplay)
    {
        var records = new List<WorldGenerationContent>();
        for (int offset = 0; offset < _canonical.Length; offset += WorldGenerationContent.MaximumPayloadBytes)
            records.Add(new(WorldSampleCoordinate.FromChunk(Coordinate, 0, 0, 65), 0x53430001, 1,
                (ulong)records.Count, _canonical.AsSpan(offset, Math.Min(WorldGenerationContent.MaximumPayloadBytes, _canonical.Length - offset))));
        return WorldGenerationFingerprint.Decoration(gameplay,
            new() { VegetationProfileId = Profile.Id, PropsProfileId = Profile.Id, RulesProfileId = "scatter.rules.v1" }, records);
    }
}
