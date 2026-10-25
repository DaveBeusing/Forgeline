using System.Buffers.Binary;
using System.IO.Compression;

namespace ForgeLine.Assets;

public sealed partial class RuntimeTextureData
{
    // Storage-only wrapper: decoded bytes retain the complete version-3 layout,
    // dimensions, semantic mips and pixels. GPU format/residency never changes.
    private const int LosslessPayloadVersion = 4;
    private const int MaximumLosslessPayloadBytes = 32 * 1024 * 1024;

    private static byte[] CompressPayload(byte[] raw)
    {
        if (raw.Length > MaximumLosslessPayloadBytes) return raw;
        using var encoded = new MemoryStream();
        using (var compressor = new ZLibStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
            compressor.Write(raw);
        if (encoded.Length + sizeof(int) * 3 >= raw.Length) return raw;
        using var result = new MemoryStream();
        using var writer = new BinaryWriter(result);
        writer.Write(LosslessPayloadVersion);
        writer.Write(raw.Length);
        writer.Write(checked((int)encoded.Length));
        encoded.Position = 0;
        encoded.CopyTo(result);
        return result.ToArray();
    }

    private static RuntimeTextureData ReadLosslessPayload(BinaryReader reader, Stream stream)
    {
        if (stream.Length - stream.Position < sizeof(int) * 2)
            throw new InvalidDataException("Lossless texture storage header is truncated.");
        int decodedLength = reader.ReadInt32();
        int encodedLength = reader.ReadInt32();
        if (decodedLength < sizeof(int) * 7 || decodedLength > MaximumLosslessPayloadBytes ||
            encodedLength < 6 || encodedLength > MaximumLosslessPayloadBytes ||
            stream.Length - stream.Position != encodedLength)
            throw new InvalidDataException("Lossless texture storage exceeds its bounds or has inconsistent lengths.");
        using var encoded = new ExactZLibInput(reader.ReadBytes(encodedLength));
        using var inflater = new ZLibStream(encoded, CompressionMode.Decompress);
        byte[] raw = GC.AllocateUninitializedArray<byte>(decodedLength);
        try
        {
            inflater.ReadExactly(raw);
            if (inflater.ReadByte() != -1)
                throw new InvalidDataException("Lossless texture expands past its declared length.");
        }
        catch (EndOfStreamException error)
        {
            throw new InvalidDataException("Lossless texture storage ended before its declared decoded length.", error);
        }
        if (encoded.Position != encoded.Length)
            throw new InvalidDataException("Lossless texture storage has trailing compressed-section data.");
        // No nested wrappers, recursive inflation or new semantic representation.
        if (BinaryPrimitives.ReadInt32LittleEndian(raw) != CurrentPayloadVersion)
            throw new InvalidDataException("Lossless texture must contain a version-3 semantic payload.");
        return FromPayload(raw);
    }
    // Keep a small tail out of the inflater's read-ahead buffer, then feed it
    // bytewise. A completed ZLib stream must consume the whole declared section;
    // otherwise a valid stream followed by arbitrary padding could be accepted.
    private sealed class ExactZLibInput(byte[] bytes) : Stream
    {
        private readonly MemoryStream _input = new(bytes, writable: false);
        public override bool CanRead => _input.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _input.Length;
        public override long Position { get => _input.Position; set => throw new NotSupportedException(); }
        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, Math.Max(1, Length - Position - 8));
            return _input.Read(buffer[..count]);
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int ReadByte() => _input.ReadByte();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _input.Dispose();
            base.Dispose(disposing);
        }
    }
}
