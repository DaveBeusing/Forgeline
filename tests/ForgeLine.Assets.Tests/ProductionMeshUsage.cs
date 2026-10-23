using System.Text.Json;
using ForgeLine.Assets;
using Xunit;

namespace ForgeLine.Assets.Tests;

/// <summary>Qualification roles from authored references; a rendered role always takes precedence.</summary>
internal sealed class ProductionMeshUsage
{
    private readonly HashSet<string> _rendered = new(StringComparer.Ordinal);
    private readonly HashSet<string> _proxies = new(StringComparer.Ordinal);

    internal IEnumerable<string> NonRenderedMeshIds => _proxies.Where(IsProxyOnly).Order(StringComparer.Ordinal);

    internal bool IsProxyOnly(string id) => _proxies.Contains(id) && !_rendered.Contains(id);

    internal bool IsRendered(string id) => _rendered.Contains(id);

    internal static bool IsPhysicalFamily(string id) =>
        id.StartsWith("unit.directorate.", StringComparison.Ordinal) ||
        id.StartsWith("building.directorate.", StringComparison.Ordinal) ||
        id.StartsWith("infrastructure.directorate.", StringComparison.Ordinal) ||
        id.StartsWith("mesh.world.prop.", StringComparison.Ordinal) ||
        id.StartsWith("mesh.world.resource.", StringComparison.Ordinal) ||
        id.StartsWith("mesh.world.vegetation.", StringComparison.Ordinal);

    internal static ProductionMeshUsage Create(RuntimeAssetManifest manifest, JsonElement conveyorProduction)
    {
        var usage = new ProductionMeshUsage();
        var records = manifest.Assets.ToDictionary(static record => record.Id, StringComparer.Ordinal);
        var collisionTargets = manifest.Assets.Select(static record => record.CollisionReference)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (string id in collisionTargets)
        {
            _ = RequireMesh(id, "collision");
            // Preserve the established collision-ID contract, now requiring an
            // actual typed collision reference. Selection has no naming exemption.
            if (id.Contains(".collision", StringComparison.Ordinal)) usage._proxies.Add(id);
        }

        foreach (RuntimeAssetRecord record in manifest.Assets)
        {
            if (record.Type == RuntimeAssetType.Mesh && IsPhysicalFamily(record.Id) && record.Lods.Count > 0)
                AddRendered(record.Id);
        }

        foreach (JsonElement module in conveyorProduction.GetProperty("modules").EnumerateArray())
        {
            AddConveyorRender(module.GetProperty("assetId").GetString()!);
            foreach (JsonElement variant in module.GetProperty("variants").EnumerateArray())
                AddConveyorRender(variant.GetProperty("assetId").GetString()!);
            AddProxy(module.GetProperty("collisionAssetId").GetString()!);
            AddProxy(module.GetProperty("selectionAssetId").GetString()!);
            AddProxy(module.GetProperty("destroyedSelectionAssetId").GetString()!);
        }
        return usage;

        void AddConveyorRender(string id)
        {
            AddRendered(id);
            // The production catalog enumerates every visible conveyor state;
            // a state's separate collision reference therefore supplies a proxy role.
            if (records[id].CollisionReference is { } collision) AddProxy(collision);
        }

        void AddRendered(string id)
        {
            RuntimeAssetRecord record = RequireMesh(id, "render");
            if (!usage._rendered.Add(id)) return;
            foreach (AssetLodReference lod in record.Lods) AddRendered(lod.AssetId);
        }

        void AddProxy(string id)
        {
            _ = RequireMesh(id, "proxy");
            usage._proxies.Add(id);
        }

        RuntimeAssetRecord RequireMesh(string id, string role)
        {
            Assert.True(records.TryGetValue(id, out RuntimeAssetRecord? record),
                $"Authored {role} use references missing mesh '{id}'.");
            RuntimeAssetRecord target = record!;
            Assert.True(target.Type == RuntimeAssetType.Mesh,
                $"Authored {role} use '{id}' targets {target.Type}, expected Mesh.");
            return target;
        }
    }
}
