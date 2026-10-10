using System.Buffers.Binary;
using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class TexturePipelineTests
{
    [Theory]
    [InlineData("balanced")]
    [InlineData("best")]
    public void BandedCompressionMatchesSerialBytesAcrossMipsAndPartialBands(string quality)
    {
        using var workspace = new TextureWorkspace();
        const string id = "texture.test.band_parity";
        const int width = 32;
        const int height = 260;
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index++)
            pixels[index] = (byte)((index * 37 + index / 19) % 256);
        workspace.WriteTexture(id, RuntimeTextureUsage.BaseColor, RuntimeTextureColorSpace.Srgb,
            width, height, pixels);
        Assert.True(workspace.Compile().Success);
        RuntimeTextureData raw = workspace.ReadTexture(id);
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.BaseColor, RuntimeTextureColorSpace.Srgb,
            true, null, format: "bc7Unorm", compressionQuality: quality);
        var progress = new List<AssetCompilationProgress>();
        Assert.True(AssetPipelineCompiler.Compile(workspace.SourceRoot, workspace.RuntimeRoot,
            progress: progress.Add).Success);
        RuntimeTextureData compressed = workspace.ReadTexture(id);
        var serial = new BCnEncoder.Encoder.BcEncoder(BCnEncoder.Shared.CompressionFormat.Bc7);
        serial.Options.IsParallel = false;
        serial.OutputOptions.GenerateMipMaps = false;
        serial.OutputOptions.Quality = quality == "balanced"
            ? BCnEncoder.Encoder.CompressionQuality.Balanced
            : BCnEncoder.Encoder.CompressionQuality.BestQuality;
        Assert.Equal(raw.Mips.Count, compressed.Mips.Count);
        for (int level = 0; level < raw.Mips.Count; level++)
        {
            RuntimeTextureMipLevel mip = raw.Mips[level];
            Assert.Equal(serial.EncodeToRawBytes(mip.Pixels, mip.Width, mip.Height,
                BCnEncoder.Encoder.PixelFormat.Rgba32)[0], compressed.Mips[level].Pixels);
        }
        Assert.Contains(progress, p => p.Stage == "texture-bc7-band" && p.Detail!.Contains("row=256 rows=4", StringComparison.Ordinal));
        Assert.Contains(progress, p => p.Stage == "runtime-write" && p.State == "completed");
        Assert.All(progress, p => Assert.True(p.ElapsedMilliseconds >= 0 && p.ManagedBytes >= 0));
        byte[] before = compressed.ToPayload();
        progress.Clear();
        Assert.Equal(1, AssetPipelineCompiler.Compile(workspace.SourceRoot, workspace.RuntimeRoot,
            progress: progress.Add).SkippedCount);
        Assert.Contains(progress, p => p.State == "unchanged" && p.AssetId == id);
        Assert.DoesNotContain(progress, p => p.Stage == "texture-bc7");
        Assert.Equal(before, workspace.ReadTexture(id).ToPayload());
    }

    [Fact]
    public void BalancedCompressionPreservesNormalDirectionAndQualityInvalidatesCache()
    {
        using var workspace = new TextureWorkspace();
        const string id = "texture.test.production_normal";
        const int size = 32;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var direction = System.Numerics.Vector3.Normalize(new(
                MathF.Sin(x * MathF.PI / 8) * 0.35f,
                MathF.Cos(y * MathF.PI / 8) * 0.35f, 1));
            int offset = (y * size + x) * 4;
            pixels[offset] = (byte)MathF.Round((direction.X * 0.5f + 0.5f) * 255);
            pixels[offset + 1] = (byte)MathF.Round((direction.Y * 0.5f + 0.5f) * 255);
            pixels[offset + 2] = (byte)MathF.Round((direction.Z * 0.5f + 0.5f) * 255);
            pixels[offset + 3] = 255;
        }
        workspace.WriteTexture(id, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear, size, size, pixels);
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear,
            true, null, format: "bc7Unorm", compressionQuality: "balanced");
        Assert.True(workspace.Compile().Success);
        var texture = workspace.ReadTexture(id);
        var decoded = new BCnEncoder.Decoder.BcDecoder().DecodeRaw(texture.Mips[0].Pixels, size, size, BCnEncoder.Shared.CompressionFormat.Bc7);
        for (int index = 0; index < decoded.Length; index++)
        {
            var expected = System.Numerics.Vector3.Normalize(new(pixels[index * 4] / 127.5f - 1,
                pixels[index * 4 + 1] / 127.5f - 1, pixels[index * 4 + 2] / 127.5f - 1));
            var actual = System.Numerics.Vector3.Normalize(new(decoded[index].r / 127.5f - 1,
                decoded[index].g / 127.5f - 1, decoded[index].b / 127.5f - 1));
            Assert.True(System.Numerics.Vector3.Dot(expected, actual) >= MathF.Cos(6 * MathF.PI / 180),
                "Compressed tangent normal deviates by more than six degrees.");
        }
        byte[] before = texture.ToPayload();
        Assert.True(workspace.Compile(clean: true).Success);
        Assert.Equal(before, workspace.ReadTexture(id).ToPayload());
        Assert.Equal(1, workspace.Compile().SkippedCount);
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear,
            true, null, format: "bc7Unorm", compressionQuality: "best");
        Assert.Equal(1, workspace.Compile().CompiledCount);
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear,
            true, null, format: "bc7Unorm", compressionQuality: "999");
        Assert.False(workspace.Compile().Success);
    }
    [Theory]
    [InlineData(RuntimeTextureUsage.TerrainControl, 8, 8)]
    [InlineData(RuntimeTextureUsage.GenericData, 8, 8)]
    [InlineData(RuntimeTextureUsage.Normal, 7, 8)]
    public void RejectsBc7ForLosslessDataOrUnalignedTopLevel(RuntimeTextureUsage usage, int width, int height)
    {
        using var workspace = new TextureWorkspace();
        const string id = "texture.test.invalid_compression";
        workspace.WriteTexture(id, usage, RuntimeTextureColorSpace.Linear,
            width, height, SolidPixels(width, height, 128, 128, 255, 255));
        workspace.WriteTextureDefinition(id, usage, RuntimeTextureColorSpace.Linear,
            true, null, format: "bc7Unorm");
        Assert.False(workspace.Compile().Success);
    }
    [Fact]
    public void Bc7CompilationRetainsSemanticMipsAndIsDeterministic()
    {
        using var workspace = new TextureWorkspace();
        const string id = "texture.test.compressed";
        workspace.WriteTexture(id, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear,
            8, 8, SolidPixels(8, 8, 128, 128, 255, 255));
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear,
            true, null, format: "bc7Unorm");
        AssetCompilationResult result = workspace.Compile();
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        RuntimeTextureData texture = workspace.ReadTexture(id);
        Assert.Equal(RuntimeTextureFormat.Bc7Unorm, texture.Format);
        Assert.Equal(4, texture.Mips.Count);
        Assert.Equal(112, texture.ResidentByteCount);
        var decoder = new BCnEncoder.Decoder.BcDecoder();
        var decoded = decoder.DecodeRaw(texture.Mips[0].Pixels, 8, 8, BCnEncoder.Shared.CompressionFormat.Bc7);
        Assert.All(decoded, pixel =>
        {
            Assert.InRange((int)pixel.r, 126, 130);
            Assert.InRange((int)pixel.g, 126, 130);
            Assert.InRange((int)pixel.b, 253, 255);
        });
        byte[] before = texture.ToPayload();
        Assert.True(workspace.Compile(clean: true).Success);
        Assert.Equal(before, workspace.ReadTexture(id).ToPayload());
        Assert.Equal(1, workspace.Compile().SkippedCount);
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.Normal, RuntimeTextureColorSpace.Linear, true, null);
        Assert.Equal(1, workspace.Compile().CompiledCount);
        Assert.Equal(RuntimeTextureFormat.Rgba8Unorm, workspace.ReadTexture(id).Format);
    }

    [Fact]
    public void RuntimeResolutionCapFiltersSourceAndInvalidatesCachedOutput()
    {
        using var workspace = new TextureWorkspace();
        const string id = "texture.test.resolution_cap";
        workspace.WriteTexture(id, RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb, 8, 4, SolidPixels(8, 4, 128, 64, 32, 255));
        Assert.True(workspace.Compile().Success);
        Assert.Equal(8, workspace.ReadTexture(id).Width);
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb, true, null, maxDimension: 2);
        AssetCompilationResult changed = workspace.Compile();
        Assert.True(changed.Success);
        Assert.Equal(1, changed.CompiledCount);
        RuntimeTextureData capped = workspace.ReadTexture(id);
        Assert.Equal(2, capped.Width);
        Assert.Equal(1, capped.Height);
        Assert.Equal(2, capped.Mips.Count);
        Assert.Equal(128, capped.Mips[0].Pixels[0]);
        Assert.Equal(1, workspace.Compile().SkippedCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(16385)]
    public void InvalidRuntimeResolutionCapIsRejected(int limit)
    {
        using var workspace = new TextureWorkspace();
        const string id = "texture.test.invalid_cap";
        workspace.WriteTexture(id, RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb, 2, 2, SolidPixels(2, 2, 128, 64, 32, 255));
        workspace.WriteTextureDefinition(id, RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb, true, null, maxDimension: limit);
        AssetCompilationResult result = workspace.Compile();
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ASSET034");
    }

    private static readonly JsonSerializerOptions FixtureJsonOptions =
        new()
        {
            WriteIndented =
                true
        };

    [Fact]
    public void GeneratesExpectedFullMipDimensionSequence()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.dimensions",
            RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb,
            4,
            2,
            SolidPixels(
                4,
                2,
                32,
                64,
                96,
                255));

        AssetCompilationResult result =
            workspace.Compile();
        RuntimeTextureData texture =
            workspace.ReadTexture(
                "texture.test.dimensions");

        Assert.True(
            result.Success);
        Assert.Collection(
            texture.Mips,
            mip =>
            {
                Assert.Equal(4, mip.Width);
                Assert.Equal(2, mip.Height);
            },
            mip =>
            {
                Assert.Equal(2, mip.Width);
                Assert.Equal(1, mip.Height);
            },
            mip =>
            {
                Assert.Equal(1, mip.Width);
                Assert.Equal(1, mip.Height);
            });
    }

    [Fact]
    public void BaseColorMipsFilterInLinearLight()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.srgb",
            RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb,
            2,
            1,
            [
                0, 0, 0, 255,
                255, 255, 255, 255
            ]);

        Assert.True(
            workspace.Compile().Success);

        RuntimeTextureMipLevel mip =
            workspace.ReadTexture(
                "texture.test.srgb")
            .Mips[1];

        Assert.Equal(
            188,
            mip.Pixels[0]);
        Assert.Equal(
            188,
            mip.Pixels[1]);
        Assert.Equal(
            188,
            mip.Pixels[2]);
        Assert.Equal(
            255,
            mip.Pixels[3]);
    }

    [Fact]
    public void NormalMapMipsAreRenormalized()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.normal",
            RuntimeTextureUsage.Normal,
            RuntimeTextureColorSpace.Linear,
            2,
            1,
            [
                255, 128, 128, 255,
                128, 255, 128, 255
            ]);

        Assert.True(
            workspace.Compile().Success);

        RuntimeTextureMipLevel mip =
            workspace.ReadTexture(
                "texture.test.normal")
            .Mips[1];
        double x =
            DecodeNormal(
                mip.Pixels[0]);
        double y =
            DecodeNormal(
                mip.Pixels[1]);
        double z =
            DecodeNormal(
                mip.Pixels[2]);
        double length =
            Math.Sqrt(
                x * x +
                y * y +
                z * z);

        Assert.InRange(
            length,
            0.99,
            1.01);
        Assert.True(
            mip.Pixels[0] >
            200);
        Assert.True(
            mip.Pixels[1] >
            200);
    }

    [Fact]
    public void OrmMipsPreservePackedChannelSemantics()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.orm",
            RuntimeTextureUsage.Orm,
            RuntimeTextureColorSpace.Linear,
            2,
            1,
            [
                0, 64, 128, 255,
                255, 192, 64, 255
            ]);

        Assert.True(
            workspace.Compile().Success);

        byte[] pixel =
            workspace.ReadTexture(
                "texture.test.orm")
            .Mips[1]
            .Pixels;

        Assert.Equal(
            128,
            pixel[0]);
        Assert.Equal(
            128,
            pixel[1]);
        Assert.Equal(
            96,
            pixel[2]);
        Assert.Equal(
            255,
            pixel[3]);
    }

    [Fact]
    public void TerrainControlMipsRemainNormalized()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.control",
            RuntimeTextureUsage.TerrainControl,
            RuntimeTextureColorSpace.Linear,
            2,
            1,
            [
                255, 0, 0, 0,
                0, 255, 0, 0
            ]);

        Assert.True(
            workspace.Compile().Success);

        byte[] pixel =
            workspace.ReadTexture(
                "texture.test.control")
            .Mips[1]
            .Pixels;

        Assert.Equal(
            255,
            pixel.Sum(
                static value =>
                    (int)value));
        Assert.Equal(
            128,
            pixel[0]);
        Assert.Equal(
            127,
            pixel[1]);
    }

    [Fact]
    public void RepeatedCleanCompilationProducesIdenticalRuntimeFile()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.deterministic",
            RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb,
            4,
            4,
            SolidPixels(
                4,
                4,
                70,
                90,
                110,
                255));

        Assert.True(
            workspace.Compile(
                clean: true)
            .Success);
        byte[] first =
            workspace.ReadRuntimeFile(
                "texture.test.deterministic");

        Assert.True(
            workspace.Compile(
                clean: true)
            .Success);
        byte[] second =
            workspace.ReadRuntimeFile(
                "texture.test.deterministic");

        Assert.Equal(
            first,
            second);
    }

    [Fact]
    public void MipSettingChangeInvalidatesIncrementalCache()
    {
        using var workspace =
            new TextureWorkspace();
        const string Id =
            "texture.test.cache";

        workspace.WriteTexture(
            Id,
            RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb,
            4,
            4,
            SolidPixels(
                4,
                4,
                20,
                40,
                60,
                255),
            maxMipLevels: 1);

        AssetCompilationResult first =
            workspace.Compile();
        AssetCompilationResult unchanged =
            workspace.Compile();

        workspace.WriteTextureDefinition(
            Id,
            RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb,
            generateMipmaps: true,
            maxMipLevels: 2);
        AssetCompilationResult changed =
            workspace.Compile();

        Assert.True(first.Success);
        Assert.Equal(1, first.CompiledCount);
        Assert.True(unchanged.Success);
        Assert.Equal(1, unchanged.SkippedCount);
        Assert.True(changed.Success);
        Assert.Equal(1, changed.CompiledCount);
        Assert.Equal(
            2,
            workspace.ReadTexture(
                Id)
            .Mips.Count);
    }

    [Fact]
    public void MaterialTextureUsageMismatchFailsDuringCompilation()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.normal_only",
            RuntimeTextureUsage.Normal,
            RuntimeTextureColorSpace.Linear,
            1,
            1,
            [128, 128, 255, 255]);
        workspace.WriteMaterial(
            "material.test.invalid",
            """
            {
              "baseColorTexture": "texture.test.normal_only"
            }
            """);

        AssetCompilationResult result =
            workspace.Compile();

        Assert.False(
            result.Success);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                "ASSET032" &&
                diagnostic.Message.Contains(
                    "baseColorTexture",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void CorruptTgaFailsWithActionableDiagnostic()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteCorruptTexture(
            "texture.test.corrupt");

        AssetCompilationResult result =
            workspace.Compile();

        Assert.False(
            result.Success);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                "ASSET100" &&
                diagnostic.Message.Contains(
                    "TGA header",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void CompilerReportsTextureRuntimeMetadata()
    {
        using var workspace =
            new TextureWorkspace();
        workspace.WriteTexture(
            "texture.test.report",
            RuntimeTextureUsage.BaseColor,
            RuntimeTextureColorSpace.Srgb,
            4,
            2,
            SolidPixels(
                4,
                2,
                10,
                20,
                30,
                255));

        AssetCompilationResult result =
            workspace.Compile();

        Assert.True(
            result.Success);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                "ASSETI001" &&
                diagnostic.Message.Contains(
                    "dimensions=4x2",
                    StringComparison.Ordinal) &&
                diagnostic.Message.Contains(
                    "mips=3",
                    StringComparison.Ordinal) &&
                diagnostic.Message.Contains(
                    "usage=BaseColor",
                    StringComparison.Ordinal));
    }

    private static byte[] SolidPixels(
        int width,
        int height,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        var pixels =
            new byte[
                checked(
                    width *
                    height *
                    4)];

        for (int offset = 0;
             offset < pixels.Length;
             offset += 4)
        {
            pixels[offset] =
                red;
            pixels[offset + 1] =
                green;
            pixels[offset + 2] =
                blue;
            pixels[offset + 3] =
                alpha;
        }

        return pixels;
    }

    private static double DecodeNormal(
        byte value) =>
        value /
        127.5 -
        1.0;

    private sealed class TextureWorkspace : IDisposable
    {
        private readonly string _root =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-texture-pipeline-" +
                Guid.NewGuid().ToString(
                    "N"));

        public TextureWorkspace()
        {
            SourceRoot =
                Path.Combine(
                    _root,
                    "source");
            RuntimeRoot =
                Path.Combine(
                    _root,
                    "runtime");
            Directory.CreateDirectory(
                SourceRoot);
        }

        public string SourceRoot { get; }

        public string RuntimeRoot { get; }

        public AssetCompilationResult Compile(
            bool clean = false) =>
            AssetPipelineCompiler.Compile(
                SourceRoot,
                RuntimeRoot,
                clean);

        public void WriteTexture(
            string id,
            RuntimeTextureUsage usage,
            RuntimeTextureColorSpace colorSpace,
            int width,
            int height,
            byte[] rgba,
            bool generateMipmaps = true,
            int? maxMipLevels = null)
        {
            string name =
                FileName(
                    id);
            WriteTga(
                $"textures/{name}.tga",
                width,
                height,
                rgba);
            WriteTextureDefinition(
                id,
                usage,
                colorSpace,
                generateMipmaps,
                maxMipLevels);
        }

        public void WriteTextureDefinition(
            string id,
            RuntimeTextureUsage usage,
            RuntimeTextureColorSpace colorSpace,
            bool generateMipmaps,
            int? maxMipLevels,
            int? maxDimension = null,
            string? format = null,
            string? compressionQuality = null)
        {
            string name =
                FileName(
                    id);
            var definition =
                new Dictionary<string, object?>
                {
                    ["id"] =
                        id,
                    ["type"] =
                        "texture",
                    ["source"] =
                        $"{name}.tga",
                    ["textureUsage"] =
                        TextureUsageName(
                            usage),
                    ["textureColorSpace"] =
                        colorSpace ==
                        RuntimeTextureColorSpace.Srgb
                            ? "srgb"
                            : "linear",
                    ["textureGenerateMipmaps"] =
                        generateMipmaps
                };

            if (maxMipLevels.HasValue)
            {
                definition["textureMaxMipLevels"] =
                    maxMipLevels.Value;
            }

            if (maxDimension.HasValue)
                definition["textureMaxDimension"] = maxDimension.Value;
            if (format is not null) definition["textureFormat"] = format;
            if (compressionQuality is not null) definition["textureCompressionQuality"] = compressionQuality;

            WriteText(
                $"textures/{name}.asset.json",
                JsonSerializer.Serialize(
                    definition,
                    FixtureJsonOptions));
        }

        public void WriteMaterial(
            string id,
            string json)
        {
            string name =
                FileName(
                    id);
            WriteText(
                $"materials/{name}.material.json",
                json);
            WriteText(
                $"materials/{name}.asset.json",
                JsonSerializer.Serialize(
                    new Dictionary<string, object>
                    {
                        ["id"] =
                            id,
                        ["type"] =
                            "material",
                        ["source"] =
                            $"{name}.material.json"
                    }));
        }

        public void WriteCorruptTexture(
            string id)
        {
            string name =
                FileName(
                    id);
            WriteBytes(
                $"textures/{name}.tga",
                [1, 2, 3, 4]);
            WriteTextureDefinition(
                id,
                RuntimeTextureUsage.BaseColor,
                RuntimeTextureColorSpace.Srgb,
                generateMipmaps: true,
                maxMipLevels: null);
        }

        public RuntimeTextureData ReadTexture(
            string id)
        {
            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    RuntimeRoot);
            RuntimeAssetContent content =
                catalog.Read(
                    AssetId.Parse(
                        id));

            return RuntimeTextureData.FromPayload(
                content.Payload);
        }

        public byte[] ReadRuntimeFile(
            string id)
        {
            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    RuntimeRoot);
            RuntimeAssetRecord record =
                catalog.Get(
                    AssetId.Parse(
                        id));
            string path =
                Path.Combine(
                    RuntimeRoot,
                    record.RuntimePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));

            return File.ReadAllBytes(
                path);
        }

        public void Dispose()
        {
            if (Directory.Exists(
                    _root))
            {
                Directory.Delete(
                    _root,
                    recursive: true);
            }
        }

        private void WriteTga(
            string relativePath,
            int width,
            int height,
            byte[] rgba)
        {
            Assert.Equal(
                checked(
                    width *
                    height *
                    4),
                rgba.Length);

            var bytes =
                new byte[
                    checked(
                        18 +
                        rgba.Length)];
            bytes[2] =
                2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(
                    12,
                    2),
                checked(
                    (ushort)width));
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(
                    14,
                    2),
                checked(
                    (ushort)height));
            bytes[16] =
                32;
            bytes[17] =
                0x20;

            for (int source = 0,
                     target = 18;
                 source < rgba.Length;
                 source += 4,
                     target += 4)
            {
                bytes[target] =
                    rgba[source + 2];
                bytes[target + 1] =
                    rgba[source + 1];
                bytes[target + 2] =
                    rgba[source];
                bytes[target + 3] =
                    rgba[source + 3];
            }

            WriteBytes(
                relativePath,
                bytes);
        }

        private void WriteText(
            string relativePath,
            string content)
        {
            string path =
                ResolveSource(
                    relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    path)!);
            File.WriteAllText(
                path,
                content);
        }

        private void WriteBytes(
            string relativePath,
            byte[] bytes)
        {
            string path =
                ResolveSource(
                    relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    path)!);
            File.WriteAllBytes(
                path,
                bytes);
        }

        private string ResolveSource(
            string relativePath) =>
            Path.Combine(
                SourceRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        private static string FileName(
            string id) =>
            id.Replace(
                '.',
                '_');

        private static string TextureUsageName(
            RuntimeTextureUsage usage) =>
            usage switch
            {
                RuntimeTextureUsage.Normal =>
                    "normal",
                RuntimeTextureUsage.Orm =>
                    "orm",
                RuntimeTextureUsage.Emissive =>
                    "emissive",
                RuntimeTextureUsage.GenericData =>
                    "genericData",
                RuntimeTextureUsage.TerrainControl =>
                    "terrainControl",
                _ =>
                    "baseColor"
            };
    }
}
