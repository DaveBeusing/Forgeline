using System.Buffers.Binary;

namespace ForgeLine.Platform;

/// <summary>One immutable, physical-pixel native pointer; never a software overlay.</summary>
public sealed class WindowCursorImage
{
    private readonly byte[] _rgba;
    public int Size { get; }
    public int HotspotX { get; }
    public int HotspotY { get; }
    public WindowCursorImage(int size, int hotspotX, int hotspotY, ReadOnlySpan<byte> rgba)
    {
        if (size is < 24 or > 64 || hotspotX < 0 || hotspotY < 0 || hotspotX >= size || hotspotY >= size || rgba.Length != size * size * 4)
            throw new ArgumentException("Invalid cursor dimensions, hotspot or RGBA payload.");
        Size = size; HotspotX = hotspotX; HotspotY = hotspotY; _rgba = rgba.ToArray();
    }
    internal byte[] Resource()
    {
        int maskStride = (Size + 31) / 32 * 4;
        var bytes = new byte[4 + 40 + _rgba.Length + maskStride * Size];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)HotspotX);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), (ushort)HotspotY);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), Size);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), Size * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), 32);
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
        {
            int source = (y * Size + x) * 4, target = 44 + ((Size - 1 - y) * Size + x) * 4;
            byte alpha = _rgba[source + 3];
            // Native alpha cursors require premultiplied BGRA, unlike straight-alpha atlas pages.
            bytes[target] = (byte)((_rgba[source + 2] * alpha + 127) / 255);
            bytes[target + 1] = (byte)((_rgba[source + 1] * alpha + 127) / 255);
            bytes[target + 2] = (byte)((_rgba[source] * alpha + 127) / 255);
            bytes[target + 3] = alpha;
            if (alpha == 0) bytes[44 + _rgba.Length + (Size - 1 - y) * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
        }
        return bytes;
    }
}
