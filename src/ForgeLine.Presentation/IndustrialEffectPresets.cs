using ForgeLine.Game;

namespace ForgeLine.Presentation;

/// <summary>Cosmetic presets admitted only from visible, completed working states.</summary>
public readonly record struct IndustrialEffectPreset(VfxEffectKind Effect, string Socket)
{
    public static bool TryResolve(in BuildingFeaturePresentationMetadata building, bool working,
        out IndustrialEffectPreset preset)
    {
        preset = default;
        if (!working || building.State != BuildingPresentationState.Operational) return false;
        if (building.Building == BuildingIds.VehicleFactory)
            preset = new(VfxEffectKind.IndustrialWelding, "production_output");
        else if (building.Building == BuildingIds.Smelter || building.Building == BuildingIds.Refinery ||
                 building.Building == BuildingIds.AmmunitionPlant)
            preset = new(VfxEffectKind.IndustrialSteam, "exhaust");
        return preset.Effect != VfxEffectKind.None;
    }
}
