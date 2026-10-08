using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PlayerCommandBoundaryTests
{
    [Fact]
    public void MovementResultCarriesSessionCorrelationAndExecutionTick()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4101);
        var gateway =
            CreateGateway(scenario);

        EntityId unit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitMovement(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                new Vector3(32.0f, 0.0f, 32.0f),
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact);

        Assert.True(receipt.Accepted);
        Assert.True(receipt.CorrelationId.IsSpecified);
        Assert.Equal(
            scenario.Simulation.SessionId,
            receipt.SessionId);
        Assert.Equal(
            scenario.Simulation.CurrentTick.Next(),
            receipt.TargetTick);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            receipt.CorrelationId,
            result.CorrelationId);
        Assert.Equal(
            receipt.SessionId,
            result.SessionId);
        Assert.Equal(
            PlayerCommandKind.Movement,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);
        Assert.Equal(1, result.AcceptedTargets);
        Assert.Equal(0, result.RejectedTargets);
        Assert.Equal(
            scenario.Simulation.CurrentTick,
            result.ResolvedAtTick);
    }

    [Fact]
    public void ResultDeliveryIsOrderedAndSurvivesDelayedConsumption()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4102);
        var gateway =
            CreateGateway(
                scenario,
                maximumOutstanding: 4);
        EntityId unit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        PlayerCommandSubmissionReceipt first =
            gateway.SubmitMovement(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                new Vector3(24.0f, 0.0f, 24.0f),
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact);
        PlayerCommandSubmissionReceipt second =
            gateway.SubmitMovement(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                new Vector3(48.0f, 0.0f, 48.0f),
                scenario.Simulation.CurrentTick,
                FormationTemplate.Compact);

        scenario.Simulation.AdvanceOneTick();
        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(2, gateway.Results.Count);

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel firstResult));
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel secondResult));

        Assert.Equal(
            first.CorrelationId,
            firstResult.CorrelationId);
        Assert.Equal(
            second.CorrelationId,
            secondResult.CorrelationId);
        Assert.True(
            firstResult.ResolvedAtTick <=
            secondResult.ResolvedAtTick);
    }

    [Fact]
    public void FullBoundaryRejectsNewSubmissionWithoutDroppingExistingResults()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4103);
        var gateway =
            CreateGateway(
                scenario,
                maximumOutstanding: 2);
        EntityId unit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        PlayerCommandSubmissionReceipt first =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                16.0f);
        PlayerCommandSubmissionReceipt second =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                32.0f);
        PlayerCommandSubmissionReceipt rejected =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                48.0f);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
        Assert.False(rejected.Accepted);
        Assert.Equal(
            PlayerCommandSubmissionFailure.BoundaryFull,
            rejected.Failure);

        scenario.Simulation.AdvanceOneTick();

        Assert.Equal(2, gateway.Results.Count);
        Assert.True(
            gateway.Results.TryRead(out _));

        PlayerCommandSubmissionReceipt resumed =
            SubmitMovement(
                gateway,
                scenario,
                unit,
                64.0f);

        Assert.True(resumed.Accepted);
    }

    [Fact]
    public void FreshRuntimeUsesDifferentSessionIdentity()
    {
        using MatchRuntime first =
            CreateHumanScenario(seed: 4104);
        using MatchRuntime second =
            CreateHumanScenario(seed: 4104);

        Assert.True(
            first.Simulation.SessionId.IsSpecified);
        Assert.True(
            second.Simulation.SessionId.IsSpecified);
        Assert.NotEqual(
            first.Simulation.SessionId,
            second.Simulation.SessionId);
    }

    [Fact]
    public void PendingResultRemainsBoundToOriginalSessionAcrossRestart()
    {
        using MatchRuntime first =
            CreateHumanScenario(seed: 4105);
        var firstGateway =
            CreateGateway(first);
        EntityId firstUnit =
            first.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        PlayerCommandSubmissionReceipt receipt =
            SubmitMovement(
                firstGateway,
                first,
                firstUnit,
                72.0f);

        first.Simulation.AdvanceOneTick();

        Assert.True(receipt.Accepted);
        Assert.Equal(
            1,
            firstGateway.Results.Count);

        using MatchRuntime restarted =
            CreateHumanScenario(seed: 4105);
        var restartedGateway =
            CreateGateway(restarted);

        Assert.NotEqual(
            firstGateway.SessionId,
            restartedGateway.SessionId);
        Assert.Equal(
            0,
            restartedGateway.OutstandingCount);
        Assert.False(
            restartedGateway.Results.TryRead(out _));

        Assert.True(
            firstGateway.Results.TryRead(
                out PlayerCommandResultReadModel oldResult));
        Assert.Equal(
            firstGateway.SessionId,
            oldResult.SessionId);
        Assert.Equal(
            receipt.CorrelationId,
            oldResult.CorrelationId);
    }

    [Fact]
    public void TerminalEndMatchResultIsDeliveredThroughBoundary()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4106);
        var gateway =
            CreateGateway(scenario);

        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore));
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            scenario.GetMatchState().IsTerminal);

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitEndMatch(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            receipt.CorrelationId,
            result.CorrelationId);
        Assert.Equal(
            PlayerCommandKind.EndMatch,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);
        Assert.Equal(
            scenario.Simulation.SessionId,
            result.SessionId);
        Assert.Equal(
            MatchStatus.Ended,
            scenario.GetMatchState().Status);
    }

    [Fact]
    public void ProductionSubmissionRejectsFacilityOwnedByAnotherPlayer()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4107);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        InventoryId output =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        EntityId facility =
            scenario.Simulation.Entities.CreateEntity();

        scenario.Simulation.Entities.AddComponent(
            facility,
            new ControllableEntity(
                new PlayerId(2),
                ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            facility,
            new ProductionFacility(
                input,
                output,
                ProductionCapability.SteelProcessing,
                scenario.Simulation.CurrentTick));

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitProduction(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                facility,
                RecipeIds.Steel,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandKind.Production,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Rejected,
            result.State);
        Assert.Equal(0, result.AcceptedTargets);
        Assert.Equal(1, result.RejectedTargets);
    }

    [Fact]
    public void UnitProductionSubmissionUsesOwnedFacilityAndPublishesAcceptedResult()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4108);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(4_000.0));
        EntityId facility =
            scenario.Simulation.Entities.CreateEntity();

        scenario.Simulation.Entities.AddComponent(
            facility,
            new ControllableEntity(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ControllableEntityCategory.Building));
        scenario.Simulation.Entities.AddComponent(
            facility,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Infantry,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                scenario.Simulation.CurrentTick));

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitUnitProduction(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                facility,
                UnitIds.RifleSquad,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandKind.UnitProduction,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);
        Assert.Equal(1, result.AcceptedTargets);
        Assert.Equal(0, result.RejectedTargets);
    }

    [Fact]
    public void UnitProductionRallyPointSubmissionEnforcesFacilityOwnership()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4109);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        InventoryId input =
            scenario.Inventories.CreateInventory(
                new InventorySpecification(1_000.0));
        EntityId ownedFacility =
            scenario.Simulation.Entities.CreateEntity();
        EntityId foreignFacility =
            scenario.Simulation.Entities.CreateEntity();

        scenario.Simulation.Entities.AddComponent(
            ownedFacility,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                Vector3.Zero,
                scenario.Simulation.CurrentTick));
        scenario.Simulation.Entities.AddComponent(
            foreignFacility,
            new UnitProductionFacility(
                input,
                UnitProductionCapability.Vehicle,
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).Player,
                Vector3.Zero,
                scenario.Simulation.CurrentTick));

        Vector3 rallyPoint =
            new(240.0f, 0.0f, 180.0f);

        PlayerCommandSubmissionReceipt owned =
            gateway.SubmitUnitProductionRallyPoint(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ownedFacility,
                rallyPoint,
                scenario.Simulation.CurrentTick);

        Assert.True(owned.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel ownedResult));
        Assert.Equal(
            PlayerCommandKind.UnitProduction,
            ownedResult.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            ownedResult.State);
        Assert.Equal(
            rallyPoint,
            scenario.Simulation.Entities
                .GetComponent<UnitProductionRallyPoint>(
                    ownedFacility)
                .WorldPosition);

        PlayerCommandSubmissionReceipt foreign =
            gateway.SubmitUnitProductionRallyPoint(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                foreignFacility,
                rallyPoint,
                scenario.Simulation.CurrentTick);

        Assert.True(foreign.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel foreignResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Rejected,
            foreignResult.State);
        Assert.False(
            scenario.Simulation.Entities
                .HasComponent<UnitProductionRallyPoint>(
                    foreignFacility));
    }

    [Fact]
    public void LogisticsStockPolicySubmissionEnforcesOwnershipAndThresholds()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4111);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);

        PlayerCommandSubmissionReceipt owned =
            gateway.SubmitLogisticsStockPolicy(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                ResourceIds.Fuel,
                50.0,
                100.0,
                150.0,
                LogisticsStockPriority.High,
                enabled: true,
                scenario.Simulation.CurrentTick);

        Assert.True(owned.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel ownedResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            ownedResult.State);

        var matchingPolicies =
            new List<LogisticsStockPolicy>();
        foreach (EntityId entity in
                 scenario.Simulation.Entities.Query<LogisticsStockPolicy>())
        {
            LogisticsStockPolicy candidate =
                scenario.Simulation.Entities
                    .GetComponent<LogisticsStockPolicy>(
                        entity);

            if (candidate.TargetEntity ==
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore &&
                candidate.ResourceId ==
                    ResourceIds.Fuel)
            {
                matchingPolicies.Add(candidate);
            }
        }

        LogisticsStockPolicy policy =
            Assert.Single(matchingPolicies);
        Assert.Equal(100.0, policy.DesiredTarget);

        PlayerCommandSubmissionReceipt foreign =
            gateway.SubmitLogisticsStockPolicy(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore,
                ResourceIds.Fuel,
                10.0,
                20.0,
                30.0,
                LogisticsStockPriority.Normal,
                enabled: true,
                scenario.Simulation.CurrentTick);

        Assert.True(foreign.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel foreignResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Rejected,
            foreignResult.State);
        Assert.Equal(
            PlayerLogisticsActionFailureReason.ForeignOwnership,
            foreignResult.ActionFailure);

        PlayerCommandSubmissionReceipt invalid =
            gateway.SubmitLogisticsStockPolicy(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                ResourceIds.Ammunition,
                30.0,
                20.0,
                40.0,
                LogisticsStockPriority.Normal,
                enabled: true,
                scenario.Simulation.CurrentTick);

        Assert.True(invalid.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel invalidResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Rejected,
            invalidResult.State);
        Assert.Equal(
            PlayerLogisticsActionFailureReason.InvalidThresholds,
            invalidResult.ActionFailure);

        PlayerCommandSubmissionReceipt nonFinite =
            gateway.SubmitLogisticsStockPolicy(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                ResourceIds.Electronics,
                double.NaN,
                20.0,
                40.0,
                LogisticsStockPriority.Normal,
                enabled: true,
                scenario.Simulation.CurrentTick);

        Assert.True(nonFinite.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel nonFiniteResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Rejected,
            nonFiniteResult.State);
        Assert.Equal(
            PlayerLogisticsActionFailureReason.InvalidThresholds,
            nonFiniteResult.ActionFailure);
    }

    [Fact]
    public void AutomaticResupplyPolicySubmissionUpdatesOwnedUnit()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4112);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId unit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitAutomaticResupplyPolicy(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                unit,
                ammunitionThreshold: 0.35,
                fuelThreshold: 0.4,
                enabled: false,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandKind.Supply,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);

        AutomaticResupplyPolicy policy =
            scenario.Simulation.Entities
                .GetComponent<AutomaticResupplyPolicy>(
                    unit);
        Assert.Equal(0.35, policy.AmmunitionThreshold);
        Assert.Equal(0.4, policy.FuelThreshold);
        Assert.False(policy.Enabled);
    }

    [Fact]
    public void SupplyPrioritySubmissionEnforcesOwnership()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4113);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId ownedUnit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId foreignUnit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];

        PlayerCommandSubmissionReceipt owned =
            gateway.SubmitSupplyPriority(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                ownedUnit,
                BattlefieldSupplyPriority.Critical,
                scenario.Simulation.CurrentTick);

        Assert.True(owned.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel ownedResult));
        Assert.Equal(
            PlayerCommandKind.Supply,
            ownedResult.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            ownedResult.State);
        Assert.Equal(
            BattlefieldSupplyPriority.Critical,
            scenario.Simulation.Entities
                .GetComponent<UnitSupplyPriority>(
                    ownedUnit)
                .Priority);

        PlayerCommandSubmissionReceipt foreign =
            gateway.SubmitSupplyPriority(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                foreignUnit,
                BattlefieldSupplyPriority.High,
                scenario.Simulation.CurrentTick);

        Assert.True(foreign.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel foreignResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Rejected,
            foreignResult.State);
        Assert.Equal(
            PlayerLogisticsActionFailureReason.ForeignOwnership,
            foreignResult.ActionFailure);
        Assert.False(
            scenario.Simulation.Entities.TryGetComponent(
                foreignUnit,
                out UnitSupplyPriority foreignPriority) &&
            foreignPriority.Priority ==
                BattlefieldSupplyPriority.High);
    }

    [Fact]
    public void RetreatToRecoveryUsesOwnedCombinedSupportProvider()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4117);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId unit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitRetreatToRecovery(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                scenario.Simulation.CurrentTick,
                FormationTemplate.Column);

        Assert.True(receipt.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandKind.Tactical,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);

        RetreatRecoveryState recovery =
            scenario.Simulation.Entities
                .GetComponent<RetreatRecoveryState>(
                    unit);
        Assert.Equal(
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
            recovery.Provider);
        Assert.Equal(
            RetreatRecoveryReason.RepairAndSupply,
            recovery.Reason);

        CombatOrderState order =
            scenario.Simulation.Entities
                .GetComponent<CombatOrderState>(
                    unit);
        Assert.Equal(
            CombatOrderKind.Retreat,
            order.Kind);
        Assert.Equal(
            recovery.Destination,
            order.Destination);
    }

    [Fact]
    public void TacticalAttackRequiresCurrentIdentifiedEnemy()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4114);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId attacker =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId target =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];
        FactionId westFaction =
            new((uint)scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value);

        IntelligenceSignature signature =
            scenario.Simulation.Entities
                .GetComponent<IntelligenceSignature>(
                    target);
        WorldTransform targetTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    target);

        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            westFaction,
            target,
            signature,
            targetTransform.Position,
            IntelligenceState.Identified,
            scenario.Simulation.CurrentTick);

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitAttack(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [attacker],
                target,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel accepted));
        Assert.Equal(
            PlayerCommandKind.Tactical,
            accepted.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            accepted.State);
        Assert.Equal(
            CombatOrderKind.Attack,
            scenario.Simulation.Entities
                .GetComponent<CombatOrderState>(
                    attacker).Kind);

        PlayerCommandSubmissionReceipt stale =
            gateway.SubmitAttack(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [attacker],
                target,
                scenario.Simulation.CurrentTick);

        Assert.True(stale.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel rejected));
        Assert.Equal(
            PlayerCommandFeedbackState.Rejected,
            rejected.State);
        Assert.Equal(
            PlayerTacticalActionFailureReason.TargetNotIdentified,
            rejected.TacticalFailure);
    }

    [Fact]
    public void TacticalAttackPublishesPartialResultForMixedSelection()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4115);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId attacker =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId cargo =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[1];
        EntityId foreign =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[1];
        EntityId target =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];
        FactionId westFaction =
            new((uint)scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value);

        IntelligenceSignature signature =
            scenario.Simulation.Entities
                .GetComponent<IntelligenceSignature>(
                    target);
        WorldTransform transform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    target);
        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            westFaction,
            target,
            signature,
            transform.Position,
            IntelligenceState.Identified,
            scenario.Simulation.CurrentTick);

        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitAttack(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [attacker, cargo, foreign],
                target,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandFeedbackState.Partial,
            result.State);
        Assert.Equal(1, result.AcceptedTargets);
        Assert.Equal(2, result.RejectedTargets);
    }

    [Fact]
    public void TacticalMovementCommandsPreserveDistinctOrderSemantics()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4116);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId unit =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        WorldTransform transform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    unit);
        Vector3 first =
            transform.Position +
            new Vector3(20.0f, 0.0f, 0.0f);
        Vector3 second =
            transform.Position +
            new Vector3(0.0f, 0.0f, 20.0f);

        Assert.True(
            gateway.SubmitAttackMove(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                first,
                scenario.Simulation.CurrentTick,
                FormationTemplate.Line).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));

        CombatOrderState order =
            scenario.Simulation.Entities
                .GetComponent<CombatOrderState>(
                    unit);
        Assert.Equal(
            CombatOrderKind.AttackMove,
            order.Kind);
        Assert.Equal(
            FormationTemplate.Line,
            order.Formation);

        Assert.True(
            gateway.SubmitHoldPosition(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));
        Assert.Equal(
            CombatOrderKind.HoldPosition,
            scenario.Simulation.Entities
                .GetComponent<CombatOrderState>(
                    unit).Kind);

        Assert.True(
            gateway.SubmitStopCombat(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));
        Assert.Equal(
            CombatOrderKind.Stop,
            scenario.Simulation.Entities
                .GetComponent<CombatOrderState>(
                    unit).Kind);

        Assert.True(
            gateway.SubmitRetreat(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [unit],
                second,
                scenario.Simulation.CurrentTick,
                FormationTemplate.Column).Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(gateway.Results.TryRead(out _));

        order =
            scenario.Simulation.Entities
                .GetComponent<CombatOrderState>(
                    unit);
        Assert.Equal(
            CombatOrderKind.Retreat,
            order.Kind);
        Assert.Equal(
            FormationTemplate.Column,
            order.Formation);
    }

    [Fact]
    public void PlayerFireMissionUsesStoredContactCoordinate()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4117);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId target =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];
        WorldTransform targetTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    target);
        Vector3 storedPosition =
            targetTransform.Position;
        Vector3 artilleryPosition =
            storedPosition +
            new Vector3(-180.0f, 0.0f, 0.0f);

        Assert.True(
            scenario.Terrain.TrySampleHeight(
                artilleryPosition.X,
                artilleryPosition.Z,
                out float artilleryHeight));
        artilleryPosition.Y =
            artilleryHeight;

        EntityId artillery =
            scenario.UnitFactory.Create(
                scenario.Services.UnitDefinitions[
                    UnitIds.MobileArtillery],
                artilleryPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        FactionId westFaction =
            new((uint)scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value);
        IntelligenceSignature signature =
            scenario.Simulation.Entities
                .GetComponent<IntelligenceSignature>(
                    target);

        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            westFaction,
            target,
            signature,
            storedPosition,
            IntelligenceState.Detected,
            scenario.Simulation.CurrentTick);

        scenario.Simulation.Entities.SetComponent(
            target,
            targetTransform with
            {
                Position =
                    storedPosition +
                    new Vector3(60.0f, 0.0f, 0.0f)
            });

        IntelligenceContactKey key =
            IntelligenceContactKey.FromEntity(
                target);
        PlayerCommandSubmissionReceipt receipt =
            gateway.SubmitFireMission(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [artillery],
                key,
                requestedRounds: 1,
                scenario.Simulation.CurrentTick);

        Assert.True(receipt.Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandKind.Artillery,
            result.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);

        FireMissionState mission =
            scenario.Simulation.Entities
                .GetComponent<FireMissionState>(
                    artillery);
        Assert.Equal(
            key,
            mission.ContactKey);
        Assert.Equal(
            storedPosition.X,
            mission.TargetPosition.X,
            precision: 3);
        Assert.Equal(
            storedPosition.Z,
            mission.TargetPosition.Z,
            precision: 3);
        Assert.NotEqual(
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    target).Position.X,
            mission.TargetPosition.X);
    }

    [Fact]
    public void TacticalAttackRejectsDetectedDeadAndIncompatibleTargets()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4120);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        EntityId attacker =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId target =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];
        FactionId faction =
            new((uint)scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value);
        IntelligenceSignature targetSignature =
            scenario.Simulation.Entities
                .GetComponent<IntelligenceSignature>(
                    target);
        WorldTransform targetTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    target);

        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            faction,
            target,
            targetSignature,
            targetTransform.Position,
            IntelligenceState.Detected,
            scenario.Simulation.CurrentTick);

        Assert.True(
            gateway.SubmitAttack(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [attacker],
                target,
                scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel detected));
        Assert.Equal(
            PlayerTacticalActionFailureReason.TargetNotIdentified,
            detected.TacticalFailure);

        IntelligenceSignature coreSignature =
            scenario.Simulation.Entities
                .GetComponent<IntelligenceSignature>(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore);
        WorldTransform coreTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore);
        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            faction,
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore,
            coreSignature,
            coreTransform.Position,
            IntelligenceState.Identified,
            scenario.Simulation.CurrentTick);

        Assert.True(
            gateway.SubmitAttack(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [attacker],
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore,
                scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel incompatible));
        Assert.Equal(
            PlayerTacticalActionFailureReason.UnsupportedTargetClass,
            incompatible.TacticalFailure);

        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            faction,
            target,
            targetSignature,
            targetTransform.Position,
            IntelligenceState.Identified,
            scenario.Simulation.CurrentTick);
        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                target));

        Assert.True(
            gateway.SubmitAttack(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [attacker],
                target,
                scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel dead));
        Assert.Equal(
            PlayerTacticalActionFailureReason.TargetUnavailable,
            dead.TacticalFailure);
    }

    [Fact]
    public void PlayerArtilleryReportsRangeAndUsesNormalNoAmmoState()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4121);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        WorldTransform westCore =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore);
        Vector3 artilleryPosition =
            westCore.Position +
            new Vector3(120.0f, 0.0f, 0.0f);

        Assert.True(
            scenario.Terrain.TrySampleHeight(
                artilleryPosition.X,
                artilleryPosition.Z,
                out float height));
        artilleryPosition.Y = height + 1.4f;

        EntityId artillery =
            scenario.UnitFactory.Create(
                scenario.Services.UnitDefinitions[
                    UnitIds.MobileArtillery],
                artilleryPosition,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player);
        EntityId contactEntity =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];
        IntelligenceSignature signature =
            scenario.Simulation.Entities
                .GetComponent<IntelligenceSignature>(
                    contactEntity);
        FactionId faction =
            new((uint)scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value);
        IntelligenceContactKey key =
            IntelligenceContactKey.FromEntity(
                contactEntity);

        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            faction,
            contactEntity,
            signature,
            artilleryPosition +
                new Vector3(900.0f, 0.0f, 0.0f),
            IntelligenceState.Detected,
            scenario.Simulation.CurrentTick);

        Assert.True(
            gateway.SubmitFireMission(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [artillery],
                key,
                requestedRounds: 1,
                scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel outOfRange));
        Assert.Equal(
            PlayerTacticalActionFailureReason.ArtilleryOutOfRange,
            outOfRange.TacticalFailure);

        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            faction,
            contactEntity,
            signature,
            artilleryPosition +
                new Vector3(180.0f, 0.0f, 0.0f),
            IntelligenceState.Detected,
            scenario.Simulation.CurrentTick);

        PlayerCommandSubmissionReceipt disableAutomaticResupply =
            gateway.SubmitAutomaticResupplyPolicy(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                artillery,
                ammunitionThreshold: 0.2,
                fuelThreshold: 0.2,
                enabled: false,
                scenario.Simulation.CurrentTick);

        Assert.True(disableAutomaticResupply.Accepted);
        scenario.Simulation.AdvanceOneTick();
        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel policyResult));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            policyResult.State);

        AmmunitionState ammunition =
            scenario.Simulation.Entities
                .GetComponent<AmmunitionState>(
                    artillery);
        double quantity =
            scenario.Inventories.GetQuantity(
                ammunition.InventoryId,
                ResourceIds.Ammunition);
        Assert.True(
            scenario.Inventories.Remove(
                ammunition.InventoryId,
                ResourceIds.Ammunition,
                quantity).Succeeded);

        Assert.True(
            gateway.SubmitFireMission(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                [artillery],
                key,
                requestedRounds: 1,
                scenario.Simulation.CurrentTick).Accepted);
        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel accepted));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            accepted.State);

        ArtilleryWeaponDefinition artilleryWeapon =
            scenario.Services.ArtilleryWeapons.GetRequired(
                DirectorateContent.WeaponIds.MobileArtillery);

        for (int tick = 0;
             tick <= artilleryWeapon.AcquisitionTicks + 2 &&
             scenario.Simulation.Entities
                 .GetComponent<FireMissionState>(
                     artillery).Status !=
                 FireMissionStatus.NoAmmo;
             tick++)
        {
            scenario.Simulation.AdvanceOneTick();
        }

        Assert.Equal(
            FireMissionStatus.NoAmmo,
            scenario.Simulation.Entities
                .GetComponent<FireMissionState>(
                    artillery).Status);
    }

    [Fact]
    public void PlayerAttackNaturallyDestroysCommandCoreAndResolvesVictory()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4118);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);
        WorldTransform coreTransform =
            scenario.Simulation.Entities
                .GetComponent<WorldTransform>(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore);
        var attackers =
            new List<EntityId>();

        for (int index = 0;
             index < 2;
             index++)
        {
            Vector3 position =
                coreTransform.Position +
                new Vector3(
                    -140.0f,
                    0.0f,
                    -20.0f + index * 40.0f);

            Assert.True(
                scenario.Terrain.TrySampleHeight(
                    position.X,
                    position.Z,
                    out float height));
            position.Y = height + 1.5f;

            attackers.Add(
                scenario.UnitFactory.Create(
                    scenario.Services.UnitDefinitions[
                        UnitIds.MainBattleTank],
                    position,
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player));
        }

        FactionId westFaction =
            new((uint)scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player.Value);
        IntelligenceSignature coreSignature =
            scenario.Simulation.Entities
                .GetComponent<IntelligenceSignature>(
                    scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore);

        scenario.Intelligence.BeginTick(
            scenario.Simulation.CurrentTick);
        scenario.Intelligence.Observe(
            westFaction,
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore,
            coreSignature,
            coreTransform.Position,
            IntelligenceState.Identified,
            scenario.Simulation.CurrentTick);

        Assert.True(
            gateway.SubmitAttack(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                attackers.ToArray(),
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore,
                scenario.Simulation.CurrentTick).Accepted);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel result));
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            result.State);

        for (int tick = 0;
             tick < 1_500 &&
             !scenario.GetMatchState().IsTerminal;
             tick++)
        {
            scenario.Simulation.AdvanceOneTick();
        }

        MatchState match =
            scenario.GetMatchState();

        Assert.Equal(
            MatchStatus.Victory,
            match.Status);
        Assert.Equal(
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
            match.Winner);
        Assert.False(
            scenario.Simulation.Entities.IsAlive(
                scenario.GetBase(new ForgeLine.Game.PlayerId(2)).CommandCore));
        Assert.True(
            scenario.Services.TacticalCombat.Metrics
                .EngagingUnits >= 0);
    }


    [Fact]
    public void TechnologyResearchStartAndCancelResolveThroughCommandBoundary()
    {
        using MatchRuntime scenario =
            CreateHumanScenario(seed: 4114);
        PlayerCommandGateway gateway =
            CreateGateway(scenario);

        PlayerCommandSubmissionReceipt start =
            gateway.SubmitTechnologyResearch(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                TechnologyIds.IndustrialStandardization,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                scenario.Simulation.CurrentTick);

        Assert.True(start.Accepted);
        Assert.Equal(
            PlayerCommandKind.Technology,
            start.Kind);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel startResult));
        Assert.Equal(
            PlayerCommandKind.Technology,
            startResult.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            startResult.State);
        Assert.True(
            TechnologyStateQueries.TryGetActiveResearch(
                scenario.Simulation.Entities,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                out EntityId requestEntity,
                out _));

        PlayerCommandSubmissionReceipt cancel =
            gateway.SubmitTechnologyResearchCancel(
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                requestEntity,
                scenario.Simulation.CurrentTick);

        Assert.True(cancel.Accepted);

        scenario.Simulation.AdvanceOneTick();

        Assert.True(
            gateway.Results.TryRead(
                out PlayerCommandResultReadModel cancelResult));
        Assert.Equal(
            PlayerCommandKind.Technology,
            cancelResult.Kind);
        Assert.Equal(
            PlayerCommandFeedbackState.Accepted,
            cancelResult.State);
        Assert.False(
            TechnologyStateQueries.TryGetActiveResearch(
                scenario.Simulation.Entities,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                out _,
                out _));
    }

    private static MatchRuntime CreateHumanScenario(
        ulong seed)
    {
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                MatchScenarioProfile.Gameplay,
                seed) with
            {
                Participants =
                    CentralDivideScenario.CreateDefaultParticipants(
                        westComputerControlled: false,
                        eastComputerControlled: false)
            };

        return CentralDivideScenario.Create(
            runtime,
            TestContext.Current.CancellationToken);
    }

    private static PlayerCommandGateway CreateGateway(
        MatchRuntime scenario,
        int maximumOutstanding = 128)
    {
        var gateway =
            new PlayerCommandGateway(
                scenario.Simulation,
                scenario.Services.BuildingCommands,
                scenario.BattlefieldRuntime.MatchStateEntity,
                maximumOutstanding,
                intelligence:
                    scenario.Intelligence,
                weapons:
                    scenario.Services.Weapons,
                artilleryWeapons:
                    scenario.Services.ArtilleryWeapons,
                technologies:
                    scenario.Services.TechnologyDefinitions);
        scenario.Simulation.RegisterTickObserver(
            gateway);
        return gateway;
    }

    private static PlayerCommandSubmissionReceipt SubmitMovement(
        PlayerCommandGateway gateway,
        MatchRuntime scenario,
        EntityId unit,
        float coordinate) =>
        gateway.SubmitMovement(
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
            [unit],
            new Vector3(
                coordinate,
                0.0f,
                coordinate),
            scenario.Simulation.CurrentTick,
            FormationTemplate.Compact);
}
