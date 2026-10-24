using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>A bounded consumer of faction knowledge and normal tactical commands.</summary>
public sealed class SkirmishRaidPlanner
{
    private readonly InventoryStore _inventories;
    private readonly WeaponCatalog _weapons;
    private readonly FactionIntelligenceStore _intelligence;
    private readonly HierarchicalNavigationSystem _navigation;
    private readonly HierarchicalPathfinder _routes;
    private readonly Dictionary<EntityId, SkirmishRaidReadModel> _diagnostics = new();

    public SkirmishRaidPlanner(InventoryStore inventories, WeaponCatalog weapons,
        FactionIntelligenceStore intelligence, HierarchicalNavigationSystem navigation)
    {
        _inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
        _weapons = weapons ?? throw new ArgumentNullException(nameof(weapons));
        _intelligence = intelligence ?? throw new ArgumentNullException(nameof(intelligence));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _routes = new HierarchicalPathfinder(navigation.World);
    }

    public SkirmishRaidReadModel Capture(EntityId controller) =>
        _diagnostics.GetValueOrDefault(controller);

    public void Forget(EntityId controller) => _diagnostics.Remove(controller);

    public void Evaluate(SimulationContext context, EntityId controllerEntity,
        SkirmishOpponentController controller, IReadOnlyList<EntityId> ownedCombatUnits,
        FactionIntelligenceSnapshot knowledge, SkirmishOpponentConfiguration configuration,
        bool priority)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ownedCombatUnits);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!context.Entities.IsAlive(controllerEntity) || knowledge.Faction != controller.Faction)
            return;
        var policy = configuration.Raids;
        if (_routes.World.Version != _navigation.World.Version)
            _routes.UpdateWorld(_navigation.World);
        context.Entities.TryGetComponent(controllerEntity, out SkirmishRaidState state);
        int count = Math.Min(knowledge.Contacts.Count, policy.MaximumContacts);
        SkirmishRaidReason reason;
        if (state.IsActive)
        {
            state = Advance(context, controller, state, knowledge, policy, priority || !policy.Enabled);
            Store(context.Entities, controllerEntity, state);
            reason = state.Reason;
        }
        else if (!policy.Enabled || priority)
            reason = SkirmishRaidReason.Priority;
        else if (context.Tick < state.NextAllowedTick)
            reason = SkirmishRaidReason.Cooldown;
        else
        {
            state = Select(context, controller, state, ownedCombatUnits, knowledge, configuration, out reason);
            Store(context.Entities, controllerEntity, state);
        }
        _diagnostics[controllerEntity] = new(state, reason, count);
    }

    private SkirmishRaidState Select(SimulationContext context, SkirmishOpponentController controller,
        SkirmishRaidState previous, IReadOnlyList<EntityId> owned,
        FactionIntelligenceSnapshot knowledge, SkirmishOpponentConfiguration configuration,
        out SkirmishRaidReason reason)
    {
        var policy = configuration.Raids;
        reason = SkirmishRaidReason.NoKnownTarget;
        var candidates = new List<(IntelligenceContact Contact, EntityId Entity, TargetClass Class, int Rank)>();
        // The faction store supplies stable contact order. Never enumerate the hostile world.
        for (int i = 0; i < Math.Min(policy.MaximumContacts, knowledge.Contacts.Count); i++)
        {
            var contact = knowledge.Contacts[(previous.ContactCursor + i) % knowledge.Contacts.Count];
            if (!contact.IsCurrent || !contact.IsIdentified ||
                Distance(controller.HomePosition, contact.LastKnownPosition) > policy.MaximumDistanceMeters ||
                !_intelligence.TryResolveCurrentlyIdentifiedEntity(controller.Faction, contact.ContactKey, out var target) ||
                !context.Entities.TryGetComponent(target, out Targetable targetable) ||
                !context.Entities.TryGetComponent(target, out Combatant combatant) || combatant.Faction == controller.Faction) continue;
            int rank = TargetRank(context.Entities, target);
            if (rank > 0) candidates.Add((contact, target, targetable.Class, rank));
        }
        foreach (var candidate in candidates.OrderByDescending(c => c.Rank)
                     .ThenBy(c => Distance(controller.HomePosition, c.Contact.LastKnownPosition))
                     .ThenBy(c => c.Entity))
        {
            if (KnownThreat(context.Entities, controller.Faction, knowledge,
                    candidate.Contact.LastKnownPosition, policy) > policy.MaximumKnownThreat)
            { reason = SkirmishRaidReason.KnownDefense; continue; }

            var selected = new List<EntityId>(3);
            int home = owned.Count(e => IsReady(context.Entities, e, controller.Player, policy) &&
                IsIdle(context.Entities, e) && Distance(Position(context.Entities, e), controller.HomePosition) <= policy.HomeRadiusMeters);
            if (home < policy.HomeDefenders) { reason = SkirmishRaidReason.NoEligibleForce; continue; }
            int idleReady = owned.Count(e => IsReady(context.Entities, e, controller.Player, policy) && IsIdle(context.Entities, e));
            int pressure = owned.Count(e => context.Entities.TryGetComponent(e, out UnitIdentity identity) &&
                identity.UnitId == UnitIds.MainBattleTank);
            int reserve = idleReady >= configuration.MinimumAttackUnits && pressure >= configuration.MinimumObjectivePressureUnits
                ? configuration.MinimumAttackUnits : policy.HomeDefenders;
            foreach (EntityId unit in owned.OrderBy(e => context.Entities.TryGetComponent(e, out UnitIdentity identity) &&
                         identity.UnitId == UnitIds.MainBattleTank ? 1 : 0).ThenBy(e => e))
            {
                if (selected.Count == policy.MaximumUnits || idleReady - selected.Count <= reserve) break;
                if (!IsReady(context.Entities, unit, controller.Player, policy) || !IsIdle(context.Entities, unit) ||
                    !TryWeapon(context.Entities, unit, out var weapon) || !weapon.Effectiveness.CanEngage(candidate.Class)) continue;
                bool tank = context.Entities.GetComponent<UnitIdentity>(unit).UnitId == UnitIds.MainBattleTank;
                if (tank && pressure <= configuration.MinimumObjectivePressureUnits) continue;
                bool atHome = Distance(Position(context.Entities, unit), controller.HomePosition) <= 120;
                if (atHome && home <= policy.HomeDefenders) continue;
                if (!CanTravel(context.Entities, unit, candidate.Contact.LastKnownPosition, controller.HomePosition,
                        policy, roundTrip: true, out reason)) continue;
                if (!HasAmmunition(context.Entities, unit, weapon.AmmunitionPerShot * policy.MinimumShots))
                { reason = SkirmishRaidReason.Ammunition; continue; }
                selected.Add(unit);
                if (tank) pressure--;
                if (atHome) home--;
            }
            if (selected.Count < policy.MinimumUnits)
            { if (reason is SkirmishRaidReason.NoKnownTarget or SkirmishRaidReason.None) reason = SkirmishRaidReason.NoEligibleForce; continue; }

            var move = new AttackMoveCommand(controller.Player, selected.ToArray(), candidate.Contact.LastKnownPosition,
                context.Tick, FormationTemplate.Compact, policy.PursuitLeashMeters);
            move.Execute(context);
            reason = SkirmishRaidReason.None;
            return new(SkirmishRaidPhase.Approaching, reason, candidate.Contact.ContactKey, candidate.Entity,
                candidate.Contact.LastKnownPosition, candidate.Contact.LastKnownPosition, controller.HomePosition,
                context.Tick, candidate.Contact.LastSeenTick, previous.NextAllowedTick, move.CreatedCombatGroup,
                selected[0], selected[1], selected.Count == 3 ? selected[2] : EntityId.Invalid)
                { OrderedPosition = candidate.Contact.LastKnownPosition, ContactCursor = previous.ContactCursor };
        }
        return previous with { ContactCursor = knowledge.Contacts.Count == 0 ? 0 :
            (previous.ContactCursor + Math.Min(policy.MaximumContacts, knowledge.Contacts.Count)) % knowledge.Contacts.Count };
    }

    private SkirmishRaidState Advance(SimulationContext context, SkirmishOpponentController controller,
        SkirmishRaidState state, FactionIntelligenceSnapshot knowledge, SkirmishRaidConfiguration policy, bool priority)
    {
        EntityId[] members = Members(context.Entities, controller.Player, state);
        if (members.Length == 0)
        {
            bool lost = new[] { state.First, state.Second, state.Third }
                .Where(e => e.IsValid).All(e => !context.Entities.IsAlive(e));
            return Finish(context, state, policy, lost ? SkirmishRaidReason.UnitsLost : SkirmishRaidReason.OrdersReplaced);
        }
        if (priority)
        {
            // Release only the orders that still belong to this raid. The next normal decision may defend.
            new StopCombatCommand(controller.Player, members, context.Tick).Execute(context);
            return Finish(context, state, policy, SkirmishRaidReason.Priority);
        }
        if (context.Tick.Value - state.StartedAtTick.Value >= policy.ActiveTicks)
        {
            // The tactical return remains valid after the bounded strategic intent expires.
            if (state.Phase is not (SkirmishRaidPhase.Returning or SkirmishRaidPhase.Resupplying))
                state = Retreat(context, controller, state, members, policy, SkirmishRaidReason.DurationExpired);
            return Finish(context, state, policy, SkirmishRaidReason.DurationExpired);
        }
        if (state.Phase is SkirmishRaidPhase.Returning or SkirmishRaidPhase.Resupplying)
        {
            if (members.All(e => Distance(Position(context.Entities, e), state.ReturnPoint) <= 16))
            {
                new StopCombatCommand(controller.Player, members, context.Tick).Execute(context);
                return Finish(context, state, policy, SkirmishRaidReason.Returned);
            }
            if (state.Phase == SkirmishRaidPhase.Resupplying)
            {
                foreach (EntityId unit in members)
                    BattlefieldResupplyPlanner.TryIssueNearestProviderOrder(context, _inventories, unit,
                        controller.Player, context.Tick, BattlefieldSupplyResource.All, out _);
                if (members.All(e => CanTravel(context.Entities, e, state.ReturnPoint, state.ReturnPoint,
                        policy, false, out _))) return Retreat(context, controller, state, members, policy, state.Reason);
            }
            return state;
        }
        if (members.Length < policy.MinimumUnits)
            return Retreat(context, controller, state, members, policy, SkirmishRaidReason.UnitsLost);
        foreach (EntityId unit in members)
        {
            if (!IsReady(context.Entities, unit, controller.Player, policy, continuing: true) ||
                !TryWeapon(context.Entities, unit, out var weapon) ||
                !HasAmmunition(context.Entities, unit, weapon.AmmunitionPerShot * 2))
                return Retreat(context, controller, state, members, policy, SkirmishRaidReason.Ammunition);
            if (!CanTravel(context.Entities, unit, state.LastKnownPosition, state.ReturnPoint,
                    policy, true, out var reason))
                return Retreat(context, controller, state, members, policy, reason);
        }
        if (KnownThreat(context.Entities, controller.Faction, knowledge, state.LastKnownPosition, policy) > policy.MaximumKnownThreat)
            return Retreat(context, controller, state, members, policy, SkirmishRaidReason.KnownDefense);

        IntelligenceContact? current = _intelligence.TryGetContact(controller.Faction, state.Contact, out var foundContact)
            ? foundContact : null;
        bool identified = current is { IsCurrent: true, IsIdentified: true } &&
            _intelligence.TryResolveCurrentlyIdentifiedEntity(controller.Faction, state.Contact, out EntityId target) &&
            target == state.Target;
        if (!identified)
        {
            if (context.Tick.Value - state.LastSeenTick.Value > policy.SearchTicks)
                return Retreat(context, controller, state, members, policy, SkirmishRaidReason.ContactLost);
            if (state.Phase == SkirmishRaidPhase.Searching) return state;
            var search = new AttackMoveCommand(controller.Player, members, state.LastKnownPosition,
                context.Tick, FormationTemplate.Compact, policy.PursuitLeashMeters);
            search.Execute(context);
            return state with { Phase = SkirmishRaidPhase.Searching, Reason = SkirmishRaidReason.ContactLost,
                Group = search.CreatedCombatGroup, OrderedPosition = state.LastKnownPosition };
        }
        Vector3 position = current!.Value.LastKnownPosition;
        if (Distance(position, state.TargetAnchor) > policy.PursuitLeashMeters)
            return Retreat(context, controller, state, members, policy, SkirmishRaidReason.TargetEscaped);
        bool inRange = members.All(e => TryWeapon(context.Entities, e, out var weapon) &&
            Distance(Position(context.Entities, e), position) <= weapon.RangeMeters * 0.9f);
        if (inRange && state.Phase != SkirmishRaidPhase.Attacking)
        {
            var attack = new AttackCommand(controller.Player, members, state.Target, context.Tick, policy.PursuitLeashMeters);
            attack.Execute(context);
            state = state with { Phase = SkirmishRaidPhase.Attacking, Group = attack.CreatedCombatGroup };
        }
        else if (!inRange && (state.Phase == SkirmishRaidPhase.Searching ||
                 Distance(state.OrderedPosition, position) > 40))
        {
            var move = new AttackMoveCommand(controller.Player, members, position,
                context.Tick, FormationTemplate.Compact, policy.PursuitLeashMeters);
            move.Execute(context);
            state = state with { Phase = SkirmishRaidPhase.Approaching, Group = move.CreatedCombatGroup, OrderedPosition = position };
        }
        return state with { LastKnownPosition = position, LastSeenTick = current.Value.LastSeenTick, Reason = SkirmishRaidReason.None };
    }

    private SkirmishRaidState Retreat(SimulationContext context, SkirmishOpponentController controller,
        SkirmishRaidState state, EntityId[] members, SkirmishRaidConfiguration policy, SkirmishRaidReason reason)
    {
        bool affordable = members.All(e => CanTravel(context.Entities, e, state.ReturnPoint, state.ReturnPoint, policy, false, out _));
        EntityId group;
        if (affordable)
        {
            var retreat = new RetreatCommand(controller.Player, members, state.ReturnPoint, context.Tick, FormationTemplate.Column);
            retreat.Execute(context); group = retreat.CreatedCombatGroup;
        }
        else
        {
            var hold = new HoldPositionCommand(controller.Player, members, context.Tick);
            hold.Execute(context); group = hold.CreatedCombatGroup;
            foreach (EntityId unit in members)
                BattlefieldResupplyPlanner.TryIssueNearestProviderOrder(context, _inventories, unit,
                    controller.Player, context.Tick, BattlefieldSupplyResource.All, out _);
        }
        return state with { Phase = affordable ? SkirmishRaidPhase.Returning : SkirmishRaidPhase.Resupplying,
            Reason = reason, Group = group };
    }

    private bool CanTravel(EntityRegistry entities, EntityId unit, Vector3 target, Vector3 home,
        SkirmishRaidConfiguration policy, bool roundTrip, out SkirmishRaidReason reason)
    {
        reason = SkirmishRaidReason.UnknownRoute;
        if (!entities.TryGetComponent(unit, out NavigationAgent agent)) return false;
        var outbound = _routes.FindPath(Position(entities, unit), target, agent.Capabilities, true);
        if (!outbound.Succeeded || outbound.Path is null) return false;
        double length = outbound.Path.Diagnostics.RouteLengthMeters;
        if (roundTrip)
        {
            var inbound = _routes.FindPath(target, home, agent.Capabilities, true);
            if (!inbound.Succeeded || inbound.Path is null) return false;
            length += inbound.Path.Diagnostics.RouteLengthMeters;
        }
        reason = SkirmishRaidReason.FuelReserve;
        if (!entities.TryGetComponent(unit, out UnitFuelState fuel))
            return agent.MovementClass == NavigationMovementClass.Infantry;
        bool affordable = _inventories.Contains(fuel.InventoryId) &&
            _inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel) >=
            length * fuel.ConsumptionPerMeter * policy.FuelMargin + fuel.Capacity * policy.FuelReserveFraction;
        if (affordable) reason = SkirmishRaidReason.None;
        return affordable;
    }

    private static bool IsReady(EntityRegistry entities, EntityId entity, PlayerId owner,
        SkirmishRaidConfiguration policy, bool continuing = false) =>
        entities.IsAlive(entity) && entities.TryGetComponent(entity, out ControllableEntity controllable) &&
        controllable.IsControllable && controllable.Owner == owner && entities.TryGetComponent(entity, out UnitIdentity identity) &&
        identity.UnitId != UnitIds.ScoutVehicle && identity.UnitId != UnitIds.CargoTruck && identity.UnitId != UnitIds.SupplyTruck &&
        entities.TryGetComponent(entity, out HealthState health) && !health.IsDepleted &&
        entities.HasComponent<WorldTransform>(entity) && entities.HasComponent<WeaponState>(entity) &&
        entities.HasComponent<Combatant>(entity) && entities.TryGetComponent(entity, out UnitCombatReadiness readiness) &&
        readiness.OverallReadiness >= policy.ReadinessThreshold * (continuing ? 0.5 : 1) &&
        !entities.HasComponent<ResupplyOrder>(entity);

    private static bool IsIdle(EntityRegistry entities, EntityId unit) =>
        !entities.HasComponent<MovementOrder>(unit) && !entities.HasComponent<NavigationPendingPath>(unit) &&
        !entities.HasComponent<NavigationRouteState>(unit) &&
        (!entities.TryGetComponent(unit, out CombatOrderState order) || (order.Kind == CombatOrderKind.Stop || order.Kind == CombatOrderKind.HoldPosition &&
          entities.HasComponent<TacticalTestOpponent>(unit)) ||
         entities.TryGetComponent(unit, out TacticalCombatState tactical) &&
         tactical.Status is CombatOrderStatus.Complete or CombatOrderStatus.Cancelled);

    private bool TryWeapon(EntityRegistry entities, EntityId unit, out WeaponDefinition weapon)
    {
        weapon = null!;
        if (!entities.TryGetComponent(unit, out WeaponState state) || !_weapons.TryGet(state.WeaponId, out var found) || found is null)
            return false;
        weapon = found; return true;
    }

    private bool HasAmmunition(EntityRegistry entities, EntityId unit, double quantity) =>
        entities.TryGetComponent(unit, out AmmunitionState ammunition) && _inventories.Contains(ammunition.InventoryId) &&
        _inventories.GetAvailableQuantity(ammunition.InventoryId, ResourceIds.Ammunition) >= quantity;

    internal static bool IsLogisticsTarget(EntityRegistry entities, EntityId target) => TargetRank(entities, target) > 0;

    private static int TargetRank(EntityRegistry entities, EntityId target)
    {
        if (entities.TryGetComponent(target, out UnitIdentity unit))
            return unit.UnitId == UnitIds.CargoTruck || unit.UnitId == UnitIds.SupplyTruck ? 6 : 0;
        if (!entities.TryGetComponent(target, out CompletedBuilding building)) return 0;
        return building.BuildingId == BuildingIds.StorageDepot || building.BuildingId == BuildingIds.SupplyDepot ? 5 :
            building.BuildingId == BuildingIds.PowerPlant ? 4 :
            building.BuildingId == BuildingIds.Smelter || building.BuildingId == BuildingIds.Refinery ||
            building.BuildingId == BuildingIds.ElectronicsPlant || building.BuildingId == BuildingIds.AmmunitionPlant ||
            building.BuildingId == BuildingIds.Barracks || building.BuildingId == BuildingIds.VehicleFactory ? 3 : 0;
    }

    private int KnownThreat(EntityRegistry entities, FactionId faction, FactionIntelligenceSnapshot knowledge,
        Vector3 position, SkirmishRaidConfiguration policy)
    {
        int threat = 0;
        for (int i = 0; i < knowledge.Contacts.Count; i++)
        {
            var contact = knowledge.Contacts[i];
            if (!contact.IsCurrent || !contact.IsIdentified || Distance(position, contact.LastKnownPosition) > policy.ThreatRadiusMeters ||
                !_intelligence.TryResolveCurrentlyIdentifiedEntity(faction, contact.ContactKey, out var entity) ||
                !entities.TryGetComponent(entity, out UnitIdentity unit) || !entities.HasComponent<WeaponState>(entity)) continue;
            threat += unit.UnitId == UnitIds.MainBattleTank || unit.UnitId == UnitIds.MobileArtillery ? 3 : 1;
        }
        return threat;
    }

    private static EntityId[] Members(EntityRegistry entities, PlayerId owner, SkirmishRaidState state) =>
        new[] { state.First, state.Second, state.Third }.Where(e => e.IsValid && entities.IsAlive(e) &&
            entities.TryGetComponent(e, out ControllableEntity controllable) && controllable.Owner == owner &&
            entities.TryGetComponent(e, out CombatGroupMember member) && member.Group == state.Group).ToArray();

    private static SkirmishRaidState Finish(SimulationContext context, SkirmishRaidState state,
        SkirmishRaidConfiguration policy, SkirmishRaidReason reason) => state with
        { Phase = SkirmishRaidPhase.Complete, Reason = reason, NextAllowedTick = new(checked(context.Tick.Value + policy.CooldownTicks)),
            Group = EntityId.Invalid, First = EntityId.Invalid, Second = EntityId.Invalid, Third = EntityId.Invalid };

    private static void Store(EntityRegistry entities, EntityId controller, SkirmishRaidState state)
    {
        if (entities.HasComponent<SkirmishRaidState>(controller)) entities.SetComponent(controller, state);
        else entities.AddComponent(controller, state);
    }

    private static Vector3 Position(EntityRegistry entities, EntityId unit) => entities.GetComponent<WorldTransform>(unit).Position;
    private static float Distance(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
