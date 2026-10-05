using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class AssetPipelineTests
{
    [Fact]
    public void CompilesTextureAndMaterialAndLoadsRuntimeAssets()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteTga("textures/albedo.tga", 255, 64, 32, 255);
        workspace.WriteText(
            "materials/armor.material.json",
            """
            {
              "baseColorTexture": "texture.directorate.armor_albedo",
              "metallicFactor": 0.7,
              "roughnessFactor": 0.4
            }
            """);
        workspace.WriteAsset(
            "textures/albedo.asset.json",
            """
            {
              "id": "texture.directorate.armor_albedo",
              "type": "texture",
              "source": "albedo.tga"
            }
            """);
        workspace.WriteAsset(
            "materials/armor.asset.json",
            """
            {
              "id": "material.directorate.armor",
              "type": "material",
              "source": "armor.material.json"
            }
            """);

        var result = workspace.Compile();

        Assert.True(result.Success);
        Assert.Equal(2, result.CompiledCount);
        Assert.Equal(0, result.SkippedCount);

        var catalog = RuntimeAssetCatalog.Load(workspace.RuntimeRoot);
        var textureId = AssetId.Parse("texture.directorate.armor_albedo");
        var materialId = AssetId.Parse("material.directorate.armor");

        Assert.True(catalog.Contains(textureId));
        Assert.True(catalog.Contains(materialId));
        Assert.Equal(RuntimeAssetType.Texture, catalog.Read(textureId).Type);
        Assert.Equal(RuntimeAssetType.Material, catalog.Read(materialId).Type);
        Assert.Contains(
            "texture.directorate.armor_albedo",
            catalog.Get(materialId).Dependencies);
    }

    [Fact]
    public void RejectsDuplicateStableIds()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteTga("a/a.tga", 1, 2, 3, 255);
        workspace.WriteTga("b/b.tga", 4, 5, 6, 255);
        workspace.WriteAsset(
            "a/a.asset.json",
            """{"id":"texture.test.duplicate","type":"texture","source":"a.tga"}""");
        workspace.WriteAsset(
            "b/b.asset.json",
            """{"id":"texture.test.duplicate","type":"texture","source":"b.tga"}""");

        var result = workspace.Compile();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "ASSET006");
    }

    [Fact]
    public void RejectsMissingDependency()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteTga("textures/a.tga", 1, 2, 3, 255);
        workspace.WriteAsset(
            "textures/a.asset.json",
            """
            {
              "id": "texture.test.a",
              "type": "texture",
              "source": "a.tga",
              "dependencies": ["texture.test.missing"]
            }
            """);

        var result = workspace.Compile();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "ASSET018");
    }

    [Fact]
    public void DependencyChangeRebuildsDependentsWhileUnchangedBuildSkips()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteTga("textures/albedo.tga", 10, 20, 30, 255);
        workspace.WriteText(
            "materials/armor.material.json",
            """{"baseColorTexture":"texture.test.albedo"}""");
        workspace.WriteAsset(
            "textures/albedo.asset.json",
            """{"id":"texture.test.albedo","type":"texture","source":"albedo.tga"}""");
        workspace.WriteAsset(
            "materials/armor.asset.json",
            """{"id":"material.test.armor","type":"material","source":"armor.material.json"}""");

        var first = workspace.Compile();
        var second = workspace.Compile();
        workspace.WriteTga("textures/albedo.tga", 40, 50, 60, 255);
        var third = workspace.Compile();

        Assert.True(first.Success);
        Assert.Equal(2, first.CompiledCount);
        Assert.True(second.Success);
        Assert.Equal(0, second.CompiledCount);
        Assert.Equal(2, second.SkippedCount);
        Assert.True(third.Success);
        Assert.Equal(2, third.CompiledCount);
    }

    [Fact]
    public void RejectsMalformedLodsMissingCollisionAndDuplicateSockets()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteText("meshes/unit.gltf", "{}");
        workspace.WriteAsset(
            "meshes/unit.asset.json",
            """
            {
              "id": "unit.test.vehicle",
              "type": "mesh",
              "source": "unit.gltf",
              "collisionRequired": true,
              "lods": [
                {"level":1,"assetId":"unit.test.vehicle_lod","maxDistance":100},
                {"level":1,"assetId":"unit.test.vehicle_lod2","maxDistance":90}
              ],
              "sockets": [
                {"name":"muzzle","x":0,"y":0,"z":0},
                {"name":"muzzle","x":1,"y":0,"z":0}
              ]
            }
            """);

        var result = workspace.Compile();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "ASSET011");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "ASSET013");
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "ASSET014");
    }

    [Fact]
    public void RejectsNonUnitSourceScale()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteTga("textures/a.tga", 1, 2, 3, 255);
        workspace.WriteAsset(
            "textures/a.asset.json",
            """
            {
              "id": "texture.test.scaled",
              "type": "texture",
              "source": "a.tga",
              "scale": 0.01
            }
            """);

        var result = workspace.Compile();

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, static diagnostic => diagnostic.Code == "ASSET010");
    }

    [Fact]
    public void ImportsGltfStaticMeshAndPublishesBounds()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteGltfTriangle("meshes/triangle.gltf");
        workspace.WriteAsset(
            "meshes/triangle.asset.json",
            """{"id":"unit.test.triangle","type":"mesh","source":"triangle.gltf"}""");

        var result = workspace.Compile();
        var catalog = RuntimeAssetCatalog.Load(workspace.RuntimeRoot);
        var record = catalog.Get(AssetId.Parse("unit.test.triangle"));

        Assert.True(result.Success);
        var bounds = record.Bounds
            ?? throw new InvalidOperationException("Compiled mesh did not publish bounds.");
        Assert.Equal(0f, bounds.MinX);
        Assert.Equal(1f, bounds.MaxX);
        Assert.Equal(1f, bounds.MaxY);

        var runtime = catalog.Read(AssetId.Parse("unit.test.triangle"));
        using var stream = new MemoryStream(runtime.Payload);
        using var reader = new BinaryReader(stream);
        Assert.Equal(1, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
        Assert.Equal(3, reader.ReadInt32());
    }

    [Fact]
    public void ImportsGlbStaticMesh()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteGlbTriangle("meshes/triangle.glb");
        workspace.WriteAsset(
            "meshes/triangle.asset.json",
            """{"id":"unit.test.triangle_glb","type":"mesh","source":"triangle.glb"}""");

        var result = workspace.Compile();

        Assert.True(result.Success);
        var catalog = RuntimeAssetCatalog.Load(workspace.RuntimeRoot);
        Assert.True(catalog.Contains(AssetId.Parse("unit.test.triangle_glb")));
    }

    [Fact]
    public void ImportsPngTextureToRgbaRuntimePayload()
    {
        using var workspace = new AssetWorkspace();
        workspace.WritePng("textures/pixel.png", 12, 34, 56, 255);
        workspace.WriteAsset(
            "textures/pixel.asset.json",
            """{"id":"texture.test.pixel","type":"texture","source":"pixel.png"}""");

        var result = workspace.Compile();
        var catalog = RuntimeAssetCatalog.Load(workspace.RuntimeRoot);
        var runtime = catalog.Read(AssetId.Parse("texture.test.pixel"));

        Assert.True(result.Success);

        RuntimeTextureData texture =
            RuntimeTextureData.FromPayload(
                runtime.Payload);

        Assert.Equal(1, texture.Width);
        Assert.Equal(1, texture.Height);
        Assert.Equal(RuntimeTextureFormat.Rgba8Unorm, texture.Format);
        Assert.Equal(RuntimeTextureColorSpace.Srgb, texture.ColorSpace);
        Assert.Equal(RuntimeTextureUsage.Color, texture.Usage);
        Assert.Single(texture.Mips);
        Assert.Equal(
            new byte[] { 12, 34, 56, 255 },
            texture.Mips[0].Pixels);
    }

    [Fact]
    public void StableIdProducesDeterministicRuntimePath()
    {
        using var workspace = new AssetWorkspace();
        workspace.WriteTga("z/a.tga", 1, 2, 3, 255);
        workspace.WriteAsset(
            "z/a.asset.json",
            """{"id":"texture.directorate.test_panel","type":"texture","source":"a.tga"}""");

        var result = workspace.Compile();
        var catalog = RuntimeAssetCatalog.Load(workspace.RuntimeRoot);
        var record = catalog.Get(AssetId.Parse("texture.directorate.test_panel"));

        Assert.True(result.Success);
        Assert.Equal(
            "textures/texture/directorate/test_panel.flasset",
            record.RuntimePath);
    }

    private sealed class AssetWorkspace : IDisposable
    {
        private readonly string root =
            Path.Combine(Path.GetTempPath(), "forgeline-assets-" + Guid.NewGuid().ToString("N"));

        public AssetWorkspace()
        {
            SourceRoot = Path.Combine(root, "source");
            RuntimeRoot = Path.Combine(root, "runtime");
            Directory.CreateDirectory(SourceRoot);
        }

        public string SourceRoot { get; }

        public string RuntimeRoot { get; }

        public AssetCompilationResult Compile(bool clean = false) =>
            AssetPipelineCompiler.Compile(SourceRoot, RuntimeRoot, clean);

        public void WriteAsset(string relativePath, string json) =>
            WriteText(relativePath, json);

        public void WriteText(string relativePath, string content)
        {
            var path = ResolveSource(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void WriteTga(string relativePath, byte r, byte g, byte b, byte a)
        {
            var bytes = new byte[22];
            bytes[2] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14, 2), 1);
            bytes[16] = 32;
            bytes[17] = 0x20;
            bytes[18] = b;
            bytes[19] = g;
            bytes[20] = r;
            bytes[21] = a;
            WriteBytes(relativePath, bytes);
        }

        public void WritePng(string relativePath, byte r, byte g, byte b, byte a)
        {
            using var png = new MemoryStream();
            png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

            Span<byte> ihdr = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], 1);
            BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), 1);
            ihdr[8] = 8;
            ihdr[9] = 6;
            WritePngChunk(png, "IHDR"u8, ihdr);

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write([0, r, g, b, a]);
            }

            WritePngChunk(png, "IDAT"u8, compressed.ToArray());
            WritePngChunk(png, "IEND"u8, []);
            WriteBytes(relativePath, png.ToArray());
        }

        public void WriteGltfTriangle(string relativePath)
        {
            var binary = CreateTriangleBuffer();
            var base64 = Convert.ToBase64String(binary);
            WriteText(relativePath, CreateTriangleJson(
                $"data:application/octet-stream;base64,{base64}",
                binary.Length));
        }

        public void WriteGlbTriangle(string relativePath)
        {
            var binary = CreateTriangleBuffer();
            var json = Encoding.UTF8.GetBytes(CreateTriangleJson(null, binary.Length));
            var paddedJsonLength = Align4(json.Length);
            var paddedBinaryLength = Align4(binary.Length);
            var totalLength = 12 + 8 + paddedJsonLength + 8 + paddedBinaryLength;
            var glb = new byte[totalLength];

            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(0, 4), 0x46546C67);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4, 4), 2);
            BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(8, 4), totalLength);

            var offset = 12;
            BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(offset, 4), paddedJsonLength);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(offset + 4, 4), 0x4E4F534A);
            offset += 8;
            json.CopyTo(glb, offset);
            glb.AsSpan(offset + json.Length, paddedJsonLength - json.Length).Fill(0x20);
            offset += paddedJsonLength;

            BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(offset, 4), paddedBinaryLength);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(offset + 4, 4), 0x004E4942);
            offset += 8;
            binary.CopyTo(glb, offset);

            WriteBytes(relativePath, glb);
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private string ResolveSource(string relativePath) =>
            Path.Combine(SourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private void WriteBytes(string relativePath, byte[] bytes)
        {
            var path = ResolveSource(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        private static byte[] CreateTriangleBuffer()
        {
            var bytes = new byte[42];
            var floats = new[]
            {
                0f, 0f, 0f,
                1f, 0f, 0f,
                0f, 1f, 0f,
            };

            for (var index = 0; index < floats.Length; index++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(index * 4, 4),
                    BitConverter.SingleToInt32Bits(floats[index]));
            }

            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(36, 2), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(38, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(40, 2), 2);
            return bytes;
        }

        private static string CreateTriangleJson(string? uri, int byteLength)
        {
            var buffer = uri is null
                ? $"{{\"byteLength\":{byteLength}}}"
                : $"{{\"byteLength\":{byteLength},\"uri\":{JsonSerializer.Serialize(uri)}}}";

            return $$"""
            {
              "asset":{"version":"2.0"},
              "buffers":[{{buffer}}],
              "bufferViews":[
                {"buffer":0,"byteOffset":0,"byteLength":36},
                {"buffer":0,"byteOffset":36,"byteLength":6}
              ],
              "accessors":[
                {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
                {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}
              ],
              "meshes":[
                {"primitives":[{"attributes":{"POSITION":0},"indices":1,"mode":4}]}
              ]
            }
            """;
        }

        private static int Align4(int value) => (value + 3) & ~3;

        private static void WritePngChunk(
            Stream stream,
            ReadOnlySpan<byte> type,
            ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            stream.Write(length);
            stream.Write(type);
            stream.Write(data);
            stream.Write([0, 0, 0, 0]);
        }
    }
}
