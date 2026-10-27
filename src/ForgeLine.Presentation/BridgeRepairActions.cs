using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct BridgeRepairActionReadModel(
    EntityId Engineer, EntityId Bridge, EntityId Source, uint Revision, string Name,
    string Status, double Health, double MaximumHealth, uint Progress, uint Duration,
    bool Paid, double Steel, double Electronics, BridgeRepairFailure Failure)
{
    public string Title => Name + " / " + Status;
    public string Details => $"HP {Health:0}/{MaximumHealth:0} / WORK {Progress}/{Duration} / " +
        (Paid ? "KIT PAID / RESUME OR HAND OVER" : $"120 STEEL + 20 ELECTRONICS / SOURCE {Source} / STOCK {Steel:0} + {Electronics:0}");
}

internal static partial class PlayerActionSnapshotFactory
{
    private static System.Collections.ObjectModel.ReadOnlyCollection<BridgeRepairActionReadModel> CaptureBridgeRepairs(
        SimulationContext context, PresentationExtractionContext extraction, EntityId engineer)
    {
        var entities = context.Entities;
        if (!entities.TryGetComponent(engineer, out UnitIdentity unit) || unit.UnitId != UnitIds.CombatEngineer ||
            !entities.TryGetComponent(engineer, out WorldTransform pose)) return Array.AsReadOnly<BridgeRepairActionReadModel>([]);
        var scenario = extraction.Scenario;
        float Distance(EntityId entity)
        {
            Vector3 delta = entities.GetComponent<WorldTransform>(entity).Position - pose.Position;
            return delta.X * delta.X + delta.Z * delta.Z;
        }
        // Bounded copied actions. Prefer an affordable nearby own storage, then nearest own storage
        // so the authoritative policy can explain a range or material rejection.
        var sources = new List<EntityId>(32);
        foreach (EntityId entity in entities.Query<CompletedBuilding, InventoryStorage>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            if (entities.GetComponent<CompletedBuilding>(entity).Owner != extraction.Player ||
                !entities.HasComponent<WorldTransform>(entity) ||
                !entities.TryGetComponent(entity, out HealthState health) || health.IsDepleted) continue;
            sources.Add(entity);
            if (sources.Count == 32) break;
        }
        bool Affordable(EntityId source)
        {
            var inventory = entities.GetComponent<InventoryStorage>(source).InventoryId;
            return scenario.Inventories.Contains(inventory) &&
                scenario.Inventories.GetAvailableQuantity(inventory, ResourceIds.Steel) >= BridgeCombat.SteelCost &&
                scenario.Inventories.GetAvailableQuantity(inventory, ResourceIds.Electronics) >= BridgeCombat.ElectronicsCost &&
                Distance(source) <= BridgeCombat.MaterialRange * BridgeCombat.MaterialRange;
        }
        EntityId chosen = sources.OrderByDescending(Affordable).ThenBy(Distance).ThenBy(e => e).FirstOrDefault();
        var rows = new List<BridgeRepairActionReadModel>(8);
        var bridges = new List<EntityId>(32);
        var faction = new FactionId(checked((uint)extraction.Player.Value));
        foreach (EntityId entity in entities.Query<BridgeCombat, WorldTransform>())
            if (scenario.Intelligence.IsEntityCurrentlyIdentified(faction, entity)) bridges.Add(entity);
        foreach (EntityId bridge in bridges.OrderBy(Distance).ThenBy(e => e).Take(8))
        {
            var combat = entities.GetComponent<BridgeCombat>(bridge);
            var infrastructure = entities.GetComponent<StrategicInfrastructure>(bridge);
            var state = entities.GetComponent<StrategicInfrastructureState>(bridge);
            var health = entities.GetComponent<HealthState>(bridge);
            bool paid = entities.TryGetComponent(bridge, out BridgeRepairWork work);
            EntityId source = paid && work.Owner == extraction.Player ? work.MaterialSource : chosen;
            double steel = 0, electronics = 0;
            if (entities.TryGetComponent(source, out CompletedBuilding sourceBuilding) && sourceBuilding.Owner == extraction.Player &&
                entities.TryGetComponent(source, out InventoryStorage storage) && scenario.Inventories.Contains(storage.InventoryId))
            {
                steel = scenario.Inventories.GetAvailableQuantity(storage.InventoryId, ResourceIds.Steel);
                electronics = scenario.Inventories.GetAvailableQuantity(storage.InventoryId, ResourceIds.Electronics);
            }
            var failure = scenario.Services.StrategicInfrastructure.EvaluateBridgeRepair(
                context, extraction.Player, engineer, bridge, source, combat.Revision);
            string status = state.State.ToString().ToUpperInvariant();
            if (paid && work.Block != BridgeRepairFailure.None) status = "PAUSED";
            rows.Add(new(engineer, bridge, source, combat.Revision, infrastructure.Key, status,
                health.Current, health.Maximum, state.RestorationProgressTicks, infrastructure.RestorationTicks,
                paid, steel, electronics, failure));
        }
        return Array.AsReadOnly(rows.ToArray());
    }
}
