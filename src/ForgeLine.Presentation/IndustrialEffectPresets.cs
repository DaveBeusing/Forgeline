using ForgeLine.Game;
using ForgeLine.Assets;
using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Cosmetic presets admitted only from visible, completed working states.</summary>
public readonly record struct IndustrialEffectPreset(VfxEffectKind Effect, string Socket)
{
    public Vector3 ResolveAnchor(in BuildingFeaturePresentationMetadata building, in RenderTransform transform,
        RuntimeAssetCatalog? catalog)
    {
        Vector3 local = new(0, MathF.Abs(transform.Scale.Y) * (Effect == VfxEffectKind.IndustrialWelding ? .2f : .8f), 0);
        if (catalog is not null && BuildingPresentationCatalog.TryGet(building.Building, out var definition) &&
            catalog.TryGet(AssetId.Parse(definition.MeshAssetId), out RuntimeAssetRecord? record) && record is not null)
            foreach (var socket in record.Sockets)
                if (socket.Name == Socket) { local = new Vector3(socket.X, socket.Y, socket.Z) * transform.Scale; break; }
        return transform.Position + Vector3.Transform(local, transform.Rotation);
    }

    public static bool TryResolve(in BuildingFeaturePresentationMetadata building, bool working,
        out IndustrialEffectPreset preset)
    {
        preset = default;
        if (!working || building.State != BuildingPresentationState.Operational) return false;
        if (building.Building == BuildingIds.VehicleFactory)
            preset = new(VfxEffectKind.IndustrialWelding, "motion_loading_door");
        else if (building.Building == BuildingIds.Smelter || building.Building == BuildingIds.Refinery ||
                 building.Building == BuildingIds.AmmunitionPlant)
            preset = new(VfxEffectKind.IndustrialSteam, building.Building == BuildingIds.Refinery ? "process_tower"
                : building.Building == BuildingIds.Smelter ? "slag_outlet" : "exhaust");
        return preset.Effect != VfxEffectKind.None;
    }
}
