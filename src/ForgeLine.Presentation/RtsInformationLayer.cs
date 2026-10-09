using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum RtsUiIcon : byte
{
    None = 0,
    ResourceFerrousOre,
    ResourceVolatiles,
    ResourceSilicates,
    ResourceRareElements,
    ResourceSteel,
    ResourceFuel,
    ResourceElectronics,
    ResourceAmmunition,
    UnitInfantry,
    UnitReconnaissance,
    UnitArmor,
    UnitArtillery,
    UnitLogistics,
    UnitRepair,
    BuildingCommand,
    BuildingExtraction,
    BuildingProcessing,
    BuildingFactory,
    BuildingStorage,
    BuildingSupply,
    BuildingPower,
    CommandMove,
    CommandAttack,
    CommandAttackMove,
    CommandStop,
    CommandHold,
    CommandPatrol,
    CommandBuild,
    CommandRepair,
    CommandSupply,
    CommandCancel,
    CursorDefault,
    CursorSelect,
    CursorMove,
    CursorAttack,
    CursorAttackMove,
    CursorBuild,
    CursorRepair,
    CursorSupply,
    CursorInvalid,
    CursorPan,
    CursorDragSelect,
    SupplySupplied,
    SupplyLow,
    SupplyCritical,
    SupplyUnsupplied,
    MinimapFriendlyUnit,
    MinimapVisibleEnemy,
    MinimapDetectedContact,
    MinimapBuilding,
    MinimapCommandStructure,
    MinimapResource,
    MinimapDepot,
    MinimapObjective,
    MinimapSelectedGroup,
    MinimapAttackNotification,
    StatusHealth,
    StatusFuel,
    StatusAmmunition,
    StatusPower,
    StatusAlert
}

public enum RtsUiGlyph : byte
{
    None = 0,
    Ore,
    Drop,
    Crystal,
    Star,
    Ingot,
    Circuit,
    Ammunition,
    Infantry,
    Reconnaissance,
    Armor,
    Artillery,
    Truck,
    Repair,
    Command,
    Extraction,
    Processing,
    Factory,
    Storage,
    Supply,
    Power,
    Move,
    Attack,
    AttackMove,
    Stop,
    Hold,
    Patrol,
    Build,
    Cancel,
    Cursor,
    Select,
    Invalid,
    Pan,
    DragSelect,
    Check,
    LowSupply,
    Critical,
    Empty,
    FriendlyUnit,
    EnemyUnit,
    Contact,
    Building,
    Objective,
    Alert,
    Health
}

public readonly record struct RtsUiIconDefinition(
    RtsUiIcon Icon,
    string AssetId,
    RtsUiGlyph Glyph,
    Vector4 FallbackTint,
    string ShortLabel)
{
    public void Validate()
    {
        if (Icon == RtsUiIcon.None ||
            Glyph == RtsUiGlyph.None)
        {
            throw new InvalidOperationException(
                "RTS UI icon definitions require concrete icon and glyph values.");
        }

        ForgeLine.Assets.AssetId.Parse(AssetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ShortLabel);

        if (!float.IsFinite(FallbackTint.X) ||
            !float.IsFinite(FallbackTint.Y) ||
            !float.IsFinite(FallbackTint.Z) ||
            !float.IsFinite(FallbackTint.W))
        {
            throw new InvalidOperationException(
                $"RTS UI icon '{Icon}' has an invalid fallback tint.");
        }
    }
}

public enum RtsCursorKind : byte
{
    Default = 0,
    Select,
    Move,
    Attack,
    AttackMove,
    Build,
    Repair,
    Supply,
    Invalid,
    Pan,
    DragSelect
}

public enum StrategicOverlayMode : byte
{
    None = 0,
    Logistics,
    Supply,
    Sensors,
    Navigation,
    Power,
    All
}

public enum RtsMinimapSymbolKind : byte
{
    FriendlyUnit = 0,
    VisibleEnemy,
    DetectedContact,
    Building,
    CommandStructure,
    Resource,
    Depot,
    Objective,
    SelectedGroup,
    AttackNotification
}

public enum RtsFogPattern : byte
{
    Solid = 0,
    Hatch,
    Clear
}

public readonly record struct RtsCursorContext(
    bool HasPointer,
    bool IsDragSelecting,
    bool IsPanning,
    bool BuildingPlacementActive,
    bool BuildingPlacementValid,
    TacticalTargetingMode TargetingMode,
    bool TacticalTargetValid,
    bool HoveredSelectable,
    bool HasSelection,
    bool SupplyModeActive = false,
    bool MovementTargetValid = true,
    bool PointerCaptured = false,
    bool MovementSelectionSupported = true,
    bool SupplyTargetSupported = false);

public static class RtsCursorResolver
{
    public static RtsCursorKind Resolve(
        in RtsCursorContext context)
    {
        if (!context.HasPointer || context.PointerCaptured)
        {
            return RtsCursorKind.Default;
        }

        if (context.IsPanning)
        {
            return RtsCursorKind.Pan;
        }

        if (context.IsDragSelecting)
        {
            return RtsCursorKind.DragSelect;
        }

        if (context.BuildingPlacementActive)
        {
            return context.BuildingPlacementValid
                ? RtsCursorKind.Build
                : RtsCursorKind.Invalid;
        }

        if (context.TargetingMode != TacticalTargetingMode.None)
        {
            if (!context.TacticalTargetValid)
            {
                return RtsCursorKind.Invalid;
            }

            return context.TargetingMode switch
            {
                TacticalTargetingMode.Attack => RtsCursorKind.Attack,
                TacticalTargetingMode.AttackMove => RtsCursorKind.AttackMove,
                TacticalTargetingMode.FireMission => RtsCursorKind.Attack,
                TacticalTargetingMode.Retreat => RtsCursorKind.Move,
                _ => RtsCursorKind.Default
            };
        }

        if (context.SupplyModeActive && context.SupplyTargetSupported)
        {
            return RtsCursorKind.Supply;
        }

        if (context.HoveredSelectable)
        {
            return RtsCursorKind.Select;
        }

        return context.HasSelection && context.MovementSelectionSupported
            ? (context.MovementTargetValid ? RtsCursorKind.Move : RtsCursorKind.Invalid)
            : RtsCursorKind.Default;
    }

    public static bool CanMoveSelection(PresentationSnapshot? snapshot, SelectionSet selection, PlayerId player)
    {
        if (snapshot is null || !snapshot.SessionId.IsSpecified || snapshot.PlayerExperience?.IsMatchComplete == true) return false;
        foreach (ref readonly var instance in snapshot.Instances)
            if (selection.Contains(instance.Entity) && instance.Selectable.Owner == player && instance.Selectable.CanMove &&
                (instance.Visibility & RenderVisibilityMask.World) != 0 && !instance.UnitFeature.IsWreck) return true;
        return false;
    }
}

public static class RtsUiLayout
{
    public const float ReferenceDpi = 96.0f;

    public static float ScaleForDpi(uint dpi)
    {
        float value = dpi == 0 ? 1.0f : dpi / ReferenceDpi;
        return Math.Clamp(value, 0.75f, 2.5f);
    }
}

public static class RtsUiIconCatalog
{
    private static readonly Dictionary<RtsUiIcon, RtsUiIconDefinition> Definitions =
        CreateDefinitions();

    public static IReadOnlyCollection<RtsUiIconDefinition> All =>
        Definitions.Values;

    public static RtsUiIconDefinition Get(RtsUiIcon icon) =>
        Definitions.TryGetValue(icon, out RtsUiIconDefinition definition)
            ? definition
            : throw new KeyNotFoundException(
                $"RTS UI icon '{icon}' has no definition.");

    public static bool TryGet(
        RtsUiIcon icon,
        out RtsUiIconDefinition definition) =>
        Definitions.TryGetValue(icon, out definition);

    public static RtsUiIcon ResolveCursor(RtsCursorKind cursor) =>
        cursor switch
        {
            RtsCursorKind.Select => RtsUiIcon.CursorSelect,
            RtsCursorKind.Move => RtsUiIcon.CursorMove,
            RtsCursorKind.Attack => RtsUiIcon.CursorAttack,
            RtsCursorKind.AttackMove => RtsUiIcon.CursorAttackMove,
            RtsCursorKind.Build => RtsUiIcon.CursorBuild,
            RtsCursorKind.Repair => RtsUiIcon.CursorRepair,
            RtsCursorKind.Supply => RtsUiIcon.CursorSupply,
            RtsCursorKind.Invalid => RtsUiIcon.CursorInvalid,
            RtsCursorKind.Pan => RtsUiIcon.CursorPan,
            RtsCursorKind.DragSelect => RtsUiIcon.CursorDragSelect,
            _ => RtsUiIcon.CursorDefault
        };

    public static RtsUiIcon ResolveSupply(BattlefieldSupplyStatus status) =>
        status switch
        {
            BattlefieldSupplyStatus.Supplied => RtsUiIcon.SupplySupplied,
            BattlefieldSupplyStatus.LowSupply => RtsUiIcon.SupplyLow,
            BattlefieldSupplyStatus.Critical => RtsUiIcon.SupplyCritical,
            BattlefieldSupplyStatus.Unsupplied => RtsUiIcon.SupplyUnsupplied,
            _ => RtsUiIcon.SupplyUnsupplied
        };

    public static RtsUiIcon ResolveUnitRole(UnitId unit) =>
        unit switch
        {
            var id when id == UnitIds.RifleSquad => RtsUiIcon.UnitInfantry,
            var id when id == UnitIds.CombatEngineer => RtsUiIcon.UnitRepair,
            var id when id == UnitIds.ScoutVehicle => RtsUiIcon.UnitReconnaissance,
            var id when id == UnitIds.MainBattleTank => RtsUiIcon.UnitArmor,
            var id when id == UnitIds.MobileArtillery => RtsUiIcon.UnitArtillery,
            var id when id == UnitIds.CargoTruck || id == UnitIds.SupplyTruck =>
                RtsUiIcon.UnitLogistics,
            _ => RtsUiIcon.UnitInfantry
        };

    public static RtsUiIcon ResolveBuildingRole(BuildingId building) =>
        building switch
        {
            var id when id == BuildingIds.CommandCore || id == BuildingIds.Radar =>
                RtsUiIcon.BuildingCommand,
            var id when id == BuildingIds.Extractor => RtsUiIcon.BuildingExtraction,
            var id when id == BuildingIds.Smelter ||
                        id == BuildingIds.Refinery ||
                        id == BuildingIds.ElectronicsPlant ||
                        id == BuildingIds.AmmunitionPlant =>
                RtsUiIcon.BuildingProcessing,
            var id when id == BuildingIds.VehicleFactory || id == BuildingIds.Barracks =>
                RtsUiIcon.BuildingFactory,
            var id when id == BuildingIds.StorageDepot => RtsUiIcon.BuildingStorage,
            var id when id == BuildingIds.SupplyDepot || id == BuildingIds.LogisticsHub =>
                RtsUiIcon.BuildingSupply,
            var id when id == BuildingIds.PowerPlant => RtsUiIcon.BuildingPower,
            _ => RtsUiIcon.BuildingStorage
        };

    public static RtsUiIcon ResolveWorldResource(WorldVisualId visual) =>
        visual switch
        {
            WorldVisualId.ResourceFerrousOre => RtsUiIcon.ResourceFerrousOre,
            WorldVisualId.ResourceSilicates => RtsUiIcon.ResourceSilicates,
            WorldVisualId.ResourceVolatiles => RtsUiIcon.ResourceVolatiles,
            WorldVisualId.ResourceRareElements => RtsUiIcon.ResourceRareElements,
            _ => RtsUiIcon.MinimapResource
        };

    public static RtsUiIcon ResolveMinimap(RtsMinimapSymbolKind kind) =>
        kind switch
        {
            RtsMinimapSymbolKind.FriendlyUnit => RtsUiIcon.MinimapFriendlyUnit,
            RtsMinimapSymbolKind.VisibleEnemy => RtsUiIcon.MinimapVisibleEnemy,
            RtsMinimapSymbolKind.DetectedContact => RtsUiIcon.MinimapDetectedContact,
            RtsMinimapSymbolKind.Building => RtsUiIcon.MinimapBuilding,
            RtsMinimapSymbolKind.CommandStructure => RtsUiIcon.MinimapCommandStructure,
            RtsMinimapSymbolKind.Resource => RtsUiIcon.MinimapResource,
            RtsMinimapSymbolKind.Depot => RtsUiIcon.MinimapDepot,
            RtsMinimapSymbolKind.Objective => RtsUiIcon.MinimapObjective,
            RtsMinimapSymbolKind.SelectedGroup => RtsUiIcon.MinimapSelectedGroup,
            RtsMinimapSymbolKind.AttackNotification => RtsUiIcon.MinimapAttackNotification,
            _ => RtsUiIcon.MinimapResource
        };

    private static Dictionary<RtsUiIcon, RtsUiIconDefinition> CreateDefinitions()
    {
        var result = new Dictionary<RtsUiIcon, RtsUiIconDefinition>();
        Vector4 resource = new(0.78f, 0.72f, 0.42f, 1.0f);
        Vector4 friendly = new(0.36f, 0.82f, 0.92f, 1.0f);
        Vector4 industrial = new(0.72f, 0.76f, 0.70f, 1.0f);
        Vector4 command = new(0.92f, 0.76f, 0.30f, 1.0f);
        Vector4 warning = new(0.96f, 0.34f, 0.20f, 1.0f);
        Vector4 neutral = new(0.82f, 0.86f, 0.90f, 1.0f);
        Vector4 supply = new(0.42f, 0.88f, 0.58f, 1.0f);

        Add(RtsUiIcon.ResourceFerrousOre, "ui.icon.resource.ferrous_ore", RtsUiGlyph.Ore, resource, "FE");
        Add(RtsUiIcon.ResourceVolatiles, "ui.icon.resource.volatiles", RtsUiGlyph.Drop, resource, "VOL");
        Add(RtsUiIcon.ResourceSilicates, "ui.icon.resource.silicates", RtsUiGlyph.Crystal, resource, "SIL");
        Add(RtsUiIcon.ResourceRareElements, "ui.icon.resource.rare_elements", RtsUiGlyph.Star, resource, "RARE");
        Add(RtsUiIcon.ResourceSteel, "ui.icon.resource.steel", RtsUiGlyph.Ingot, industrial, "STEEL");
        Add(RtsUiIcon.ResourceFuel, "ui.icon.resource.fuel", RtsUiGlyph.Drop, supply, "FUEL");
        Add(RtsUiIcon.ResourceElectronics, "ui.icon.resource.electronics", RtsUiGlyph.Circuit, friendly, "ELEC");
        Add(RtsUiIcon.ResourceAmmunition, "ui.icon.resource.ammunition", RtsUiGlyph.Ammunition, command, "AMMO");
        Add(RtsUiIcon.UnitInfantry, "ui.icon.unit_role.infantry", RtsUiGlyph.Infantry, friendly, "INF");
        Add(RtsUiIcon.UnitReconnaissance, "ui.icon.unit_role.reconnaissance", RtsUiGlyph.Reconnaissance, friendly, "RECON");
        Add(RtsUiIcon.UnitArmor, "ui.icon.unit_role.armor", RtsUiGlyph.Armor, friendly, "ARMOR");
        Add(RtsUiIcon.UnitArtillery, "ui.icon.unit_role.artillery", RtsUiGlyph.Artillery, friendly, "ARTY");
        Add(RtsUiIcon.UnitLogistics, "ui.icon.unit_role.logistics", RtsUiGlyph.Truck, supply, "LOG");
        Add(RtsUiIcon.UnitRepair, "ui.icon.unit_role.repair", RtsUiGlyph.Repair, supply, "ENG");
        Add(RtsUiIcon.BuildingCommand, "ui.icon.building_role.command", RtsUiGlyph.Command, command, "CMD");
        Add(RtsUiIcon.BuildingExtraction, "ui.icon.building_role.extraction", RtsUiGlyph.Extraction, industrial, "EXTR");
        Add(RtsUiIcon.BuildingProcessing, "ui.icon.building_role.processing", RtsUiGlyph.Processing, industrial, "PROC");
        Add(RtsUiIcon.BuildingFactory, "ui.icon.building_role.factory", RtsUiGlyph.Factory, industrial, "FACT");
        Add(RtsUiIcon.BuildingStorage, "ui.icon.building_role.storage", RtsUiGlyph.Storage, industrial, "STORE");
        Add(RtsUiIcon.BuildingSupply, "ui.icon.building_role.supply", RtsUiGlyph.Supply, supply, "SUP");
        Add(RtsUiIcon.BuildingPower, "ui.icon.building_role.power", RtsUiGlyph.Power, command, "PWR");
        Add(RtsUiIcon.CommandMove, "ui.icon.command.move", RtsUiGlyph.Move, neutral, "MOVE");
        Add(RtsUiIcon.CommandAttack, "ui.icon.command.attack", RtsUiGlyph.Attack, warning, "ATK");
        Add(RtsUiIcon.CommandAttackMove, "ui.icon.command.attack_move", RtsUiGlyph.AttackMove, warning, "A-M");
        Add(RtsUiIcon.CommandStop, "ui.icon.command.stop", RtsUiGlyph.Stop, neutral, "STOP");
        Add(RtsUiIcon.CommandHold, "ui.icon.command.hold", RtsUiGlyph.Hold, neutral, "HOLD");
        Add(RtsUiIcon.CommandPatrol, "ui.icon.command.patrol", RtsUiGlyph.Patrol, neutral, "PAT");
        Add(RtsUiIcon.CommandBuild, "ui.icon.command.build", RtsUiGlyph.Build, industrial, "BUILD");
        Add(RtsUiIcon.CommandRepair, "ui.icon.command.repair", RtsUiGlyph.Repair, supply, "REPAIR");
        Add(RtsUiIcon.CommandSupply, "ui.icon.command.supply", RtsUiGlyph.Supply, supply, "SUPPLY");
        Add(RtsUiIcon.CommandCancel, "ui.icon.command.cancel", RtsUiGlyph.Cancel, warning, "CANCEL");
        Add(RtsUiIcon.CursorDefault, "ui.icon.cursor.default", RtsUiGlyph.Cursor, neutral, "CUR");
        Add(RtsUiIcon.CursorSelect, "ui.icon.cursor.select", RtsUiGlyph.Select, friendly, "SEL");
        Add(RtsUiIcon.CursorMove, "ui.icon.cursor.move", RtsUiGlyph.Move, friendly, "MOVE");
        Add(RtsUiIcon.CursorAttack, "ui.icon.cursor.attack", RtsUiGlyph.Attack, warning, "ATK");
        Add(RtsUiIcon.CursorAttackMove, "ui.icon.cursor.attack_move", RtsUiGlyph.AttackMove, warning, "A-M");
        Add(RtsUiIcon.CursorBuild, "ui.icon.cursor.build", RtsUiGlyph.Build, industrial, "BUILD");
        Add(RtsUiIcon.CursorRepair, "ui.icon.cursor.repair", RtsUiGlyph.Repair, supply, "REP");
        Add(RtsUiIcon.CursorSupply, "ui.icon.cursor.supply", RtsUiGlyph.Supply, supply, "SUP");
        Add(RtsUiIcon.CursorInvalid, "ui.icon.cursor.invalid", RtsUiGlyph.Invalid, warning, "NO");
        Add(RtsUiIcon.CursorPan, "ui.icon.cursor.pan", RtsUiGlyph.Pan, neutral, "PAN");
        Add(RtsUiIcon.CursorDragSelect, "ui.icon.cursor.drag_select", RtsUiGlyph.DragSelect, friendly, "DRAG");
        Add(RtsUiIcon.SupplySupplied, "ui.icon.supply.supplied", RtsUiGlyph.Check, supply, "OK");
        Add(RtsUiIcon.SupplyLow, "ui.icon.supply.low", RtsUiGlyph.LowSupply, command, "LOW");
        Add(RtsUiIcon.SupplyCritical, "ui.icon.supply.critical", RtsUiGlyph.Critical, warning, "CRIT");
        Add(RtsUiIcon.SupplyUnsupplied, "ui.icon.supply.unsupplied", RtsUiGlyph.Empty, warning, "NONE");
        Add(RtsUiIcon.MinimapFriendlyUnit, "ui.icon.minimap.friendly_unit", RtsUiGlyph.FriendlyUnit, friendly, "UNIT");
        Add(RtsUiIcon.MinimapVisibleEnemy, "ui.icon.minimap.visible_enemy", RtsUiGlyph.EnemyUnit, warning, "ENEMY");
        Add(RtsUiIcon.MinimapDetectedContact, "ui.icon.minimap.detected_contact", RtsUiGlyph.Contact, command, "CONTACT");
        Add(RtsUiIcon.MinimapBuilding, "ui.icon.minimap.building", RtsUiGlyph.Building, industrial, "BLDG");
        Add(RtsUiIcon.MinimapCommandStructure, "ui.icon.minimap.command_structure", RtsUiGlyph.Command, command, "CORE");
        Add(RtsUiIcon.MinimapResource, "ui.icon.minimap.resource", RtsUiGlyph.Crystal, resource, "RES");
        Add(RtsUiIcon.MinimapDepot, "ui.icon.minimap.depot", RtsUiGlyph.Supply, supply, "DEPOT");
        Add(RtsUiIcon.MinimapObjective, "ui.icon.minimap.objective", RtsUiGlyph.Objective, command, "OBJ");
        Add(RtsUiIcon.MinimapSelectedGroup, "ui.icon.minimap.selected_group", RtsUiGlyph.Select, friendly, "SEL");
        Add(RtsUiIcon.MinimapAttackNotification, "ui.icon.minimap.attack_notification", RtsUiGlyph.Alert, warning, "ATTACK");
        Add(RtsUiIcon.StatusHealth, "ui.icon.status.health", RtsUiGlyph.Health, friendly, "HP");
        Add(RtsUiIcon.StatusFuel, "ui.icon.status.fuel", RtsUiGlyph.Drop, supply, "FUEL");
        Add(RtsUiIcon.StatusAmmunition, "ui.icon.status.ammunition", RtsUiGlyph.Ammunition, command, "AMMO");
        Add(RtsUiIcon.StatusPower, "ui.icon.status.power", RtsUiGlyph.Power, command, "PWR");
        Add(RtsUiIcon.StatusAlert, "ui.icon.status.alert", RtsUiGlyph.Alert, warning, "ALERT");

        foreach (RtsUiIconDefinition definition in result.Values)
        {
            definition.Validate();
        }

        return result;

        void Add(
            RtsUiIcon icon,
            string assetId,
            RtsUiGlyph glyph,
            Vector4 tint,
            string shortLabel) =>
            result.Add(
                icon,
                new RtsUiIconDefinition(
                    icon,
                    assetId,
                    glyph,
                    tint,
                    shortLabel));
    }
}
