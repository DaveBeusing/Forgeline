using System.Buffers.Binary;
using System.Text.Json;
using ForgeLine.AssetCompiler;
using ForgeLine.Assets;
using Xunit;
using Xunit.Sdk;

namespace ForgeLine.Assets.Tests;

public sealed class ProductionAssetCatalogContractTests
{
    [Fact]
    public void NewConveyorModuleExtendsDeclaredCatalogWithoutInvalidatingExistingMembership()
    {
        using var workspace = new SourceWorkspace();
        workspace.WriteMesh("mesh.world.prop.fixture");
        AssetCompilationResult initial = workspace.Compile();
        RuntimeAssetCatalog initialCatalog = RuntimeAssetCatalog.Load(workspace.RuntimeRoot);
        var initialSources = ProductionAssetCatalogContract.ReadSources(workspace.SourceRoot);
        ProductionAssetCatalogContract.AssertMatches(initialSources, initialCatalog.Manifest, initial);

        const string moduleId = "infrastructure.directorate.conveyor.fixture_module";
        workspace.WriteMesh(moduleId);
        AssetCompilationResult expanded = workspace.Compile();
        RuntimeAssetCatalog expandedCatalog = RuntimeAssetCatalog.Load(workspace.RuntimeRoot);
        var expandedSources = ProductionAssetCatalogContract.ReadSources(workspace.SourceRoot);
        ProductionAssetCatalogContract.AssertMatches(expandedSources, expandedCatalog.Manifest, expanded);

        Assert.Equal(RuntimeAssetType.Mesh, expandedCatalog.Get(AssetId.Parse(moduleId)).Type);
        Assert.All(initialSources, source => Assert.Equal(source.Type,
            expandedCatalog.Get(AssetId.Parse(source.Id)).Type));
        Assert.Equal(initialSources.Count + 1, expandedSources.Count);
    }

    [Theory]
    [InlineData("missing", "Missing: [mesh.contract.required]")]
    [InlineData("unexpected", "Unexpected: [mesh.contract.unexpected]")]
    [InlineData("duplicate", "Duplicate runtime asset IDs: mesh.contract.required")]
    [InlineData("type", "declares Mesh, but its runtime record is Material")]
    public void CatalogMembershipFaultsRetainActionableIdentityDiagnostics(string fault, string expectedMessage)
    {
        ProductionSourceAsset[] sources = [new("mesh.contract.required", RuntimeAssetType.Mesh, "required.asset.json")];
        RuntimeAssetRecord required = Record("mesh.contract.required");
        RuntimeAssetRecord[] records = fault switch
        {
            "missing" => [],
            "unexpected" => [required, Record("mesh.contract.unexpected")],
            "duplicate" => [required, required],
            "type" => [required with { Type = RuntimeAssetType.Material }],
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        var manifest = new RuntimeAssetManifest { CompilerVersion = "contract", Assets = records };
        var result = new AssetCompilationResult(true, records.Length, 0, []);

        XunitException failure = Assert.ThrowsAny<XunitException>(() =>
            ProductionAssetCatalogContract.AssertMatches(sources, manifest, result));

        Assert.Contains(expectedMessage, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateSourceIdReportsBothDefinitionsBeforeCompilation()
    {
        using var workspace = new SourceWorkspace();
        workspace.WriteMesh("mesh.contract.required");
        File.Copy(Path.Combine(workspace.SourceRoot, "mesh.contract.required.asset.json"),
            Path.Combine(workspace.SourceRoot, "duplicate.asset.json"));

        XunitException failure = Assert.ThrowsAny<XunitException>(() =>
            ProductionAssetCatalogContract.ReadSources(workspace.SourceRoot));

        Assert.Contains("Duplicate source asset IDs: mesh.contract.required", failure.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate.asset.json", failure.Message, StringComparison.Ordinal);
        Assert.Contains("mesh.contract.required.asset.json", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingMembershipDoesNotHideIncorrectCompilationAccounting()
    {
        ProductionSourceAsset[] sources = [new("mesh.contract.required", RuntimeAssetType.Mesh, "required.asset.json")];
        var manifest = new RuntimeAssetManifest { CompilerVersion = "contract", Assets = [Record(sources[0].Id)] };

        Assert.ThrowsAny<XunitException>(() => ProductionAssetCatalogContract.AssertMatches(sources, manifest,
            new AssetCompilationResult(true, 0, 0, [])));
        ProductionAssetCatalogContract.AssertMatches(sources, manifest, new AssetCompilationResult(true, 0, 1, []));
    }

    private static RuntimeAssetRecord Record(string id) => new()
    {
        Id = id, Type = RuntimeAssetType.Mesh, SourcePath = "triangle.gltf", RuntimePath = id + ".flasset",
        SourceHash = "source", BuildHash = "build", RuntimeVersion = 1, CompilerVersion = "contract"
    };

    private sealed class SourceWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "catalog-contract-" + Guid.NewGuid().ToString("N"));
        internal string SourceRoot => Path.Combine(_root, "source");
        internal string RuntimeRoot => Path.Combine(_root, "runtime");

        internal SourceWorkspace()
        {
            Directory.CreateDirectory(SourceRoot);
            File.WriteAllText(Path.Combine(SourceRoot, "surface.asset.json"),
                """{"id":"material.contract.surface","type":"material","source":"surface.material.json"}""");
            File.WriteAllText(Path.Combine(SourceRoot, "surface.material.json"), "{}");
            byte[] binary = new byte[42];
            float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
            for (int index = 0; index < positions.Length; index++)
                BinaryPrimitives.WriteSingleLittleEndian(binary.AsSpan(index * 4, 4), positions[index]);
            BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(38, 2), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(40, 2), 2);
            string uri = "data:application/octet-stream;base64," + Convert.ToBase64String(binary);
            File.WriteAllText(Path.Combine(SourceRoot, "triangle.gltf"), $$"""
                {
                  "asset":{"version":"2.0"},
                  "buffers":[{"byteLength":42,"uri":{{JsonSerializer.Serialize(uri)}}}],
                  "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":6}],
                  "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}],
                  "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,"mode":4}]}]
                }
                """);
        }

        internal void WriteMesh(string id) => File.WriteAllText(Path.Combine(SourceRoot, id + ".asset.json"),
            $$"""{"id":"{{id}}","type":"mesh","source":"triangle.gltf","materialReferences":["material.contract.surface"]}""");

        internal AssetCompilationResult Compile() => AssetPipelineCompiler.Compile(SourceRoot, RuntimeRoot, clean: true);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
