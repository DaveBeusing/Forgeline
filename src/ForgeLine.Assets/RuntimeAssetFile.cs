using System.Text;

namespace ForgeLine.Assets;

public static class RuntimeAssetFile
{
    private const uint Magic = 0x53414C46;
    private const int HeaderVersion = 1;

    public static void Write(
        string path,
        RuntimeAssetType type,
        ReadOnlySpan<byte> metadata,
        ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        writer.Write(Magic);
        writer.Write(HeaderVersion);
        writer.Write((int)type);
        writer.Write(metadata.Length);
        writer.Write(payload.Length);
        writer.Write(metadata);
        writer.Write(payload);
    }

    public static RuntimeAssetContent Read(string path, RuntimeAssetType? expectedType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException($"'{path}' is not a ForgeLine runtime asset.");
        }

        var version = reader.ReadInt32();
        if (version != HeaderVersion)
        {
            throw new InvalidDataException($"Runtime asset header version {version} is not supported.");
        }

        var type = (RuntimeAssetType)reader.ReadInt32();
        if (!Enum.IsDefined(type))
        {
            throw new InvalidDataException($"Runtime asset type {(int)type} is invalid.");
        }

        if (expectedType is not null && type != expectedType)
        {
            throw new InvalidDataException($"Runtime asset type {type} does not match expected type {expectedType}.");
        }

        var metadataLength = reader.ReadInt32();
        var payloadLength = reader.ReadInt32();
        if (metadataLength < 0 || payloadLength < 0)
        {
            throw new InvalidDataException("Runtime asset contains invalid section lengths.");
        }

        var remaining = stream.Length - stream.Position;
        var expectedRemaining = (long)metadataLength + payloadLength;
        if (remaining != expectedRemaining)
        {
            throw new InvalidDataException("Runtime asset section lengths do not match the file length.");
        }

        var metadata = reader.ReadBytes(metadataLength);
        var payload = reader.ReadBytes(payloadLength);
        if (metadata.Length != metadataLength || payload.Length != payloadLength)
        {
            throw new EndOfStreamException("Runtime asset ended before all declared data was read.");
        }

        return new RuntimeAssetContent(type, metadata, payload);
    }
}

public sealed record RuntimeAssetContent(
    RuntimeAssetType Type,
    byte[] Metadata,
    byte[] Payload);
