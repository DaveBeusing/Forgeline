using System.Text.Json;
using ForgeLine.Assets;
using Xunit;
using Xunit.Sdk;

namespace ForgeLine.Assets.Tests;

public sealed class ProductionMeshUsageTests
{
    private const string Prefix = "infrastructure.directorate.conveyor.fixture";

    [Theory]
    [InlineData("", false)]
    [InlineData(".lod1", false)]
    [InlineData(".flowing", false)]
    [InlineData(".flowing.lod1", false)]
    [InlineData(".destroyed", false)]
    [InlineData(".unregistered.selection", false)]
    [InlineData(".unregistered.collision", false)]
    [InlineData(".pick_volume", true)]
    [InlineData(".collision", true)]
    public void RenderUsesKeepMaterialQualificationIncludingProxyNamesAndSharedRoles(string suffix, bool alsoRendered)
    {
        using var workspace = new MeshWorkspace();
        RuntimeAssetCatalog catalog = workspace.Catalog();
        using var contract = ConveyorContract(alsoRendered,
            extraRender: alsoRendered ? Prefix + suffix : null);
        ProductionMeshUsage usage = ProductionMeshUsage.Create(catalog.Manifest, contract.RootElement);
        RuntimeAssetRecord record = catalog.Get(AssetId.Parse(Prefix + suffix));
        var issues = new List<string>();

        Assert.True(ProductionAssetMaterialCompletenessTests.IsPhysicalProductionMesh(record, usage));
        ProductionAssetMaterialCompletenessTests.QualifyMesh(catalog, record, usage, issues);

        Assert.Contains($"{record.Id}: no production material reference.", issues);
    }

    [Theory]
    [InlineData(".blocking_volume")]
    [InlineData(".pick_volume")]
    [InlineData(".wreck_pick_volume")]
    [InlineData(".collision")]
    public void ExplicitNonRenderedProxiesKeepValidGeometryWithoutArtificialMaterials(string suffix)
    {
        using var workspace = new MeshWorkspace();
        RuntimeAssetCatalog catalog = workspace.Catalog();
        using var contract = ConveyorContract();
        ProductionMeshUsage usage = ProductionMeshUsage.Create(catalog.Manifest, contract.RootElement);
        RuntimeAssetRecord record = catalog.Get(AssetId.Parse(Prefix + suffix));
        var issues = new List<string>();

        Assert.False(ProductionAssetMaterialCompletenessTests.IsPhysicalProductionMesh(record, usage));
        Assert.Contains(record.Id, usage.NonRenderedMeshIds);
        ProductionAssetMaterialCompletenessTests.QualifyMesh(catalog, record, usage, issues);

        Assert.Empty(issues);
    }

    [Fact]
    public void ExplicitRenderUseOutsideHistoricalPrefixesStillRequiresPhysicalMaterials()
    {
        using var workspace = new MeshWorkspace();
        RuntimeAssetCatalog catalog = workspace.Catalog();
        using var contract = ConveyorContract(extraRender: "mesh.contract.visible");
        ProductionMeshUsage usage = ProductionMeshUsage.Create(catalog.Manifest, contract.RootElement);
        RuntimeAssetRecord record = catalog.Get(AssetId.Parse("mesh.contract.visible"));
        var issues = new List<string>();

        ProductionAssetMaterialCompletenessTests.QualifyMesh(catalog, record, usage, issues);

        Assert.Contains("mesh.contract.visible: no production material reference.", issues);
    }

    [Fact]
    public void CollisionReferenceAloneCannotHideMissingMaterialsOnAnOrdinaryRenderMesh()
    {
        using var workspace = new MeshWorkspace();
        RuntimeAssetCatalog catalog = workspace.Catalog();
        string surfaceId = Prefix + ".unregistered.selection";
        RuntimeAssetManifest manifest = catalog.Manifest with
        {
            Assets = catalog.Manifest.Assets.Select(record => record.Id == "mesh.contract.visible"
                ? record with { CollisionReference = surfaceId } : record).ToArray()
        };
        using var contract = ConveyorContract();
        ProductionMeshUsage usage = ProductionMeshUsage.Create(manifest, contract.RootElement);
        var issues = new List<string>();

        ProductionAssetMaterialCompletenessTests.QualifyMesh(catalog,
            catalog.Get(AssetId.Parse(surfaceId)), usage, issues);

        Assert.Contains(surfaceId + ": no production material reference.", issues);
    }

    [Theory]
    [InlineData(true, "proxy bounds are unavailable or invalid")]
    [InlineData(false, "proxy bounds do not enclose its geometry")]
    public void ProxyExemptionStillRejectsMissingOrIncorrectGeometryBounds(bool missing, string expectedMessage)
    {
        using var workspace = new MeshWorkspace();
        RuntimeAssetCatalog catalog = workspace.Catalog();
        using var contract = ConveyorContract();
        ProductionMeshUsage usage = ProductionMeshUsage.Create(catalog.Manifest, contract.RootElement);
        RuntimeAssetRecord record = catalog.Get(AssetId.Parse(Prefix + ".pick_volume")) with
        {
            Bounds = missing ? null : new AssetBounds(0, 0, 0, 0.5f, 0.5f, 0)
        };
        var issues = new List<string>();

        ProductionAssetMaterialCompletenessTests.QualifyMesh(catalog, record, usage, issues);

        Assert.Contains(issues, issue => issue.Contains(expectedMessage, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "references missing mesh")]
    [InlineData(false, "expected Mesh")]
    public void ProxyRolesRequireAnExistingMeshTarget(bool missing, string expectedMessage)
    {
        using var workspace = new MeshWorkspace();
        RuntimeAssetManifest manifest = workspace.Catalog().Manifest;
        string proxyId = Prefix + ".pick_volume";
        manifest = manifest with
        {
            Assets = missing
                ? manifest.Assets.Where(record => record.Id != proxyId).ToArray()
                : manifest.Assets.Select(record => record.Id == proxyId
                    ? record with { Type = RuntimeAssetType.Material } : record).ToArray()
        };
        using var contract = ConveyorContract();

        XunitException failure = Assert.ThrowsAny<XunitException>(() =>
            ProductionMeshUsage.Create(manifest, contract.RootElement));

        Assert.Contains(expectedMessage, failure.Message, StringComparison.Ordinal);
    }

    private static JsonDocument ConveyorContract(bool alsoRendered = false, string? extraRender = null)
    {
        var variants = new List<object>
        {
            new { assetId = Prefix + ".flowing" },
            new { assetId = Prefix + ".flowing.lod1" },
            new { assetId = Prefix + ".destroyed" }
        };
        if (alsoRendered) variants.Add(new { assetId = Prefix + ".pick_volume" });
        if (extraRender is not null) variants.Add(new { assetId = extraRender });
        return JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            modules = new[]
            {
                new
                {
                    assetId = Prefix, variants,
                    collisionAssetId = Prefix + ".blocking_volume",
                    selectionAssetId = Prefix + ".pick_volume",
                    destroyedSelectionAssetId = Prefix + ".wreck_pick_volume"
                }
            }
        }));
    }

    private sealed class MeshWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "mesh-usage-" + Guid.NewGuid().ToString("N"));
        private readonly List<RuntimeAssetRecord> _records = [];

        internal MeshWorkspace()
        {
            Directory.CreateDirectory(_root);
            var triangle = new RuntimeMeshData(RuntimeMeshAttributes.Normal,
                [new(0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1),
                 new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1),
                 new(0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1)],
                [0, 1, 2], [], [new(0, 3, -1)]);
            string[] ids = new[] { "", ".lod1", ".flowing", ".flowing.lod1", ".destroyed",
                    ".blocking_volume", ".pick_volume", ".wreck_pick_volume", ".unregistered.selection",
                    ".collision", ".unregistered.collision" }
                .Select(static suffix => Prefix + suffix).Append("mesh.contract.visible").ToArray();
            foreach (string id in ids)
            {
                string path = id + ".flasset";
                RuntimeAssetFile.Write(Path.Combine(_root, path), RuntimeAssetType.Mesh, [1, 2, 3], triangle.ToPayload());
                _records.Add(new RuntimeAssetRecord
                {
                    Id = id, Type = RuntimeAssetType.Mesh, SourcePath = "triangle.gltf", RuntimePath = path,
                    SourceHash = "source", BuildHash = "build", RuntimeVersion = 1, CompilerVersion = "contract",
                    Bounds = new(0, 0, 0, 1, 1, 0),
                    CollisionReference = id == Prefix ? Prefix + ".blocking_volume" :
                        id == "mesh.contract.visible" ? Prefix + ".collision" : null,
                    Lods = id == Prefix ? [new(1, Prefix + ".lod1", 140)] : []
                });
            }
        }

        internal RuntimeAssetCatalog Catalog()
        {
            File.WriteAllText(Path.Combine(_root, RuntimeAssetCatalog.ManifestFileName), JsonSerializer.Serialize(
                new RuntimeAssetManifest { CompilerVersion = "contract", Assets = _records }, RuntimeAssetCatalog.CreateJsonOptions()));
            return RuntimeAssetCatalog.Load(_root);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
