using System.Numerics;
using ForgeLine.Assets;

namespace ForgeLine.Presentation;

public sealed class TerrainSurfaceTexture
{
    private const int RgbaChannelCount = 4;

    private readonly byte[] _pixels;
    private readonly int _rowPitch;

    private TerrainSurfaceTexture(
        int width,
        int height,
        int rowPitch,
        byte[] pixels)
    {
        Width = width;
        Height = height;
        _rowPitch = rowPitch;
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
        RuntimeTextureData texture =
            RuntimeTextureData.FromPayload(
                payload);

        if (texture.Usage is not
            (RuntimeTextureUsage.Color or
             RuntimeTextureUsage.Emissive))
        {
            throw new InvalidDataException(
                $"Terrain surface texture usage {texture.Usage} is not color content.");
        }

        RuntimeTextureMipLevel mip =
            texture.Mips[0];

        return new TerrainSurfaceTexture(
            mip.Width,
            mip.Height,
            mip.RowPitch,
            mip.Pixels);
    }

    public static TerrainSurfaceTexture FromRgba8(
        int width,
        int height,
        ReadOnlySpan<byte> pixels)
    {
        RuntimeTextureData texture =
            RuntimeTextureData.FromRgba8(
                width,
                height,
                pixels,
                RuntimeTextureColorSpace.Srgb,
                RuntimeTextureUsage.Color);
        RuntimeTextureMipLevel mip =
            texture.Mips[0];

        return new TerrainSurfaceTexture(
            mip.Width,
            mip.Height,
            mip.RowPitch,
            mip.Pixels);
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
                y *
                _rowPitch +
                x *
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
