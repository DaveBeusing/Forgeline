using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace ForgeLine.World;

/// <summary>Bounded little-endian primitives. Floating point and culture-dependent text are excluded.</summary>
public sealed class WorldCanonicalWriter
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    private readonly ArrayBufferWriter<byte> _buffer = new();

    internal int RemainingBytes => MaximumBytes - _buffer.WrittenCount;

    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Allocate(4), value);
        _buffer.Advance(4);
    }

    public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Allocate(8), value);
        _buffer.Advance(8);
    }

    public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

    public void WriteIdentifier(string value)
    {
        if (!WorldGenerationRecipe.IsProfileId(value))
            throw new ArgumentException("A canonical lowercase ASCII identifier is required.", nameof(value));
        WriteBytes(Encoding.ASCII.GetBytes(value));
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        // Check both prefix and payload before mutating the writer.
        if (value.Length > MaximumBytes - _buffer.WrittenCount - 4)
            throw new InvalidOperationException("Canonical byte budget exceeded.");
        WriteUInt32((uint)value.Length);
        value.CopyTo(Allocate(value.Length));
        _buffer.Advance(value.Length);
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    private Span<byte> Allocate(int count)
    {
        if (count > MaximumBytes - _buffer.WrittenCount)
            throw new InvalidOperationException("Canonical byte budget exceeded.");
        return _buffer.GetSpan(count)[..count];
    }
}
