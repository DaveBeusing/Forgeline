using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct CombatReadinessMetrics(
    int UnitCount,
    int GroupCount,
    int ReadyUnits,
    int DegradedUnits,
    int CombatIneffectiveUnits,
    double AverageUnitReadiness,
    double AverageGroupReadiness);

public readonly record struct UnitCombatReadinessReadModel(
    EntityId Entity,
    Vector3 Position,
    UnitCombatReadiness Readiness,
    CombatOrderKind Order,
    bool HasOrder);

public readonly record struct CombatGroupReadinessReadModel(
    EntityId Group,
    CombatOrderKind Intent,
    CombatGroupReadiness Readiness);

public sealed class CombatReadinessDebugSnapshot
{
    private readonly UnitCombatReadinessReadModel[] _units;
    private readonly CombatGroupReadinessReadModel[] _groups;

    internal CombatReadinessDebugSnapshot(
        CombatReadinessMetrics metrics,
        UnitCombatReadinessReadModel[] units,
        CombatGroupReadinessReadModel[] groups)
    {
        Metrics = metrics;
        _units = units;
        _groups = groups;
    }

    public static CombatReadinessDebugSnapshot Empty { get; } =
        new(default, [], []);

    public CombatReadinessMetrics Metrics { get; }

    public IReadOnlyList<UnitCombatReadinessReadModel> Units =>
        _units;

    public IReadOnlyList<CombatGroupReadinessReadModel> Groups =>
        _groups;
}

public sealed class CombatReadinessSystem : ISimulationSystem
{
    private readonly InventoryStore _inventories;
    private readonly WeaponCatalog _weapons;
    private readonly ArtilleryWeaponCatalog? _artilleryWeapons;
    private readonly List<EntityId> _units = new();
    private readonly List<EntityId> _groups = new();
    private readonly Dictionary<EntityId, List<EntityId>> _membersByGroup =
        new();
    private readonly List<UnitCombatReadinessReadModel> _unitDebug = new();
    private readonly List<CombatGroupReadinessReadModel> _groupDebug = new();

    public CombatReadinessSystem(
        InventoryStore inventories,
        WeaponCatalog weapons,
        ArtilleryWeaponCatalog? artilleryWeapons = null)
    {
        _inventories = inventories ??
            throw new ArgumentNullException(nameof(inventories));
        _weapons = weapons ??
            throw new ArgumentNullException(nameof(weapons));
        _artilleryWeapons = artilleryWeapons;
    }

    public SimulationPhase Phase =>
        SimulationPhase.SnapshotEvents;

    public bool DebugCaptureEnabled { get; set; }

    public CombatReadinessMetrics Metrics { get; private set; }

    public CombatReadinessDebugSnapshot LastDebugSnapshot
    {
        get;
        private set;
    } = CombatReadinessDebugSnapshot.Empty;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        GatherUnits(context);
        _unitDebug.Clear();

        int ready = 0;
        int degraded = 0;
        int ineffective = 0;
        double totalReadiness = 0.0;

        for (int index = 0;
             index < _units.Count;
             index++)
        {
            EntityId entity =
                _units[index];

            if (!context.Entities.IsAlive(entity) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out HealthState health))
            {
                continue;
            }

            UnitCombatReadiness readiness =
                CalculateUnitReadiness(
                    context,
                    entity,
                    health);

            SetUnitReadiness(
                context,
                entity,
                readiness);

            totalReadiness +=
                readiness.OverallReadiness;

            if (readiness.OverallReadiness >= 0.75)
            {
                ready++;
            }
            else if (readiness.OverallReadiness >= 0.35)
            {
                degraded++;
            }
            else
            {
                ineffective++;
            }

            if (DebugCaptureEnabled &&
                context.Entities.TryGetComponent(
                    entity,
                    out WorldTransform transform))
            {
                bool hasOrder =
                    context.Entities.TryGetComponent(
                        entity,
                        out CombatOrderState order);

                _unitDebug.Add(
                    new UnitCombatReadinessReadModel(
                        entity,
                        transform.Position,
                        readiness,
                        hasOrder
                            ? order.Kind
                            : default,
                        hasOrder));
            }
        }

        GatherGroups(context);
        _groupDebug.Clear();

        double totalGroupReadiness = 0.0;
        int groupsCalculated = 0;

        for (int index = 0;
             index < _groups.Count;
             index++)
        {
            EntityId group =
                _groups[index];

            if (!context.Entities.IsAlive(group) ||
                !context.Entities.TryGetComponent(
                    group,
                    out CombatGroupIntent intent))
            {
                continue;
            }

            CombatGroupReadiness readiness =
                CalculateGroupReadiness(
                    context,
                    group,
                    intent);

            SetGroupReadiness(
                context,
                group,
                readiness);

            totalGroupReadiness +=
                readiness.OverallReadiness;
            groupsCalculated++;

            if (DebugCaptureEnabled)
            {
                _groupDebug.Add(
                    new CombatGroupReadinessReadModel(
                        group,
                        intent.Kind,
                        readiness));
            }
        }

        int unitCount =
            ready +
            degraded +
            ineffective;

        Metrics =
            new CombatReadinessMetrics(
                unitCount,
                groupsCalculated,
                ready,
                degraded,
                ineffective,
                unitCount > 0
                    ? totalReadiness / unitCount
                    : 0.0,
                groupsCalculated > 0
                    ? totalGroupReadiness / groupsCalculated
                    : 0.0);

        LastDebugSnapshot =
            DebugCaptureEnabled
                ? new CombatReadinessDebugSnapshot(
                    Metrics,
                    _unitDebug.ToArray(),
                    _groupDebug.ToArray())
                : CombatReadinessDebugSnapshot.Empty;
    }

    private void GatherUnits(
        SimulationContext context)
    {
        _units.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<
                     Combatant,
                     HealthState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _units.Add(entity);
        }
    }

    private UnitCombatReadiness CalculateUnitReadiness(
        SimulationContext context,
        EntityId entity,
        in HealthState health)
    {
        double healthFraction =
            Clamp01(health.Fraction);
        double strength =
            health.IsDepleted
                ? 0.0
                : healthFraction;
        double ammunition =
            CalculateAmmunitionFraction(
                context.Entities,
                entity);
        double fuel =
            CalculateFuelFraction(
                context.Entities,
                entity);
        double mobility =
            CalculateMobility(
                context.Entities,
                entity);
        double weaponAvailability =
            CalculateWeaponAvailability(
                context,
                entity);
        double supplyCondition =
            CalculateSupplyCondition(
                context.Entities,
                entity,
                fuel,
                ammunition);
        double combatCapability =
            Clamp01(
                healthFraction *
                ammunition *
                weaponAvailability *
                (0.5 + 0.5 * mobility));
        double overall =
            Clamp01(
                (
                    strength +
                    healthFraction +
                    fuel +
                    ammunition +
                    mobility +
                    weaponAvailability +
                    supplyCondition +
                    combatCapability) /
                8.0);

        return new UnitCombatReadiness(
            strength,
            healthFraction,
            fuel,
            ammunition,
            mobility,
            weaponAvailability,
            supplyCondition,
            combatCapability,
            overall,
            context.Tick);
    }

    private double CalculateAmmunitionFraction(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.TryGetComponent(
                entity,
                out AmmunitionState ammunition))
        {
            return HasWeaponCapability(
                    entities,
                    entity)
                ? 0.0
                : 1.0;
        }

        if (!_inventories.Contains(
                ammunition.InventoryId) ||
            ammunition.Capacity <= 0.0)
        {
            return 0.0;
        }

        return Clamp01(
            _inventories.GetQuantity(
                ammunition.InventoryId,
                ResourceIds.Ammunition) /
            ammunition.Capacity);
    }

    private double CalculateFuelFraction(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.TryGetComponent(
                entity,
                out UnitFuelState fuel))
        {
            return 1.0;
        }

        if (!_inventories.Contains(
                fuel.InventoryId) ||
            fuel.Capacity <= 0.0)
        {
            return 0.0;
        }

        return Clamp01(
            _inventories.GetQuantity(
                fuel.InventoryId,
                ResourceIds.Fuel) /
            fuel.Capacity);
    }

    private static double CalculateMobility(
        EntityRegistry entities,
        EntityId entity)
    {
        if (!entities.HasComponent<GroundMovement>(entity))
        {
            return 1.0;
        }

        if (entities.TryGetComponent(
                entity,
                out SupplyMovementConstraint supply))
        {
            if (!supply.CanMove)
            {
                return 0.0;
            }

            return Clamp01(
                supply.MaximumSpeedScale);
        }

        return 1.0;
    }

    private double CalculateWeaponAvailability(
        SimulationContext context,
        EntityId entity)
    {
        double availability = 0.0;

        if (context.Entities.TryGetComponent(
                entity,
                out WeaponState weapon) &&
            weapon.FireEnabled &&
            _weapons.TryGet(
                weapon.WeaponId,
                out WeaponDefinition? definition) &&
            definition is not null)
        {
            availability =
                context.Tick < weapon.ReloadUntilTick
                    ? 0.5
                    : 1.0;
        }

        if (_artilleryWeapons is not null &&
            context.Entities.TryGetComponent(
                entity,
                out ArtilleryCapability artillery) &&
            _artilleryWeapons.TryGet(
                artillery.WeaponId,
                out ArtilleryWeaponDefinition? artilleryDefinition) &&
            artilleryDefinition is not null)
        {
            availability =
                Math.Max(
                    availability,
                    1.0);
        }

        return availability;
    }

    private static double CalculateSupplyCondition(
        EntityRegistry entities,
        EntityId entity,
        double fuel,
        double ammunition)
    {
        if (entities.TryGetComponent(
                entity,
                out UnitSupplyState supply))
        {
            return Clamp01(
                Math.Min(
                    supply.FuelFraction,
                    supply.AmmunitionFraction));
        }

        bool hasFuel =
            entities.HasComponent<UnitFuelState>(entity);
        bool hasAmmunition =
            entities.HasComponent<AmmunitionState>(entity);

        if (!hasFuel &&
            !hasAmmunition)
        {
            return 1.0;
        }

        return Clamp01(
            Math.Min(
                hasFuel ? fuel : 1.0,
                hasAmmunition
                    ? ammunition
                    : 1.0));
    }

    private static bool HasWeaponCapability(
        EntityRegistry entities,
        EntityId entity) =>
        entities.HasComponent<WeaponState>(entity) ||
        entities.HasComponent<ArtilleryCapability>(entity);

    private CombatGroupReadiness CalculateGroupReadiness(
        SimulationContext context,
        EntityId group,
        in CombatGroupIntent intent)
    {
        _membersByGroup.TryGetValue(
            group,
            out List<EntityId>? members);

        int surviving =
            members?.Count ?? 0;
        double strength =
            intent.InitialMemberCount > 0
                ? Clamp01(
                    surviving /
                    (double)intent.InitialMemberCount)
                : 0.0;

        if (surviving == 0 ||
            members is null)
        {
            return new CombatGroupReadiness(
                strength,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0,
                intent.InitialMemberCount,
                context.Tick);
        }

        double health = 0.0;
        double fuel = 0.0;
        double ammunition = 0.0;
        double mobility = 0.0;
        double weapon = 0.0;
        double supply = 0.0;
        double combat = 0.0;
        int measured = 0;

        for (int index = 0;
             index < members.Count;
             index++)
        {
            EntityId member =
                members[index];

            if (!context.Entities.IsAlive(member) ||
                !context.Entities.TryGetComponent(
                    member,
                    out UnitCombatReadiness readiness))
            {
                continue;
            }

            health += readiness.Health;
            fuel += readiness.Fuel;
            ammunition += readiness.Ammunition;
            mobility += readiness.Mobility;
            weapon += readiness.WeaponAvailability;
            supply += readiness.SupplyCondition;
            combat += readiness.CombatCapability;
            measured++;
        }

        if (measured == 0)
        {
            return new CombatGroupReadiness(
                strength,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                0.0,
                surviving,
                intent.InitialMemberCount,
                context.Tick);
        }

        health /= measured;
        fuel /= measured;
        ammunition /= measured;
        mobility /= measured;
        weapon /= measured;
        supply /= measured;
        combat /= measured;

        double overall =
            Clamp01(
                (
                    strength +
                    health +
                    fuel +
                    ammunition +
                    mobility +
                    weapon +
                    supply +
                    combat) /
                8.0);

        return new CombatGroupReadiness(
            strength,
            health,
            fuel,
            ammunition,
            mobility,
            weapon,
            supply,
            combat,
            overall,
            surviving,
            intent.InitialMemberCount,
            context.Tick);
    }

    private void GatherGroups(
        SimulationContext context)
    {
        _groups.Clear();

        foreach (List<EntityId> members in
                 _membersByGroup.Values)
        {
            members.Clear();
        }

        foreach (EntityId group in
                 context.Entities.Query<CombatGroupIntent>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _groups.Add(group);
        }

        foreach (EntityId member in
                 context.Entities.Query<CombatGroupMember>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CombatGroupMember membership =
                context.Entities.GetComponent<CombatGroupMember>(
                    member);

            if (!context.Entities.IsAlive(
                    membership.Group))
            {
                continue;
            }

            if (!_membersByGroup.TryGetValue(
                    membership.Group,
                    out List<EntityId>? members))
            {
                members =
                    new List<EntityId>();
                _membersByGroup.Add(
                    membership.Group,
                    members);
            }

            members.Add(member);
        }
    }

    private static void SetUnitReadiness(
        SimulationContext context,
        EntityId entity,
        in UnitCombatReadiness readiness)
    {
        if (context.Entities.HasComponent<UnitCombatReadiness>(entity))
        {
            context.Entities.SetComponent(
                entity,
                readiness);
        }
        else
        {
            context.Entities.AddComponent(
                entity,
                readiness);
        }
    }

    private static void SetGroupReadiness(
        SimulationContext context,
        EntityId group,
        in CombatGroupReadiness readiness)
    {
        if (context.Entities.HasComponent<CombatGroupReadiness>(group))
        {
            context.Entities.SetComponent(
                group,
                readiness);
        }
        else
        {
            context.Entities.AddComponent(
                group,
                readiness);
        }
    }

    private static double Clamp01(double value) =>
        Math.Clamp(
            value,
            0.0,
            1.0);
}
