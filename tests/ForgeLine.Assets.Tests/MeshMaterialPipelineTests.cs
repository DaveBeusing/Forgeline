using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

public sealed class MeshMaterialPipelineTests
{
    [Fact]
    public void ImportedUvTangentsAndStableMaterialSlotReachRuntimeMesh()
    {
        using var workspace =
            new MeshWorkspace();
        workspace.WritePlainMaterial(
            "material.test.surface");
        workspace.WriteTriangle(
            "meshes/triangle.gltf",
            includeNormals: true,
            includeUv: true,
            includeTangents: true,
            mirroredUv: false);
        workspace.WriteMeshAsset(
            "mesh.test.triangle",
            "meshes/triangle.gltf",
            "material.test.surface");

        AssetCompilationResult result =
            workspace.Compile();
        RuntimeMeshData mesh =
            workspace.ReadMesh(
                "mesh.test.triangle");

        Assert.True(result.Success);
        Assert.True(mesh.HasNormals);
        Assert.True(mesh.HasUv0);
        Assert.True(mesh.HasTangents);
        Assert.Equal(
            "material.test.surface",
            Assert.Single(mesh.MaterialIds));
        Assert.Equal(
            0,
            Assert.Single(mesh.Sections).MaterialSlot);
    }

    [Fact]
    public void NormalMappedMaterialGeneratesTangentsAndMirroredUvChangesHandedness()
    {
        using var workspace =
            new MeshWorkspace();
        workspace.WriteNormalMappedMaterial();
        workspace.WriteTriangle(
            "meshes/standard.gltf",
            includeNormals: true,
            includeUv: true,
            includeTangents: false,
            mirroredUv: false);
        workspace.WriteTriangle(
            "meshes/mirrored.gltf",
            includeNormals: true,
            includeUv: true,
            includeTangents: false,
            mirroredUv: true);
        workspace.WriteMeshAsset(
            "mesh.test.standard",
            "meshes/standard.gltf",
            "material.test.normal_mapped");
        workspace.WriteMeshAsset(
            "mesh.test.mirrored",
            "meshes/mirrored.gltf",
            "material.test.normal_mapped");

        AssetCompilationResult result =
            workspace.Compile();
        RuntimeMeshData standard =
            workspace.ReadMesh(
                "mesh.test.standard");
        RuntimeMeshData mirrored =
            workspace.ReadMesh(
                "mesh.test.mirrored");

        Assert.True(result.Success);
        Assert.True(standard.HasTangents);
        Assert.True(mirrored.HasTangents);
        Assert.NotEqual(
            MathF.Sign(
                standard.Vertices[0].TangentW),
            MathF.Sign(
                mirrored.Vertices[0].TangentW));
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                "ASSETI002" &&
                diagnostic.Message.Contains(
                    "generatedTangents=3",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void TexturedMaterialWithoutUvUsesControlledDevelopmentFallback()
    {
        using var workspace =
            new MeshWorkspace();
        workspace.WriteBaseColorMappedMaterial();
        workspace.WriteTriangle(
            "meshes/no_uv.gltf",
            includeNormals: true,
            includeUv: false,
            includeTangents: false,
            mirroredUv: false);
        workspace.WriteMeshAsset(
            "mesh.test.no_uv",
            "meshes/no_uv.gltf",
            "material.test.base_color");

        AssetCompilationResult result =
            workspace.Compile();
        RuntimeMeshData mesh =
            workspace.ReadMesh(
                "mesh.test.no_uv");

        Assert.True(result.Success);
        Assert.False(mesh.HasUv0);
        Assert.Equal(
            -1,
            Assert.Single(mesh.Sections).MaterialSlot);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                "ASSETW002" &&
                diagnostic.Severity ==
                AssetCompilerDiagnosticSeverity.Warning);
    }

    [Fact]
    public void PositionOnlyMeshGeneratesNormalsWithoutRequiringTangents()
    {
        using var workspace =
            new MeshWorkspace();
        workspace.WritePlainMaterial(
            "material.test.plain");
        workspace.WriteTriangle(
            "meshes/position_only.gltf",
            includeNormals: false,
            includeUv: false,
            includeTangents: false,
            mirroredUv: false);
        workspace.WriteMeshAsset(
            "mesh.test.position_only",
            "meshes/position_only.gltf",
            "material.test.plain");

        AssetCompilationResult result =
            workspace.Compile();
        RuntimeMeshData mesh =
            workspace.ReadMesh(
                "mesh.test.position_only");

        Assert.True(result.Success);
        Assert.True(mesh.HasNormals);
        Assert.False(mesh.HasTangents);
        Assert.Equal(
            0,
            Assert.Single(mesh.Sections).MaterialSlot);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                "ASSETI002" &&
                diagnostic.Message.Contains(
                    "generatedNormals=3",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void LodMaterialIdentityMustRemainConsistent()
    {
        using var workspace =
            new MeshWorkspace();
        workspace.WritePlainMaterial(
            "material.test.primary");
        workspace.WritePlainMaterial(
            "material.test.other");
        workspace.WriteTriangle(
            "meshes/lod0.gltf",
            includeNormals: true,
            includeUv: false,
            includeTangents: false,
            mirroredUv: false);
        workspace.WriteTriangle(
            "meshes/lod1.gltf",
            includeNormals: true,
            includeUv: false,
            includeTangents: false,
            mirroredUv: false);
        workspace.WriteMeshAsset(
            "mesh.test.lod1",
            "meshes/lod1.gltf",
            "material.test.other");
        workspace.WriteText(
            "meshes/lod0.asset.json",
            """
            {
              "id":"mesh.test.lod0",
              "type":"mesh",
              "source":"lod0.gltf",
              "materialReferences":["material.test.primary"],
              "lods":[
                {"level":1,"assetId":"mesh.test.lod1","maxDistance":100}
              ]
            }
            """);

        AssetCompilationResult result =
            workspace.Compile();

        Assert.False(result.Success);
        Assert.Contains(
            result.Diagnostics,
            static diagnostic =>
                diagnostic.Code ==
                "ASSET033");
    }

    private sealed class MeshWorkspace : IDisposable
    {
        private readonly string _root =
            Path.Combine(
                Path.GetTempPath(),
                "forgeline-mesh-material-" +
                Guid.NewGuid().ToString("N"));

        public MeshWorkspace()
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

        public AssetCompilationResult Compile() =>
            AssetPipelineCompiler.Compile(
                SourceRoot,
                RuntimeRoot,
                clean: true);

        public RuntimeMeshData ReadMesh(
            string id)
        {
            RuntimeAssetCatalog catalog =
                RuntimeAssetCatalog.Load(
                    RuntimeRoot);
            return RuntimeMeshData.FromPayload(
                catalog.Read(
                    AssetId.Parse(
                        id))
                .Payload);
        }

        public void WritePlainMaterial(
            string id)
        {
            string name =
                id.Replace(
                    '.',
                    '_');
            WriteText(
                $"materials/{name}.material.json",
                """
                {
                  "baseColorFactor":[0.4,0.5,0.6,1.0],
                  "roughnessFactor":0.7,
                  "metallicFactor":0.1
                }
                """);
            WriteText(
                $"materials/{name}.asset.json",
                $$"""
                {
                  "id":{{JsonSerializer.Serialize(id)}},
                  "type":"material",
                  "source":{{JsonSerializer.Serialize(name + ".material.json")}}
                }
                """);
        }

        public void WriteNormalMappedMaterial()
        {
            WriteTga(
                "textures/normal.tga",
                128,
                128,
                255,
                255);
            WriteText(
                "textures/normal.asset.json",
                """
                {
                  "id":"texture.test.normal",
                  "type":"texture",
                  "source":"normal.tga",
                  "textureUsage":"normal",
                  "textureColorSpace":"linear",
                  "textureGenerateMipmaps":false
                }
                """);
            WriteText(
                "materials/normal.material.json",
                """
                {
                  "normalTexture":"texture.test.normal"
                }
                """);
            WriteText(
                "materials/normal.asset.json",
                """
                {
                  "id":"material.test.normal_mapped",
                  "type":"material",
                  "source":"normal.material.json"
                }
                """);
        }

        public void WriteBaseColorMappedMaterial()
        {
            WriteTga(
                "textures/base.tga",
                180,
                160,
                120,
                255);
            WriteText(
                "textures/base.asset.json",
                """
                {
                  "id":"texture.test.base",
                  "type":"texture",
                  "source":"base.tga",
                  "textureUsage":"baseColor",
                  "textureColorSpace":"srgb",
                  "textureGenerateMipmaps":false
                }
                """);
            WriteText(
                "materials/base.material.json",
                """
                {
                  "baseColorTexture":"texture.test.base"
                }
                """);
            WriteText(
                "materials/base.asset.json",
                """
                {
                  "id":"material.test.base_color",
                  "type":"material",
                  "source":"base.material.json"
                }
                """);
        }

        public void WriteMeshAsset(
            string id,
            string sourcePath,
            string materialId)
        {
            string directory =
                Path.GetDirectoryName(
                    sourcePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)) ??
                string.Empty;
            string fileName =
                Path.GetFileNameWithoutExtension(
                    sourcePath);
            WriteText(
                $"{directory.Replace(Path.DirectorySeparatorChar, '/')}/{fileName}.asset.json",
                $$"""
                {
                  "id":{{JsonSerializer.Serialize(id)}},
                  "type":"mesh",
                  "source":{{JsonSerializer.Serialize(Path.GetFileName(sourcePath))}},
                  "materialReferences":[{{JsonSerializer.Serialize(materialId)}}]
                }
                """);
        }

        public void WriteTriangle(
            string relativePath,
            bool includeNormals,
            bool includeUv,
            bool includeTangents,
            bool mirroredUv)
        {
            using var binary =
                new MemoryStream();
            using (var writer =
                   new BinaryWriter(
                       binary,
                       Encoding.UTF8,
                       leaveOpen: true))
            {
                float[] positions =
                [
                    0f, 0f, 0f,
                    1f, 0f, 0f,
                    0f, 0f, 1f
                ];
                foreach (float value in positions)
                {
                    writer.Write(value);
                }

                if (includeNormals)
                {
                    for (int index = 0; index < 3; index++)
                    {
                        writer.Write(0f);
                        writer.Write(1f);
                        writer.Write(0f);
                    }
                }

                if (includeUv)
                {
                    float u1 =
                        mirroredUv
                            ? -1f
                            : 1f;
                    writer.Write(0f);
                    writer.Write(0f);
                    writer.Write(u1);
                    writer.Write(0f);
                    writer.Write(0f);
                    writer.Write(1f);
                }

                if (includeTangents)
                {
                    for (int index = 0; index < 3; index++)
                    {
                        writer.Write(1f);
                        writer.Write(0f);
                        writer.Write(0f);
                        writer.Write(-1f);
                    }
                }

                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)2);
            }

            byte[] bytes =
                binary.ToArray();
            int offset =
                0;
            var views =
                new List<string>();
            var accessors =
                new List<string>();
            var attributes =
                new List<string>();

            AddFloatAccessor(
                "POSITION",
                36,
                3,
                "VEC3");

            if (includeNormals)
            {
                AddFloatAccessor(
                    "NORMAL",
                    36,
                    3,
                    "VEC3");
            }

            if (includeUv)
            {
                AddFloatAccessor(
                    "TEXCOORD_0",
                    24,
                    3,
                    "VEC2");
            }

            if (includeTangents)
            {
                AddFloatAccessor(
                    "TANGENT",
                    48,
                    3,
                    "VEC4");
            }

            int indexView =
                views.Count;
            views.Add(
                $$"""{"buffer":0,"byteOffset":{{offset}},"byteLength":6}""");
            int indexAccessor =
                accessors.Count;
            accessors.Add(
                $$"""{"bufferView":{{indexView}},"componentType":5123,"count":3,"type":"SCALAR"}""");

            string uri =
                "data:application/octet-stream;base64," +
                Convert.ToBase64String(
                    bytes);
            string json =
                $$"""
                {
                  "asset":{"version":"2.0"},
                  "buffers":[
                    {
                      "byteLength":{{bytes.Length}},
                      "uri":{{JsonSerializer.Serialize(uri)}}
                    }
                  ],
                  "bufferViews":[{{string.Join(",", views)}}],
                  "accessors":[{{string.Join(",", accessors)}}],
                  "meshes":[
                    {
                      "primitives":[
                        {
                          "attributes":{ {{string.Join(",", attributes)}} },
                          "indices":{{indexAccessor}},
                          "mode":4
                        }
                      ]
                    }
                  ]
                }
                """;
            WriteText(
                relativePath,
                json);

            void AddFloatAccessor(
                string semantic,
                int byteLength,
                int count,
                string type)
            {
                int viewIndex =
                    views.Count;
                views.Add(
                    $$"""{"buffer":0,"byteOffset":{{offset}},"byteLength":{{byteLength}}}""");
                int accessorIndex =
                    accessors.Count;
                accessors.Add(
                    $$"""{"bufferView":{{viewIndex}},"componentType":5126,"count":{{count}},"type":{{JsonSerializer.Serialize(type)}}}""");
                attributes.Add(
                    $"{JsonSerializer.Serialize(semantic)}:{accessorIndex}");
                offset +=
                    byteLength;
            }
        }

        public void WriteText(
            string relativePath,
            string content)
        {
            string path =
                Resolve(
                    relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    path)!);
            File.WriteAllText(
                path,
                content);
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
            byte r,
            byte g,
            byte b,
            byte a)
        {
            var bytes =
                new byte[22];
            bytes[2] =
                2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(
                    12,
                    2),
                1);
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(
                    14,
                    2),
                1);
            bytes[16] =
                32;
            bytes[17] =
                0x20;
            bytes[18] =
                b;
            bytes[19] =
                g;
            bytes[20] =
                r;
            bytes[21] =
                a;

            string path =
                Resolve(
                    relativePath);
            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    path)!);
            File.WriteAllBytes(
                path,
                bytes);
        }

        private string Resolve(
            string relativePath) =>
            Path.Combine(
                SourceRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
    }
}
