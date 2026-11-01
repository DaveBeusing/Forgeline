using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;

namespace ForgeLine.Game;

/// <summary>Actual cargo accounting for a deliberate, authorized conveyor demolition.</summary>
public readonly record struct ConveyorCargoRecovery(
    ResourceId ResourceId, double Held, double Recovered, double Lost);

/// <summary>
/// One bounded, owner-scoped recovery inventory per participant. It is authoritative
/// ECS/inventory state, not an untracked economy balance or a presentation cache.
/// </summary>
public readonly record struct ConveyorSalvageStock(PlayerId Owner, InventoryId Inventory)
{
    public const double Capacity = 4096;

    public static bool CanAccept(EntityRegistry entities, InventoryStore inventories,
        PlayerId owner, IReadOnlyList<ConveyorCargoRecovery> cargo)
    {
        double total = 0;
        foreach (ConveyorCargoRecovery item in cargo)
        {
            if (!ConveyorSimulationSystem.Eligible(item.ResourceId) ||
                !double.IsFinite(item.Recovered) || item.Recovered < 0)
                return false;
            total += item.Recovered;
        }
        if (!double.IsFinite(total)) return false;
        if (!TryFind(entities, owner, out ConveyorSalvageStock state))
            return total <= Capacity;

        if (!inventories.Contains(state.Inventory) ||
            inventories.GetRemainingCapacity(state.Inventory) < total)
            return false;

        foreach (ConveyorCargoRecovery item in cargo)
            if (inventories.GetAddableQuantity(state.Inventory, item.ResourceId,
                    item.Recovered) < item.Recovered)
                return false;
        return true;
    }

    /// <summary>
    /// Preflight must succeed before any cargo movement. Every credit is a real
    /// InventoryStore transfer from the doomed buffer; remaining cargo is lost when
    /// that buffer is destroyed by its existing lifecycle owner.
    /// </summary>
    public static bool Deposit(EntityRegistry entities, InventoryStore inventories,
        PlayerId owner, InventoryId source, IReadOnlyList<ConveyorCargoRecovery> cargo)
    {
        if (!inventories.Contains(source) || !CanAccept(entities, inventories, owner, cargo))
            return false;
        if (cargo.Count == 0) return true;

        if (!TryFind(entities, owner, out ConveyorSalvageStock state))
        {
            InventoryId inventory = inventories.CreateInventory(
                new InventorySpecification(Capacity, ConveyorSimulationSystem.EligibleResources));
            EntityId entity = entities.CreateEntity();
            state = new ConveyorSalvageStock(owner, inventory);
            entities.AddComponent(entity, state);
            entities.AddComponent(entity, new InventoryStorage(inventory));
        }

        foreach (ConveyorCargoRecovery item in cargo)
        {
            if (item.Recovered <= 0) continue;
            InventoryOperationResult transfer = inventories.Transfer(
                source, state.Inventory, item.ResourceId, item.Recovered);
            EngineInvariant.Require(transfer.Succeeded, DiagnosticCategory.Simulation,
                "CONVEYOR_SALVAGE_TRANSFER_FAILED",
                "A preflighted conveyor salvage transfer must not fail.");
        }
        return true;
    }

    /// <summary>Reclaim queued material through legal owned inventories in stable order.</summary>
    public static void Drain(EntityRegistry entities, InventoryStore inventories)
    {
        foreach (EntityId entity in entities.Query<ConveyorSalvageStock>(QueryIterationOrder.StableByEntityIndex))
        {
            ConveyorSalvageStock salvage = entities.GetComponent<ConveyorSalvageStock>(entity);
            if (!inventories.Contains(salvage.Inventory)) continue;
            foreach (InventoryResourceQuantity item in inventories.GetResourceQuantities(salvage.Inventory))
            {
                double remaining = item.AvailableQuantity;
                if (remaining <= 0) continue;
                // Command Cores first, then storage depots. Never transfer to
                // another player's inventories or industrial input buffers.
                for (int priority = 0; priority < 2 && remaining > 0; priority++)
                {
                    foreach (EntityId candidate in entities.Query<CompletedBuilding, InventoryStorage>(
                                 QueryIterationOrder.StableByEntityIndex))
                    {
                        CompletedBuilding building = entities.GetComponent<CompletedBuilding>(candidate);
                        if (building.Owner != salvage.Owner ||
                            (priority == 0 ? building.BuildingId != BuildingIds.CommandCore :
                                building.BuildingId != BuildingIds.StorageDepot) ||
                            entities.TryGetComponent(candidate, out HealthState health) && health.IsDepleted)
                            continue;
                        InventoryId destination = entities.GetComponent<InventoryStorage>(candidate).InventoryId;
                        if (destination == salvage.Inventory || !inventories.Contains(destination))
                            continue;
                        double accepted = inventories.GetAddableQuantity(
                            destination, item.ResourceId, remaining);
                        if (accepted <= 0) continue;
                        InventoryOperationResult result = inventories.Transfer(
                            salvage.Inventory, destination, item.ResourceId, accepted);
                        EngineInvariant.Require(result.Succeeded, DiagnosticCategory.Simulation,
                            "CONVEYOR_SALVAGE_RECOVERY_FAILED",
                            "An accepted conveyor salvage inventory transfer must succeed.");
                        remaining -= accepted;
                        if (remaining <= 0) break;
                    }
                }
            }
        }
    }

    public static InventoryId FindInventory(EntityRegistry entities, PlayerId owner) =>
        TryFind(entities, owner, out ConveyorSalvageStock stock) ? stock.Inventory : InventoryId.None;

    private static bool TryFind(EntityRegistry entities, PlayerId owner,
        out ConveyorSalvageStock result)
    {
        foreach (EntityId entity in entities.Query<ConveyorSalvageStock>(QueryIterationOrder.StableByEntityIndex))
        {
            ConveyorSalvageStock stock = entities.GetComponent<ConveyorSalvageStock>(entity);
            if (stock.Owner != owner) continue;
            result = stock;
            return true;
        }
        result = default;
        return false;
    }
}
