using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed record MatchContent(
    ResourceCatalog Resources,
    BuildingDefinitionCatalog Buildings,
    UnitDefinitionCatalog Units,
    TechnologyDefinitionCatalog Technologies,
    ProductionRecipeCatalog Recipes,
    WeaponCatalog Weapons,
    ArmorCatalog Armor,
    ArtilleryWeaponCatalog Artillery);

public delegate SkirmishStartingBase MatchStartingBaseFactory(
    EntityRegistry entities,
    InventoryStore inventories,
    UnitFactory unitFactory,
    TerrainWorld terrain,
    BattlefieldStartPosition start,
    MatchContent content,
    SkirmishStartingStock stock);

// Catalog and terrain ownership belongs to the composition; scheduler ownership is separate.
// Key must identify a reproducible map/content/system composition for save/replay reconstruction.
public sealed record MatchComposition(
    string Key,
    BattlefieldDefinition Battlefield,
    MatchContent Content,
    Func<BattlefieldDefinition, TerrainWorld> CreateTerrain,
    MatchStartingBaseFactory CreateStartingBase,
    Func<IReadOnlyList<ISimulationSystem>, IReadOnlyList<ISimulationSystem>> ConfigureSystems)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentNullException.ThrowIfNull(Battlefield);
        ArgumentNullException.ThrowIfNull(Content);
        ArgumentNullException.ThrowIfNull(Content.Resources);
        ArgumentNullException.ThrowIfNull(Content.Buildings);
        ArgumentNullException.ThrowIfNull(Content.Units);
        ArgumentNullException.ThrowIfNull(Content.Technologies);
        ArgumentNullException.ThrowIfNull(Content.Recipes);
        ArgumentNullException.ThrowIfNull(Content.Weapons);
        ArgumentNullException.ThrowIfNull(Content.Armor);
        ArgumentNullException.ThrowIfNull(Content.Artillery);
        ArgumentNullException.ThrowIfNull(CreateTerrain);
        ArgumentNullException.ThrowIfNull(CreateStartingBase);
        ArgumentNullException.ThrowIfNull(ConfigureSystems);
        BattlefieldValidator.ValidateDefinition(Battlefield);
    }
}
