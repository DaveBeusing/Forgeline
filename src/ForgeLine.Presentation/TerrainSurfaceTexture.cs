using System.Numerics;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

public sealed class TerrainSurfaceTexture
{
    private const int RuntimePayloadVersion = 1;
    private const int RgbaChannelCount = 4;

    private readonly byte[] _pixels;

    private TerrainSurfaceTexture(
        int width,
        int height,
        byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public static TerrainSurfaceTexture FromRuntimeAsset(
        RuntimeAssetContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Type != RuntimeAssetType.Texture)
        {
            throw new InvalidDataException(
                $"Runtime asset type {content.Type} is not a terrain texture.");
        }

        return FromRuntimePayload(
            content.Payload);
    }

    public static TerrainSurfaceTexture FromRuntimePayload(
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(int) * 4)
        {
            throw new InvalidDataException(
                "Runtime texture payload is incomplete.");
        }

        using var stream =
            new MemoryStream(
                payload.ToArray(),
                writable: false);
        using var reader =
            new BinaryReader(stream);

        int version =
            reader.ReadInt32();
        int width =
            reader.ReadInt32();
        int height =
            reader.ReadInt32();
        int channels =
            reader.ReadInt32();

        if (version != RuntimePayloadVersion)
        {
            throw new InvalidDataException(
                $"Runtime texture payload version {version} is not supported.");
        }

        if (width <= 0 ||
            height <= 0 ||
            channels != RgbaChannelCount)
        {
            throw new InvalidDataException(
                $"Runtime texture payload dimensions/channels are invalid: {width}x{height}x{channels}.");
        }

        int expectedPixelBytes =
            checked(
                width *
                height *
                RgbaChannelCount);
        long remaining =
            stream.Length -
            stream.Position;

        if (remaining != expectedPixelBytes)
        {
            throw new InvalidDataException(
                $"Runtime texture payload contains {remaining} pixel bytes; expected {expectedPixelBytes}.");
        }

        byte[] pixels =
            reader.ReadBytes(
                expectedPixelBytes);

        if (pixels.Length != expectedPixelBytes)
        {
            throw new EndOfStreamException(
                "Runtime texture payload ended before all RGBA8 pixels were read.");
        }

        return new TerrainSurfaceTexture(
            width,
            height,
            pixels);
    }

    public static TerrainSurfaceTexture FromRgba8(
        int width,
        int height,
        ReadOnlySpan<byte> pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            height);

        int expectedLength =
            checked(
                width *
                height *
                RgbaChannelCount);

        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"RGBA8 data contains {pixels.Length} bytes; expected {expectedLength}.",
                nameof(pixels));
        }

        return new TerrainSurfaceTexture(
            width,
            height,
            pixels.ToArray());
    }

    public Vector3 SampleWorld(
        Vector2 worldPosition,
        float tileMeters)
    {
        if (!float.IsFinite(worldPosition.X) ||
            !float.IsFinite(worldPosition.Y))
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldPosition));
        }

        if (!float.IsFinite(tileMeters) ||
            tileMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileMeters));
        }

        return SampleRepeat(
            worldPosition.X / tileMeters,
            worldPosition.Y / tileMeters);
    }

    public Vector3 SampleRepeat(
        float u,
        float v)
    {
        if (!float.IsFinite(u) ||
            !float.IsFinite(v))
        {
            throw new ArgumentOutOfRangeException(
                nameof(u));
        }

        float wrappedU =
            u -
            MathF.Floor(u);
        float wrappedV =
            v -
            MathF.Floor(v);
        float texelX =
            wrappedU *
            Width;
        float texelY =
            wrappedV *
            Height;

        int x0 =
            (int)MathF.Floor(
                texelX) %
            Width;
        int y0 =
            (int)MathF.Floor(
                texelY) %
            Height;
        int x1 =
            (x0 + 1) %
            Width;
        int y1 =
            (y0 + 1) %
            Height;
        float tx =
            texelX -
            MathF.Floor(
                texelX);
        float ty =
            texelY -
            MathF.Floor(
                texelY);

        Vector3 top =
            Vector3.Lerp(
                ReadRgb(
                    x0,
                    y0),
                ReadRgb(
                    x1,
                    y0),
                tx);
        Vector3 bottom =
            Vector3.Lerp(
                ReadRgb(
                    x0,
                    y1),
                ReadRgb(
                    x1,
                    y1),
                tx);

        return Vector3.Lerp(
            top,
            bottom,
            ty);
    }

    private Vector3 ReadRgb(
        int x,
        int y)
    {
        int index =
            checked(
                (y * Width + x) *
                RgbaChannelCount);

        const float reciprocalByte =
            1.0f /
            byte.MaxValue;

        return new Vector3(
            _pixels[index] *
            reciprocalByte,
            _pixels[index + 1] *
            reciprocalByte,
            _pixels[index + 2] *
            reciprocalByte);
    }
}
