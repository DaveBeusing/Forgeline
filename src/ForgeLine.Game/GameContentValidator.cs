using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

public sealed class GameContentValidationException : InvalidOperationException
{
    public GameContentValidationException(
        IReadOnlyList<string> errors)
        : base(
            "Game content validation failed:" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                errors.Select(static error => "- " + error)))
    {
        Errors = errors ??
            throw new ArgumentNullException(nameof(errors));
    }

    public IReadOnlyList<string> Errors { get; }
}

public static class GameContentValidator
{
    private static readonly BuildingId[] RequiredDirectorateBuildings =
    [
        BuildingIds.CommandCore,
        BuildingIds.PowerPlant,
        BuildingIds.Extractor,
        BuildingIds.StorageDepot,
        BuildingIds.Smelter,
        BuildingIds.Refinery,
        BuildingIds.ElectronicsPlant,
        BuildingIds.LogisticsHub,
        BuildingIds.Barracks,
        BuildingIds.VehicleFactory,
        BuildingIds.AmmunitionPlant,
        BuildingIds.SupplyDepot,
        BuildingIds.Radar
    ];

    private static readonly UnitId[] RequiredDirectorateUnits =
    [
        UnitIds.RifleSquad,
        UnitIds.CombatEngineer,
        UnitIds.ScoutVehicle,
        UnitIds.MainBattleTank,
        UnitIds.MobileArtillery,
        UnitIds.CargoTruck,
        UnitIds.SupplyTruck
    ];

    public static void ValidateDirectorate(
        FactionContentDefinition faction,
        ResourceCatalog resources,
        BuildingDefinitionCatalog buildings,
        UnitDefinitionCatalog units,
        ProductionRecipeCatalog recipes,
        WeaponCatalog weapons,
        ArmorCatalog armor,
        ArtilleryWeaponCatalog artillery)
    {
        ArgumentNullException.ThrowIfNull(faction);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(recipes);
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(armor);
        ArgumentNullException.ThrowIfNull(artillery);

        var errors = new List<string>();

        TryValidate(
            faction.Validate,
            "Faction definition",
            errors);

        if (faction.Id != DirectorateContent.FactionId)
        {
            errors.Add(
                $"Directorate faction ID must resolve to '{DirectorateContent.FactionId}', got '{faction.Id}'.");
        }

        if (!string.Equals(
                faction.ContentNamespace,
                "directorate",
                StringComparison.Ordinal))
        {
            errors.Add(
                $"Directorate content namespace must be 'directorate', got '{faction.ContentNamespace}'.");
        }

        ValidateRequiredBuildings(
            faction,
            buildings,
            resources,
            recipes,
            errors);
        ValidateRequiredUnits(
            faction,
            buildings,
            units,
            resources,
            weapons,
            armor,
            artillery,
            errors);
        ValidateRecipes(
            resources,
            recipes,
            errors);
        ValidateProductionMenus(
            faction,
            buildings,
            units,
            errors);

        if (errors.Count != 0)
        {
            throw new GameContentValidationException(errors);
        }
    }

    private static void ValidateRequiredBuildings(
        FactionContentDefinition faction,
        BuildingDefinitionCatalog buildings,
        ResourceCatalog resources,
        ProductionRecipeCatalog recipes,
        List<string> errors)
    {
        var factionIds =
            faction.Buildings
                .Select(static entry => entry.BuildingId)
                .ToHashSet();

        foreach (BuildingId required in RequiredDirectorateBuildings)
        {
            if (!factionIds.Contains(required))
            {
                errors.Add(
                    $"Directorate faction is missing required building ID '{required}'.");
                continue;
            }

            if (!buildings.TryGet(
                    required,
                    out BuildingDefinition? definition))
            {
                errors.Add(
                    $"Directorate building ID '{required}' does not resolve.");
                continue;
            }

            ValidateBuildingResources(
                definition,
                resources,
                errors);
            ValidateBuildingProcessing(
                definition,
                recipes,
                errors);
        }

        foreach (BuildingAvailability entry in faction.Buildings)
        {
            if (!buildings.TryGet(entry.BuildingId, out _))
            {
                errors.Add(
                    $"Faction availability references unknown building ID '{entry.BuildingId}'.");
            }
        }
    }

    private static void ValidateBuildingResources(
        BuildingDefinition definition,
        ResourceCatalog resources,
        List<string> errors)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            BuildingResourceCost cost = definition.Costs[index];

            if (!resources.TryGet(cost.ResourceId, out _))
            {
                errors.Add(
                    $"Building '{definition.Key}' references unknown construction resource '{cost.ResourceId}'.");
            }
        }
    }

    private static void ValidateBuildingProcessing(
        BuildingDefinition definition,
        ProductionRecipeCatalog recipes,
        List<string> errors)
    {
        if (!definition.Capabilities.HasFlag(
                BuildingCapability.Processing))
        {
            return;
        }

        bool hasRecipe =
            recipes.Definitions.Any(
                recipe =>
                    (definition.ProductionCapabilities &
                     recipe.RequiredCapability) ==
                    recipe.RequiredCapability);

        if (!hasRecipe)
        {
            errors.Add(
                $"Processing building '{definition.Key}' has no compatible production recipe.");
        }
    }

    private static void ValidateRequiredUnits(
        FactionContentDefinition faction,
        BuildingDefinitionCatalog buildings,
        UnitDefinitionCatalog units,
        ResourceCatalog resources,
        WeaponCatalog weapons,
        ArmorCatalog armor,
        ArtilleryWeaponCatalog artillery,
        List<string> errors)
    {
        var factionUnits =
            faction.Units
                .Select(static entry => entry.UnitId)
                .ToHashSet();

        foreach (UnitId required in RequiredDirectorateUnits)
        {
            if (!factionUnits.Contains(required))
            {
                errors.Add(
                    $"Directorate faction is missing required unit ID '{required}'.");
                continue;
            }

            if (!units.TryGet(
                    required,
                    out UnitDefinition? definition))
            {
                errors.Add(
                    $"Directorate unit ID '{required}' does not resolve.");
                continue;
            }

            if (definition.Faction != faction.Id)
            {
                errors.Add(
                    $"Unit '{definition.Key}' belongs to faction '{definition.Faction}', expected '{faction.Id}'.");
            }

            if (!definition.Key.StartsWith(
                    faction.ContentNamespace + ".",
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Unit '{definition.Key}' is outside the stable namespace '{faction.ContentNamespace}'.");
            }

            ValidateUnitResources(
                definition,
                resources,
                errors);
            ValidateUnitCombatReferences(
                definition,
                weapons,
                armor,
                artillery,
                errors);
            ValidateInitialSupplyConservation(
                definition,
                errors);
            ValidateUnitProductionCoverage(
                faction,
                buildings,
                definition,
                errors);
        }

        foreach (UnitAvailability entry in faction.Units)
        {
            if (!units.TryGet(entry.UnitId, out _))
            {
                errors.Add(
                    $"Faction availability references unknown unit ID '{entry.UnitId}'.");
            }
        }
    }

    private static void ValidateUnitResources(
        UnitDefinition definition,
        ResourceCatalog resources,
        List<string> errors)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            UnitResourceCost cost = definition.Costs[index];

            if (!resources.TryGet(cost.ResourceId, out _))
            {
                errors.Add(
                    $"Unit '{definition.Key}' references unknown production resource '{cost.ResourceId}'.");
            }
        }
    }

    private static void ValidateUnitCombatReferences(
        UnitDefinition definition,
        WeaponCatalog weapons,
        ArmorCatalog armor,
        ArtilleryWeaponCatalog artillery,
        List<string> errors)
    {
        if (definition.WeaponId.IsSpecified &&
            !weapons.TryGet(
                definition.WeaponId,
                out _))
        {
            errors.Add(
                $"Unit '{definition.Key}' references unknown weapon '{definition.WeaponId}'.");
        }

        if (definition.ArtilleryWeaponId.IsSpecified &&
            !artillery.TryGet(
                definition.ArtilleryWeaponId,
                out _))
        {
            errors.Add(
                $"Unit '{definition.Key}' references unknown artillery weapon '{definition.ArtilleryWeaponId}'.");
        }

        if (definition.ArmorProfileId.IsSpecified &&
            !armor.TryGet(
                definition.ArmorProfileId,
                out _))
        {
            errors.Add(
                $"Unit '{definition.Key}' references unknown armor profile '{definition.ArmorProfileId}'.");
        }
    }

    private static void ValidateInitialSupplyConservation(
        UnitDefinition definition,
        List<string> errors)
    {
        double fuelCost =
            GetCost(
                definition,
                ResourceIds.Fuel);
        double ammunitionCost =
            GetCost(
                definition,
                ResourceIds.Ammunition);

        double initialFuel =
            definition.FuelCapacity *
            definition.InitialFuelFraction;
        double initialAmmunition =
            definition.AmmunitionCapacity *
            definition.InitialAmmunitionFraction;

        if (fuelCost + 1e-9 < initialFuel)
        {
            errors.Add(
                $"Unit '{definition.Key}' spawns with {initialFuel:F3} Fuel but production only consumes {fuelCost:F3} Fuel.");
        }

        if (ammunitionCost + 1e-9 < initialAmmunition)
        {
            errors.Add(
                $"Unit '{definition.Key}' spawns with {initialAmmunition:F3} Ammunition but production only consumes {ammunitionCost:F3} Ammunition.");
        }
    }

    private static double GetCost(
        UnitDefinition definition,
        ResourceId resourceId)
    {
        for (int index = 0; index < definition.Costs.Count; index++)
        {
            UnitResourceCost cost = definition.Costs[index];
            if (cost.ResourceId == resourceId)
            {
                return cost.Quantity;
            }
        }

        return 0.0;
    }

    private static void ValidateUnitProductionCoverage(
        FactionContentDefinition faction,
        BuildingDefinitionCatalog buildings,
        UnitDefinition unit,
        List<string> errors)
    {
        UnitAvailability availability =
            faction.Units.First(entry => entry.UnitId == unit.Id);

        bool supported =
            faction.Buildings
                .Where(entry => entry.Tier <= availability.Tier)
                .Select(
                    entry =>
                        buildings.TryGet(
                            entry.BuildingId,
                            out BuildingDefinition? building)
                            ? building
                            : null)
                .Where(static building => building is not null)
                .Any(
                    building =>
                        building!.Capabilities.HasFlag(
                            BuildingCapability.UnitProduction) &&
                        (building.UnitProductionCapabilities &
                         unit.RequiredProductionCapability) ==
                        unit.RequiredProductionCapability);

        if (!supported)
        {
            errors.Add(
                $"Unit '{unit.Key}' has no available production facility at tier '{availability.Tier}'.");
        }
    }

    private static void ValidateRecipes(
        ResourceCatalog resources,
        ProductionRecipeCatalog recipes,
        List<string> errors)
    {
        foreach (ProductionRecipeDefinition recipe in recipes.Definitions)
        {
            foreach (ProductionIngredient input in recipe.Inputs)
            {
                if (!resources.TryGet(input.ResourceId, out _))
                {
                    errors.Add(
                        $"Recipe '{recipe.Key}' references unknown input resource '{input.ResourceId}'.");
                }
            }

            foreach (ProductionIngredient output in recipe.Outputs)
            {
                if (!resources.TryGet(output.ResourceId, out _))
                {
                    errors.Add(
                        $"Recipe '{recipe.Key}' references unknown output resource '{output.ResourceId}'.");
                }
            }
        }
    }

    private static void ValidateProductionMenus(
        FactionContentDefinition faction,
        BuildingDefinitionCatalog buildings,
        UnitDefinitionCatalog units,
        List<string> errors)
    {
        foreach (BuildingAvailability entry in faction.Buildings)
        {
            if (!buildings.TryGet(
                    entry.BuildingId,
                    out BuildingDefinition? building) ||
                !building.Capabilities.HasFlag(
                    BuildingCapability.UnitProduction))
            {
                continue;
            }

            IReadOnlyList<UnitId> menu =
                faction.GetUnitProductionMenu(
                    building,
                    units,
                    ContentAvailabilityTier.MechanizedWarfare);

            if (menu.Count == 0)
            {
                errors.Add(
                    $"Unit production building '{building.Key}' has an empty production menu.");
            }
        }
    }

    private static void TryValidate(
        Action validation,
        string area,
        List<string> errors)
    {
        try
        {
            validation();
        }
        catch (Exception exception)
            when (exception is
                  ArgumentException or
                  InvalidOperationException)
        {
            errors.Add(
                $"{area}: {exception.Message}");
        }
    }
}
