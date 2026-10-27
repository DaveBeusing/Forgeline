using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Uses current authorized input inventories; changes only the optional journal observation scope.</summary>
internal static class OperationsMeasurementCapture
{
    internal static OperationsDiagnosisSnapshot? Attach(SimulationContext context, PresentationExtractionContext extraction,
        OperationsDiagnosisSnapshot? diagnosis, PlayerActionSnapshot? actions, RenderInstance[] instances, bool enabled)
    {
        var scenario = extraction.Scenario;
        var subjects = new List<OperationsWindowSubject>();
        bool distributionObserved = scenario.AutomatedDistribution.LastDebugSnapshot.Tick == context.Tick;
        var visible = new HashSet<EntityId>();
        bool limited = enabled && instances.Length > OperationsCauseSnapshot.MaximumScannedInstances;
        if (enabled)
            for (int i = 0; i < Math.Min(instances.Length, OperationsCauseSnapshot.MaximumScannedInstances); i++)
                if (OperationsWorldOverlay.IsOwnedVisible(instances[i], extraction.Player)) visible.Add(instances[i].Entity);
        if (enabled && diagnosis is not null)
        {
            // Compatible input resources remain observed across completed cycles; no demand is inferred.
            if (actions?.Production is { } production)
                foreach (var recipe in production.Recipes)
                    foreach (var input in recipe.Inputs) Add(production.Entity, input.ResourceId);
            if (actions?.UnitProduction is { } factory)
                foreach (var unit in factory.Units)
                    foreach (var input in unit.Costs) Add(factory.Entity, input.ResourceId);
            foreach (var row in diagnosis.Rows)
                if (row.Subject != OperationsDiagnosisSubject.SourceInventory || !row.Source.IsValid)
                    Add(row.Entity, row.Resource);
        }
        var observation = scenario.Progress.ObserveOperations(extraction.Player, context.Tick, context.TickDuration, subjects);
        if (diagnosis is null) return null;
        if (limited || diagnosis.TotalRows > diagnosis.Rows.Count || diagnosis.TotalLinks > diagnosis.Links.Count || diagnosis.OmittedSteps > 0)
            observation = new(observation.Session, observation.Tick, observation.Player, observation.Measurements, observation.OmittedSubjects, limited: true);
        return new(diagnosis.Session, diagnosis.Tick, diagnosis.Player, diagnosis.Rows, diagnosis.Links,
            diagnosis.TotalRows, diagnosis.Root, diagnosis.OmittedSteps, diagnosis.HasCycle,
            diagnosis.MissingObservation, diagnosis.Branches, diagnosis.TotalLinks, observation, distributionObserved);

        void Add(EntityId entity, ResourceId resource)
        {
            if (!resource.IsSpecified || !visible.Contains(entity) ||
                !context.Entities.TryGetComponent(entity, out ControllableEntity owner) ||
                owner.Owner != extraction.Player || !owner.IsControllable ||
                context.Entities.TryGetComponent(entity, out CompletedBuilding building) && building.Owner != extraction.Player ||
                context.Entities.TryGetComponent(entity, out UnitProductionFacility unitOwner) && unitOwner.Owner != extraction.Player ||
                context.Entities.TryGetComponent(entity, out ConveyorPiece beltOwner) && beltOwner.Owner != extraction.Player ||
                context.Entities.HasComponent<CargoTransport>(entity)) return;
            InventoryId inventory = ConveyorSimulationSystem.ResolveInventory(context.Entities, entity, input: true);
            if (!scenario.Inventories.Contains(inventory) ||
                subjects.Any(x => x.Entity == entity && x.Inventory == inventory && x.Resource == resource)) return;
            if (subjects.Count >= GameplayProgressJournal.MaximumOperationsSubjects + 1) return;
            OperationsDiagnosisReason reason = OperationsDiagnosisReason.Unknown;
            if (context.Entities.TryGetComponent(entity, out ProductionFacility processor))
                reason = OperationsChainCapture.ProductionReason(processor.BlockReason);
            else if (context.Entities.TryGetComponent(entity, out UnitProductionFacility units))
                reason = units.BlockReason switch
                {
                    UnitProductionBlockReason.None => OperationsDiagnosisReason.None,
                    UnitProductionBlockReason.NoPower => OperationsDiagnosisReason.NoPower,
                    UnitProductionBlockReason.NoInput => OperationsDiagnosisReason.NoInput,
                    UnitProductionBlockReason.Paused => OperationsDiagnosisReason.ProductionPaused,
                    _ => OperationsDiagnosisReason.Unknown
                };
            else if (context.Entities.HasComponent<ConveyorFlowState>(entity) &&
                scenario.Services.ConveyorFlow.ObservationTick == context.Tick &&
                scenario.Services.ConveyorFlow.TryGetObservation(entity, out var flow))
                reason = OperationsChainCapture.ConveyorReason(flow.First.Reason);
            var policies = diagnosis!.Rows.Where(x => x.Entity == entity && x.Resource == resource &&
                x.Subject == OperationsDiagnosisSubject.StockPolicy).ToArray();
            double? backlog = distributionObserved && policies.Length == 1 ? policies[0].OutstandingRequestQuantity : null;
            if (reason == OperationsDiagnosisReason.Unknown && distributionObserved && policies.Length == 1) reason = policies[0].PrimaryReason;
            subjects.Add(new(entity, inventory, resource,
                reason == OperationsDiagnosisReason.Unknown ? null : reason != OperationsDiagnosisReason.None, backlog));
        }
    }
}
