using System.Security.Cryptography;

namespace ForgeLine.World;

/// <summary>One immutable, schema-labelled canonical content record.</summary>
public sealed class WorldGenerationContent
{
    public const int MaximumPayloadBytes = 65_536;
    private readonly byte[] _payload;

    public WorldGenerationContent(WorldSampleCoordinate coordinate, uint typeId, uint schemaVersion,
        ulong stableId, ReadOnlySpan<byte> canonicalPayload)
    {
        ArgumentOutOfRangeException.ThrowIfZero(typeId);
        ArgumentOutOfRangeException.ThrowIfZero(schemaVersion);
        if (canonicalPayload.Length > MaximumPayloadBytes)
            throw new ArgumentException("Content payload budget exceeded.", nameof(canonicalPayload));
        Coordinate = coordinate;
        TypeId = typeId;
        SchemaVersion = schemaVersion;
        StableId = stableId;
        _payload = canonicalPayload.ToArray();
    }

    public WorldSampleCoordinate Coordinate { get; }
    public uint TypeId { get; }
    public uint SchemaVersion { get; }
    public ulong StableId { get; }

    internal int CanonicalSize => 36 + _payload.Length;

    internal void WriteTo(WorldCanonicalWriter writer)
    {
        writer.WriteInt64(Coordinate.X);
        writer.WriteInt64(Coordinate.Z);
        writer.WriteUInt32(TypeId);
        writer.WriteUInt32(SchemaVersion);
        writer.WriteUInt64(StableId);
        writer.WriteBytes(_payload);
    }
}

public enum WorldFingerprintKind : uint
{
    Gameplay = 1,
    Decoration = 2
}

/// <summary>SHA-256 identifies canonical output, never a seed or an authored map artifact.</summary>
public sealed class WorldGenerationFingerprint
{
    public const int MaximumRecords = 262_144;
    private readonly byte[] _canonicalBytes;

    private WorldGenerationFingerprint(WorldFingerprintKind kind, byte[] canonicalBytes)
    {
        Kind = kind;
        _canonicalBytes = canonicalBytes;
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(canonicalBytes));
    }

    public WorldFingerprintKind Kind { get; }
    public string Sha256 { get; }
    public byte[] GetCanonicalBytes() => _canonicalBytes.ToArray();

    public static WorldGenerationFingerprint Gameplay(WorldGenerationRecipe recipe,
        IEnumerable<WorldGenerationContent> content)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        recipe.Validate();
        var writer = Header(WorldFingerprintKind.Gameplay);
        WriteRecipe(writer, recipe);
        WriteContent(writer, content);
        return new(WorldFingerprintKind.Gameplay, writer.ToArray());
    }

    public static WorldGenerationFingerprint Decoration(WorldGenerationFingerprint gameplay,
        WorldDecorationRecipe recipe, IEnumerable<WorldGenerationContent> content)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        ArgumentNullException.ThrowIfNull(recipe);
        if (gameplay.Kind != WorldFingerprintKind.Gameplay)
            throw new ArgumentException("Decoration must refer to a gameplay fingerprint.", nameof(gameplay));
        recipe.Validate();
        var writer = Header(WorldFingerprintKind.Decoration);
        writer.WriteBytes(Convert.FromHexString(gameplay.Sha256));
        writer.WriteUInt32(recipe.Version);
        writer.WriteIdentifier(recipe.VegetationProfileId);
        writer.WriteIdentifier(recipe.PropsProfileId);
        writer.WriteIdentifier(recipe.RulesProfileId);
        WriteContent(writer, content);
        return new(WorldFingerprintKind.Decoration, writer.ToArray());
    }

    private static WorldCanonicalWriter Header(WorldFingerprintKind kind)
    {
        var writer = new WorldCanonicalWriter();
        writer.WriteUInt32(0x31475746); // ASCII FWG1, canonical format version 1.
        writer.WriteUInt32((uint)kind);
        return writer;
    }

    private static void WriteRecipe(WorldCanonicalWriter writer, WorldGenerationRecipe recipe)
    {
        writer.WriteUInt32(recipe.GeneratorVersion);
        writer.WriteUInt64(recipe.Seed);
        writer.WriteInt32(recipe.Origin.X);
        writer.WriteInt32(recipe.Origin.Z);
        writer.WriteInt32(recipe.WidthInChunks);
        writer.WriteInt32(recipe.HeightInChunks);
        writer.WriteInt32(recipe.ChunkSizeMillimeters);
        writer.WriteInt32(recipe.SamplesPerSide);
        writer.WriteInt32(recipe.RegionSizeInChunks);
        writer.WriteIdentifier(recipe.TerrainProfileId);
        writer.WriteIdentifier(recipe.ClimateProfileId);
        writer.WriteIdentifier(recipe.GeographyProfileId);
        writer.WriteIdentifier(recipe.RulesProfileId);
        writer.WriteUInt32(recipe.Options);
    }

    private static void WriteContent(WorldCanonicalWriter writer, IEnumerable<WorldGenerationContent> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var collected = new List<WorldGenerationContent>();
        int remainingBytes = writer.RemainingBytes - 4;
        foreach (var record in content)
        {
            if (record is null || collected.Count == MaximumRecords)
                throw new ArgumentException("Invalid content or record budget exceeded.", nameof(content));
            if (record.CanonicalSize > remainingBytes)
                throw new InvalidOperationException("Canonical byte budget exceeded.");
            remainingBytes -= record.CanonicalSize;
            collected.Add(record);
        }
        var records = collected.ToArray();
        Array.Sort(records, Compare);
        writer.WriteUInt32((uint)records.Length);
        for (int index = 0; index < records.Length; index++)
        {
            if (index > 0 && Compare(records[index - 1], records[index]) == 0)
                throw new ArgumentException("Duplicate canonical content key.", nameof(content));
            records[index].WriteTo(writer);
        }
    }

    private static int Compare(WorldGenerationContent left, WorldGenerationContent right)
    {
        int result = left.Coordinate.Z.CompareTo(right.Coordinate.Z);
        if (result == 0) result = left.Coordinate.X.CompareTo(right.Coordinate.X);
        if (result == 0) result = left.TypeId.CompareTo(right.TypeId);
        if (result == 0) result = left.StableId.CompareTo(right.StableId);
        return result;
    }
}
