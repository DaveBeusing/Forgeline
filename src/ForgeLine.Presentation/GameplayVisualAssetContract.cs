using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

/// <summary>Renderer-selected IDs are roots too, even when an older manifest has no references to them.</summary>
public static class GameplayVisualAssetContract
{
    public static IReadOnlyDictionary<string, RuntimeAssetType> RequiredAssets()
    {
        var required = new Dictionary<string, RuntimeAssetType>(StringComparer.Ordinal);
        foreach (var building in BuildingPresentationCatalog.All)
            foreach (var state in Enum.GetValues<BuildingPresentationState>())
            {
                if (state == BuildingPresentationState.None) continue;
                var feature = new BuildingFeaturePresentationMetadata(building.Building, state);
                required[BuildingPresentationCatalog.ResolveMaterialAssetId(feature)] = RuntimeAssetType.Material;
                foreach (var lod in Enum.GetValues<BuildingAssetLod>())
                    required[BuildingPresentationCatalog.ResolveMeshAssetId(feature, lod)] = RuntimeAssetType.Mesh;
                if (BuildingPresentationCatalog.ResolveStateAttachmentAssetId(feature) is { } attachment)
                    required[attachment] = RuntimeAssetType.Mesh;
            }
        required["material.infrastructure.directorate.conveyor"] = RuntimeAssetType.Material;
        foreach (var module in Enum.GetValues<ConveyorModule>())
        {
            if (module is ConveyorModule.Route or ConveyorModule.Overpass) continue; // Planning actions expand into authored parts.
            var placement = new ConveyorPlacement(module, Vector3.Zero, default, 0);
            foreach (var status in Enum.GetValues<ConveyorLinkStatus>())
            {
                bool structural = placement.IsSupport || module == ConveyorModule.Endcap;
                if (structural && status is not (ConveyorLinkStatus.Structural or ConveyorLinkStatus.Damaged or
                    ConveyorLinkStatus.Critical or ConveyorLinkStatus.Destroyed)) continue;
                if (!structural && status == ConveyorLinkStatus.Structural) continue;
                var feature = new ConveyorFeaturePresentationMetadata(placement, ConveyorFeaturePresentationMetadata.Surface(status));
                for (int lod = 0; lod < 3; lod++) required[feature.Mesh(lod)] = RuntimeAssetType.Mesh;
            }
        }
        return required;
    }

    public static void Validate(RuntimeAssetCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var failures = new List<string>();
        foreach (var (id, type) in RequiredAssets())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.TryGet(AssetId.Parse(id), out var record) || record is null)
                failures.Add($"{id} (missing {type})");
            else if (record.Type != type)
                failures.Add($"{id} (expected {type}, found {record.Type})");
        }
        if (failures.Count > 0)
            throw new InvalidDataException(
                $"Runtime assets are incompatible with this client: {failures.Count} required visuals are unavailable. " +
                string.Join("; ", failures.Take(8)) +
                ". Source checkout: run pwsh ./build/Start-DevelopmentClient.ps1 from the repository root. " +
                "Packaged installation: replace the complete package, including assets/runtime. " +
                "Check FORGELINE_RUNTIME_ASSETS if an override is configured.");
    }
}
