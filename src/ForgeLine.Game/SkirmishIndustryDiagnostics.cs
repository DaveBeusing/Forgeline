using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed record SkirmishStockDiagnostic(
    string Resource, double Quantity, double Available, double Reserved);

public sealed record SkirmishIndustryDiagnostic(
    string Entity, string Building, string Status, string BlockReason,
    string Recipe, bool InputsReserved, ulong CompletedCycles,
    string PowerState, IReadOnlyList<SkirmishStockDiagnostic> Input,
    IReadOnlyList<SkirmishStockDiagnostic> Output);

public sealed record SkirmishExtractorDiagnostic(
    string Entity, string Resource, bool Enabled, string State,
    string Deposit, double RemainingQuantity, string OutputEntity)
{
    public IReadOnlyList<SkirmishStockDiagnostic> Output { get; init; } = [];
}

public sealed record SkirmishTransportDiagnostic(
    string Entity, bool IsSupplyTruck, string Position,
    double MovementFuel, double FuelCapacity, double FuelPerMeter,
    string MovementStatus, string MovementTarget, string NavigationFailure,
    string ResupplyProvider, CargoTransportOrder? Order,
    CargoTransportRuntimeState State, CargoTransportReservation? Reservation,
    IReadOnlyList<SkirmishStockDiagnostic> Cargo)
{
    public SkirmishTransportMotionDiagnostic? Motion { get; init; }
}

public sealed record SkirmishEconomyDiagnostic(
    double Generation, double Demand, int OfflineConsumers,
    int OmittedIndustryDetails, int OmittedExtractorDetails,
    IReadOnlyList<SkirmishIndustryDiagnostic> Industry,
    IReadOnlyList<SkirmishExtractorDiagnostic> Extractors)
{
    public int OmittedTransportDetails { get; init; }

    public IReadOnlyList<SkirmishTransportDiagnostic> Transports { get; init; } = [];

    public SkirmishSupplyDiagnosticSnapshot Supply { get; init; } = SkirmishSupplyDiagnosticSnapshot.Empty;
}

internal static class SkirmishIndustryDiagnostics
{
    private const int MaximumDetails = 32;
    private static readonly ResourceId[] Resources =
    [
        ResourceIds.FerrousOre, ResourceIds.Volatiles, ResourceIds.Silicates,
        ResourceIds.Steel, ResourceIds.Fuel, ResourceIds.Electronics, ResourceIds.Ammunition
    ];

    public static SkirmishEconomyDiagnostic Capture(
        SimulationContext context, InventoryStore inventories, PlayerId owner)
    {
        var industry = new List<SkirmishIndustryDiagnostic>();
        var extractors = new List<SkirmishExtractorDiagnostic>();
        int industryCount = 0;
        int extractorCount = 0;
        int offline = 0;
        double generation = 0.0;
        double demand = 0.0;

        foreach (EntityId entity in context.Entities.Query<CompletedBuilding>(
                     Ecs.QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building = context.Entities.GetComponent<CompletedBuilding>(entity);
            if (building.Owner != owner)
            {
                continue;
            }

            string powerState = "NotRequired";
            if (context.Entities.TryGetComponent(entity, out PowerConsumer consumer) && consumer.Enabled)
            {
                powerState = consumer.State.ToString();
                demand += consumer.Demand;
                offline += consumer.State == PowerOperationalState.Powered ? 0 : 1;
            }

            if (context.Entities.TryGetComponent(entity, out PowerGenerator generator) &&
                generator.Enabled && generator.State == PowerGeneratorState.Generating)
            {
                generation += generator.MaximumGeneration;
            }

            if (context.Entities.TryGetComponent(entity, out ProductionFacility facility))
            {
                industryCount++;
                if (industry.Count < MaximumDetails)
                {
                    industry.Add(new SkirmishIndustryDiagnostic(
                        entity.ToString(), building.BuildingId.ToString(), facility.Status.ToString(),
                        facility.BlockReason.ToString(), facility.ActiveRecipe.ToString(),
                        facility.InputsReserved, facility.CompletedCycles, powerState,
                        CaptureStock(inventories, facility.InputInventory),
                        CaptureStock(inventories, facility.OutputInventory)));
                }
            }

            if (context.Entities.TryGetComponent(entity, out ResourceExtractor extractor))
            {
                extractorCount++;
                if (extractors.Count < MaximumDetails)
                {
                    double remaining = context.Entities.TryGetComponent(extractor.Deposit, out ResourceDeposit deposit)
                        ? deposit.RemainingQuantity : 0.0;
                    extractors.Add(new SkirmishExtractorDiagnostic(
                        entity.ToString(), extractor.ResourceId.ToString(), extractor.Enabled,
                        extractor.State.ToString(), extractor.Deposit.ToString(), remaining,
                        extractor.OutputInventory.ToString())
                    {
                        Output = context.Entities.TryGetComponent(extractor.OutputInventory, out InventoryStorage storage)
                            ? CaptureStock(inventories, storage.InventoryId) : []
                    });
                }
            }
        }

        var transports = new List<SkirmishTransportDiagnostic>();
        int transportCount = 0;
        foreach (EntityId entity in context.Entities.Query<CargoTransport>(Ecs.QueryIterationOrder.StableByEntityIndex))
        {
            CargoTransport transport = context.Entities.GetComponent<CargoTransport>(entity);
            if (transport.Owner != owner)
            {
                continue;
            }

            transportCount++;
            if (transports.Count == MaximumDetails)
            {
                continue;
            }

            bool hasFuel = context.Entities.TryGetComponent(entity, out UnitFuelState fuel);
            double available = hasFuel && inventories.Contains(fuel.InventoryId)
                ? inventories.GetAvailableQuantity(fuel.InventoryId, ResourceIds.Fuel) : 0.0;
            transports.Add(new SkirmishTransportDiagnostic(
                entity.ToString(), context.Entities.HasComponent<SupplyTruck>(entity),
                context.Entities.TryGetComponent(entity, out WorldTransform transform) ? transform.Position.ToString() : "None",
                available, hasFuel ? fuel.Capacity : 0.0, hasFuel ? fuel.ConsumptionPerMeter : 0.0,
                context.Entities.TryGetComponent(entity, out GroundMovementState movement) ? movement.Status.ToString() : "None",
                TacticalCommandUtilities.TryGetMovementIntent(context, entity, out MovementOrder intent)
                    ? intent.WorldTarget.ToString() : "None",
                context.Entities.TryGetComponent(entity, out NavigationFailureState failure)
                    ? failure.FailureReason.ToString() : "None",
                context.Entities.TryGetComponent(entity, out ResupplyOrder resupply) ? resupply.Provider.ToString() : "None",
                context.Entities.TryGetComponent(entity, out CargoTransportOrder order) ? order : null,
                context.Entities.TryGetComponent(entity, out CargoTransportRuntimeState state)
                    ? state : CargoTransportRuntimeState.Idle,
                context.Entities.TryGetComponent(entity, out CargoTransportReservation reservation) ? reservation : null,
                CaptureStock(inventories, transport.CargoInventory))
            {
                Motion = SkirmishTransportMotionDiagnostic.Capture(context, entity)
            });
        }

        return new SkirmishEconomyDiagnostic(
            generation, demand, offline, Math.Max(0, industryCount - industry.Count),
            Math.Max(0, extractorCount - extractors.Count), industry, extractors)
        {
            OmittedTransportDetails = Math.Max(0, transportCount - transports.Count),
            Transports = transports,
            Supply = SkirmishSupplyDiagnostics.Capture(context, owner)
        };
    }

    private static List<SkirmishStockDiagnostic> CaptureStock(
        InventoryStore inventories, InventoryId inventory)
    {
        var stocks = new List<SkirmishStockDiagnostic>();
        if (!inventories.Contains(inventory))
        {
            return stocks;
        }

        foreach (ResourceId resource in Resources)
        {
            double quantity = inventories.GetQuantity(inventory, resource);
            double reserved = inventories.GetReservedQuantity(inventory, resource);
            if (quantity > 0.0 || reserved > 0.0)
            {
                stocks.Add(new SkirmishStockDiagnostic(
                    resource.ToString(), quantity,
                    inventories.GetAvailableQuantity(inventory, resource), reserved));
            }
        }

        return stocks;
    }
}
