using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class TerrainChunkSplatData
{
    public const int LayerCount = 4;
    public const int ControlSamplesPerSide = 33;

    private readonly TerrainMaterialSlot[] _palette;
    private readonly GraphicsTextureMipData[] _mips;

    internal TerrainChunkSplatData(
        IEnumerable<TerrainMaterialSlot> palette,
        IEnumerable<GraphicsTextureMipData> mips)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(mips);

        _palette =
            palette.ToArray();
        _mips =
            mips.ToArray();

        if (_palette.Length != LayerCount)
        {
            throw new ArgumentException(
                $"Terrain chunk palettes require exactly {LayerCount} slots.",
                nameof(palette));
        }

        if (_palette.Distinct().Count() != LayerCount)
        {
            throw new ArgumentException(
                "Terrain chunk palettes must not contain duplicate material slots.",
                nameof(palette));
        }

        if (_mips.Length == 0)
        {
            throw new ArgumentException(
                "Terrain chunk control data requires at least one mip.",
                nameof(mips));
        }
    }

    public IReadOnlyList<TerrainMaterialSlot> Palette =>
        _palette;

    public IReadOnlyList<GraphicsTextureMipData> Mips =>
        _mips;

    public GraphicsTextureData CreateTextureData() =>
        new(
            new GraphicsTextureDescription(
                _mips[0].Width,
                _mips[0].Height,
                GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureColorSpace.Linear,
                _mips.Length),
            _mips);
}

public static class TerrainSplatMapBuilder
{
    public static TerrainChunkSplatData Build(
        TerrainChunk chunk,
        TerrainPresentationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(profile);

        var totals =
            new double[
                TerrainPresentationProfile.MaterialSlotCount];
        AccumulateMaterialTotals(
            chunk,
            profile,
            totals);

        TerrainMaterialSlot[] palette =
            Enumerable
                .Range(
                    0,
                    TerrainPresentationProfile.MaterialSlotCount)
                .OrderByDescending(
                    index =>
                        totals[index])
                .ThenBy(
                    static index =>
                        index)
                .Take(
                    TerrainChunkSplatData.LayerCount)
                .Select(
                    static index =>
                        (TerrainMaterialSlot)index)
                .ToArray();

        byte[] levelZero =
            CreateLevelZero(
                chunk,
                profile,
                palette);
        GraphicsTextureMipData[] mips =
            BuildMipChain(
                TerrainChunkSplatData.ControlSamplesPerSide,
                TerrainChunkSplatData.ControlSamplesPerSide,
                levelZero);

        return new TerrainChunkSplatData(
            palette,
            mips);
    }

    public static void NormalizeControlWeights(
        ReadOnlySpan<float> source,
        Span<byte> destination)
    {
        if (source.Length < TerrainChunkSplatData.LayerCount)
        {
            throw new ArgumentException(
                "Terrain control weights require four source channels.",
                nameof(source));
        }

        if (destination.Length < TerrainChunkSplatData.LayerCount)
        {
            throw new ArgumentException(
                "Terrain control weights require four destination channels.",
                nameof(destination));
        }

        Span<float> normalized =
            stackalloc float[
                TerrainChunkSplatData.LayerCount];
        float total =
            0.0f;

        for (int index = 0;
             index < TerrainChunkSplatData.LayerCount;
             index++)
        {
            float value =
                float.IsFinite(source[index])
                    ? MathF.Max(
                        source[index],
                        0.0f)
                    : 0.0f;
            normalized[index] =
                value;
            total +=
                value;
        }

        if (total <= 1e-6f)
        {
            destination[0] = 255;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 0;
            return;
        }

        Span<int> whole =
            stackalloc int[
                TerrainChunkSplatData.LayerCount];
        Span<float> remainders =
            stackalloc float[
                TerrainChunkSplatData.LayerCount];
        int assigned =
            0;

        for (int index = 0;
             index < TerrainChunkSplatData.LayerCount;
             index++)
        {
            float scaled =
                normalized[index] /
                total *
                255.0f;
            int floor =
                (int)MathF.Floor(
                    scaled);
            whole[index] =
                floor;
            remainders[index] =
                scaled -
                floor;
            assigned +=
                floor;
        }

        int remaining =
            255 -
            assigned;

        while (remaining > 0)
        {
            int selected =
                0;

            for (int index = 1;
                 index < TerrainChunkSplatData.LayerCount;
                 index++)
            {
                if (remainders[index] >
                    remainders[selected])
                {
                    selected =
                        index;
                }
            }

            whole[selected]++;
            remainders[selected] =
                -1.0f;
            remaining--;
        }

        for (int index = 0;
             index < TerrainChunkSplatData.LayerCount;
             index++)
        {
            destination[index] =
                checked(
                    (byte)whole[index]);
        }
    }

    private static void AccumulateMaterialTotals(
        TerrainChunk chunk,
        TerrainPresentationProfile profile,
        Span<double> totals)
    {
        Span<float> weights =
            stackalloc float[
                TerrainPresentationProfile.MaterialSlotCount];

        for (int z = 0;
             z < TerrainChunkSplatData.ControlSamplesPerSide;
             z++)
        {
            float localZ =
                ControlCoordinate(
                    z,
                    chunk.Heightfield.ChunkSizeMeters);

            for (int x = 0;
                 x < TerrainChunkSplatData.ControlSamplesPerSide;
                 x++)
            {
                float localX =
                    ControlCoordinate(
                        x,
                        chunk.Heightfield.ChunkSizeMeters);
                Vector3 position =
                    WorldPosition(
                        chunk,
                        localX,
                        localZ);
                Vector3 normal =
                    chunk.Heightfield.SampleNormal(
                        localX,
                        localZ);

                profile.CalculateMaterialWeights(
                    position,
                    normal,
                    weights);

                for (int slot = 0;
                     slot < totals.Length;
                     slot++)
                {
                    totals[slot] +=
                        weights[slot];
                }
            }
        }
    }

    private static byte[] CreateLevelZero(
        TerrainChunk chunk,
        TerrainPresentationProfile profile,
        TerrainMaterialSlot[] palette)
    {
        int side =
            TerrainChunkSplatData.ControlSamplesPerSide;
        var pixels =
            new byte[
                checked(
                    side *
                    side *
                    TerrainChunkSplatData.LayerCount)];
        Span<float> allWeights =
            stackalloc float[
                TerrainPresentationProfile.MaterialSlotCount];
        Span<float> localWeights =
            stackalloc float[
                TerrainChunkSplatData.LayerCount];

        for (int z = 0;
             z < side;
             z++)
        {
            float localZ =
                ControlCoordinate(
                    z,
                    chunk.Heightfield.ChunkSizeMeters);

            for (int x = 0;
                 x < side;
                 x++)
            {
                float localX =
                    ControlCoordinate(
                        x,
                        chunk.Heightfield.ChunkSizeMeters);
                Vector3 position =
                    WorldPosition(
                        chunk,
                        localX,
                        localZ);
                Vector3 normal =
                    chunk.Heightfield.SampleNormal(
                        localX,
                        localZ);

                profile.CalculateMaterialWeights(
                    position,
                    normal,
                    allWeights);

                for (int layer = 0;
                     layer < TerrainChunkSplatData.LayerCount;
                     layer++)
                {
                    localWeights[layer] =
                        allWeights[
                            (int)palette[layer]];
                }

                int offset =
                    checked(
                        (z *
                         side +
                         x) *
                        TerrainChunkSplatData.LayerCount);
                NormalizeControlWeights(
                    localWeights,
                    pixels.AsSpan(
                        offset,
                        TerrainChunkSplatData.LayerCount));
            }
        }

        return pixels;
    }

    private static GraphicsTextureMipData[] BuildMipChain(
        int width,
        int height,
        byte[] pixels)
    {
        var mips =
            new List<GraphicsTextureMipData>();

        int currentWidth =
            width;
        int currentHeight =
            height;
        byte[] current =
            pixels;
        Span<float> sums =
            stackalloc float[
                TerrainChunkSplatData.LayerCount];

        while (true)
        {
            mips.Add(
                new GraphicsTextureMipData(
                    currentWidth,
                    currentHeight,
                    checked(
                        currentWidth *
                        TerrainChunkSplatData.LayerCount),
                    current));

            if (currentWidth == 1 &&
                currentHeight == 1)
            {
                break;
            }

            int nextWidth =
                Math.Max(
                    1,
                    currentWidth /
                    2);
            int nextHeight =
                Math.Max(
                    1,
                    currentHeight /
                    2);
            var next =
                new byte[
                    checked(
                        nextWidth *
                        nextHeight *
                        TerrainChunkSplatData.LayerCount)];

            for (int y = 0;
                 y < nextHeight;
                 y++)
            {
                int top =
                    y *
                    currentHeight /
                    nextHeight;
                int bottom =
                    Math.Max(
                        top + 1,
                        (y + 1) *
                        currentHeight /
                        nextHeight);

                for (int x = 0;
                     x < nextWidth;
                     x++)
                {
                    int left =
                        x *
                        currentWidth /
                        nextWidth;
                    int right =
                        Math.Max(
                            left + 1,
                            (x + 1) *
                            currentWidth /
                            nextWidth);
                    sums.Clear();

                    for (int sourceY = top;
                         sourceY < bottom;
                         sourceY++)
                    {
                        for (int sourceX = left;
                             sourceX < right;
                             sourceX++)
                        {
                            int sourceOffset =
                                checked(
                                    (sourceY *
                                     currentWidth +
                                     sourceX) *
                                    TerrainChunkSplatData.LayerCount);

                            for (int channel = 0;
                                 channel < TerrainChunkSplatData.LayerCount;
                                 channel++)
                            {
                                sums[channel] +=
                                    current[
                                        sourceOffset +
                                        channel];
                            }
                        }
                    }

                    int destinationOffset =
                        checked(
                            (y *
                             nextWidth +
                             x) *
                            TerrainChunkSplatData.LayerCount);
                    NormalizeControlWeights(
                        sums,
                        next.AsSpan(
                            destinationOffset,
                            TerrainChunkSplatData.LayerCount));
                }
            }

            currentWidth =
                nextWidth;
            currentHeight =
                nextHeight;
            current =
                next;
        }

        return mips.ToArray();
    }

    private static float ControlCoordinate(
        int sample,
        float chunkSizeMeters) =>
        sample *
        chunkSizeMeters /
        (TerrainChunkSplatData.ControlSamplesPerSide - 1);

    private static Vector3 WorldPosition(
        TerrainChunk chunk,
        float localX,
        float localZ) =>
        new(
            chunk.Bounds.Minimum.X +
            localX,
            chunk.Heightfield.SampleHeight(
                localX,
                localZ),
            chunk.Bounds.Minimum.Z +
            localZ);
}
