using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public enum DeveloperInventoryRole : uint { BuildingStock = 1, Cargo = 2 }

public sealed partial class DeveloperCommandGateway
{
    public const double MaximumQuantity = 1000000;
    private sealed record ResourceAddition(InventoryId Inventory, ResourceId Resource, double Quantity);
    private sealed class DomainPlan
    {
        internal EntityId Target;
        internal HealthState Health;
        internal double HealthAmount;
        internal ResourceAddition[] Resources = [];
        internal UnitDefinition? Unit;
        internal PlayerId Player;
        internal Vector3 Position, Incoming;
        internal WeaponId Weapon;
    }
    private bool TryPlan(DeveloperRequest request, out DomainPlan plan, out DeveloperFailureReason failure)
    {
        plan = new(); failure = DeveloperFailureReason.InvalidValue;
        if (request.Parameter != DeveloperParameterId.None) return false;
        var values = request.Values; var entities = _runtime.Simulation.Entities; var inventories = _runtime.Inventories;
        bool Kinds(params DeveloperValueKind[] kinds) => values.Count == kinds.Length && values.Select(v => v.Kind).SequenceEqual(kinds);
        bool Amount(double amount, bool zero = false) => double.IsFinite(amount) && amount <= MaximumQuantity && (zero ? amount >= 0 : amount > 0);
        if (request.Operation == DeveloperOperation.SpawnUnit)
        {
            if (request.Target.Kind != DeveloperTargetKinds.Player || !_runtime.MatchConfiguration.Participants.Any(p => p.Player == request.Target.Player))
            { failure = DeveloperFailureReason.InvalidTarget; return false; }
            if (!Kinds(DeveloperValueKind.UnsignedNumber, DeveloperValueKind.RealNumber, DeveloperValueKind.RealNumber, DeveloperValueKind.RealNumber)) return false;
            if (!_runtime.Services.UnitDefinitions.TryGet(new(values[0].AsUInt32()), out var definition)) return false;
            var requested = new Vector3((float)values[1].AsDouble(), (float)values[2].AsDouble(), (float)values[3].AsDouble());
            if (!float.IsFinite(requested.X) || !float.IsFinite(requested.Y) || !float.IsFinite(requested.Z) ||
                !_runtime.UnitFactory.TryGroundProductionPosition(definition, requested, out var grounded))
            { failure = DeveloperFailureReason.PositionBlocked; return false; }
            var grid = _runtime.Services.Navigation.World.Grid;
            var capabilities = NavigationCapabilities.For(definition.MovementClass);
            var half = definition.VisualScale * .5f + new Vector3(.5f, 0, .5f);
            foreach (var point in new[] { grounded, grounded + new Vector3(half.X, 0, half.Z), grounded - new Vector3(half.X, 0, half.Z),
                grounded + new Vector3(half.X, 0, -half.Z), grounded + new Vector3(-half.X, 0, half.Z) })
                if (!grid.TryWorldToCell(point, out var cell) || !grid.IsTraversable(cell, capabilities))
                { failure = DeveloperFailureReason.PositionBlocked; return false; }
            int checkedCount = 0;
            foreach (var entity in entities.Query<WorldTransform, SpatialPresence>())
            {
                if (++checkedCount > 4096) { failure = DeveloperFailureReason.PositionBlocked; return false; }
                var bounds = entities.GetComponent<SpatialPresence>(entity).CreateEntry(entity, entities.GetComponent<WorldTransform>(entity)).Bounds;
                if (grounded.X + half.X >= bounds.Minimum.X && grounded.X - half.X <= bounds.Maximum.X &&
                    grounded.Z + half.Z >= bounds.Minimum.Z && grounded.Z - half.Z <= bounds.Maximum.Z)
                { failure = DeveloperFailureReason.PositionBlocked; return false; }
            }
            plan.Unit = definition; plan.Player = request.Target.Player; plan.Position = grounded;
            failure = DeveloperFailureReason.None; return true;
        }
        if (request.Target.Kind != DeveloperTargetKinds.Entity || !entities.IsAlive(request.Target.Entity) ||
            !entities.TryGetComponent(request.Target.Entity, out ControllableEntity control) ||
            !_runtime.MatchConfiguration.Participants.Any(p => p.Player == control.Owner) ||
            !entities.TryGetComponent(request.Target.Entity, out HealthState health) || health.IsDepleted ||
            entities.HasComponent<ConstructionSite>(request.Target.Entity) ||
            (control.Category & (ControllableEntityCategory.Unit | ControllableEntityCategory.Building)) == 0)
        { failure = DeveloperFailureReason.InvalidTarget; return false; }
        plan.Target = request.Target.Entity; plan.Health = health;
        switch (request.Operation)
        {
            case DeveloperOperation.GrantResource:
                if (!Kinds(DeveloperValueKind.UnsignedNumber, DeveloperValueKind.UnsignedNumber, DeveloperValueKind.RealNumber) || !Amount(values[2].AsDouble())) return false;
                var resource = new ResourceId(values[0].AsUInt32());
                if (!_runtime.Services.Resources.TryGet(resource, out _)) return false;
                var role = (DeveloperInventoryRole)values[1].AsUInt32(); InventoryId inventory;
                if (role == DeveloperInventoryRole.BuildingStock && (control.Category & ControllableEntityCategory.Building) != 0 &&
                    entities.TryGetComponent(plan.Target, out InventoryStorage storage)) inventory = storage.InventoryId;
                else if (role == DeveloperInventoryRole.Cargo && entities.TryGetComponent(plan.Target, out CargoTransport cargo)) inventory = cargo.CargoInventory;
                else { failure = DeveloperFailureReason.InvalidInventoryRole; return false; }
                if (!inventories.Contains(inventory)) { failure = DeveloperFailureReason.InvalidTarget; return false; }
                plan.Resources = [new(inventory, resource, inventories.GetAddableQuantity(inventory, resource, values[2].AsDouble()))];
                break;
            case DeveloperOperation.RepairEntity:
                if (!Kinds(DeveloperValueKind.RealNumber) || !Amount(values[0].AsDouble())) return false;
                double wantedRepair = Math.Min(values[0].AsDouble(), health.Maximum - health.Current);
                plan.HealthAmount = Math.Min(health.Maximum, health.Current + wantedRepair) - health.Current;
                break;
            case DeveloperOperation.RefillUnit:
                if (!Kinds(DeveloperValueKind.RealNumber, DeveloperValueKind.RealNumber) || !Amount(values[0].AsDouble(), true) ||
                    !Amount(values[1].AsDouble(), true) || values.All(v => v.AsDouble() == 0)) return false;
                if (!entities.HasComponent<UnitIdentity>(plan.Target)) { failure = DeveloperFailureReason.InvalidTarget; return false; }
                var additions = new List<ResourceAddition>();
                if (values[0].AsDouble() > 0)
                {
                    if (!entities.TryGetComponent(plan.Target, out UnitFuelState fuel) || !inventories.Contains(fuel.InventoryId))
                    { failure = DeveloperFailureReason.InvalidTarget; return false; }
                    double wanted = Math.Min(values[0].AsDouble(), Math.Max(0, fuel.Capacity - inventories.GetQuantity(fuel.InventoryId, ResourceIds.Fuel)));
                    additions.Add(new(fuel.InventoryId, ResourceIds.Fuel, inventories.GetAddableQuantity(fuel.InventoryId, ResourceIds.Fuel, wanted)));
                }
                if (values[1].AsDouble() > 0)
                {
                    if (!entities.TryGetComponent(plan.Target, out AmmunitionState ammo) || !inventories.Contains(ammo.InventoryId))
                    { failure = DeveloperFailureReason.InvalidTarget; return false; }
                    double wanted = Math.Min(values[1].AsDouble(), Math.Max(0, ammo.Capacity - inventories.GetQuantity(ammo.InventoryId, ResourceIds.Ammunition)));
                    double accepted = inventories.GetAddableQuantity(ammo.InventoryId, ResourceIds.Ammunition, wanted);
                    if (additions.FirstOrDefault(a => a.Inventory == ammo.InventoryId) is { } fuelAddition)
                        accepted = Math.Min(accepted, Math.Max(0, inventories.GetTotalCapacity(ammo.InventoryId) -
                            (inventories.GetTotalQuantity(ammo.InventoryId) + fuelAddition.Quantity)));
                    additions.Add(new(ammo.InventoryId, ResourceIds.Ammunition, accepted));
                }
                plan.Resources = additions.ToArray(); break;
            case DeveloperOperation.DamageEntity:
                if (!_runtime.Services.RegisteredSystemTypes.Contains(typeof(CombatDamageResolutionSystem)) ||
                    !_runtime.Services.RegisteredSystemTypes.Contains(typeof(CombatEntityLifecycleSystem)))
                { failure = DeveloperFailureReason.ProviderUnavailable; return false; }
                if (!Kinds(DeveloperValueKind.RealNumber, DeveloperValueKind.UnsignedNumber, DeveloperValueKind.RealNumber, DeveloperValueKind.RealNumber) ||
                    !Amount(values[0].AsDouble()) || Math.Abs(values[2].AsDouble()) > 1 || Math.Abs(values[3].AsDouble()) > 1) return false;
                var incoming = new Vector3((float)values[2].AsDouble(), 0, (float)values[3].AsDouble());
                if (incoming.LengthSquared() < .000001f || !entities.TryGetComponent(plan.Target, out WorldTransform transform)) return false;
                plan.Weapon = new(values[1].AsUInt32());
                if (!_runtime.Services.Weapons.TryGet(plan.Weapon, out _)) return false;
                plan.Position = transform.Position; plan.Incoming = Vector3.Normalize(incoming); plan.HealthAmount = values[0].AsDouble();
                break;
            default: failure = DeveloperFailureReason.UnsupportedOperation; return false;
        }
        failure = DeveloperFailureReason.None; return true;
    }
    private void ExecuteDomain(DeveloperRequest request, SimulationContext context)
    {
        if (!TryPlan(request, out var plan, out var failure))
        { Complete(request, DeveloperResult.Rejected(request, Revision, failure)); return; }
        var entities = context.Entities;
        switch (request.Operation)
        {
            case DeveloperOperation.GrantResource:
            case DeveloperOperation.RefillUnit:
                var actual = new List<DeveloperAppliedValue>();
                foreach (var addition in plan.Resources)
                {
                    double before = _runtime.Inventories.GetQuantity(addition.Inventory, addition.Resource);
                    if (addition.Quantity > 0 && !_runtime.Inventories.Add(addition.Inventory, addition.Resource, addition.Quantity).Succeeded)
                        throw new InvalidOperationException("Validated developer inventory plan changed on its owner thread.");
                    actual.Add(DeveloperAppliedValue.ForResource(addition.Resource, request.Target,
                        _runtime.Inventories.GetQuantity(addition.Inventory, addition.Resource) - before));
                }
                Applied(request, context.Tick, actual.ToArray());
                break;
            case DeveloperOperation.RepairEntity:
                if (plan.HealthAmount > 0) entities.SetComponent(plan.Target, new HealthState(plan.Health.Current + plan.HealthAmount, plan.Health.Maximum));
                Applied(request, context.Tick, [DeveloperAppliedValue.ForHealth(DeveloperEffectKind.HealthRestored, plan.Target, plan.HealthAmount)]);
                break;
            case DeveloperOperation.SpawnUnit:
                var spawned = _runtime.UnitFactory.Create(plan.Unit!, plan.Position, plan.Player);
                Applied(request, context.Tick, [DeveloperAppliedValue.ForSpawnedEntity(spawned)]);
                break;
            case DeveloperOperation.DamageEntity:
                _damageRequest = request;
                _runtime.Services.CombatRuntime.QueueDamage(EntityId.Invalid, plan.Target, EntityId.Invalid, plan.Weapon, plan.Position,
                    plan.Incoming, new DamagePayload(plan.HealthAmount), request.Correlation);
                break;
        }
    }
    private void OnDamageApplied(Guid correlation, EntityId entity, double amount)
    {
        if (_damageRequest is not { } request || request.Correlation != correlation || request.Target.Entity != entity) return;
        _damageRequest = null;
        Applied(request, _runtime.Simulation.CurrentTick, [DeveloperAppliedValue.ForHealth(DeveloperEffectKind.HealthDamage, entity, amount)]);
    }
}
