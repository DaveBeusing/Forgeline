using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public static class PlayerActionRequestDispatcher
{
    public static bool TryDispatch(
        in PlayerActionRequest request,
        PlayerId player,
        PlayerCommandGateway gateway,
        SimulationTick observedTick,
        out PlayerCommandSubmissionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        if (request.AppendOrders && request.Kind is PlayerActionRequestKind.SubmitAttack or PlayerActionRequestKind.SubmitAttackMove)
        {
            receipt = gateway.SubmitQueuedOrders(player, request.TacticalEntities ?? [],
                request.TacticalWorldTarget, request.TacticalTarget,
                request.Kind == PlayerActionRequestKind.SubmitAttack ? QueuedOrderKind.Attack : QueuedOrderKind.AttackMove,
                QueuedOrderEdit.Append, false, observedTick, request.TacticalFormation);
            return true;
        }
        switch (request.Kind)
        {
            case PlayerActionRequestKind.RepairBridge:
                receipt = gateway.SubmitBridgeRepair(player,
                    request.TacticalEntities is { Length: 1 } ? request.TacticalEntities[0] : EntityId.Invalid,
                    request.TacticalTarget, request.Facility, request.ExpectedInfrastructureRevision, observedTick);
                return true;

            case PlayerActionRequestKind.SetConveyorRouting:
                receipt = gateway.SubmitConveyorRouting(player, request.Facility,
                    request.RoutingPreference, request.ExpectedRoutingRevision, observedTick);
                return true;

            case PlayerActionRequestKind.SubmitOperationsRule:
                receipt = gateway.SubmitOperationsRule(request.RuleOperation,
                    request.RuleDefinition with { Owner = player }, request.ExpectedRuleRevision,
                    request.Enabled, observedTick);
                return true;

            case PlayerActionRequestKind.QueueProduction:
                receipt =
                    gateway.SubmitProduction(
                        player,
                        request.Facility,
                        request.RecipeId,
                        observedTick,
                        request.Priority,
                        request.ProductionMode,
                        request.DesiredStockResourceId,
                        request.DesiredStockQuantity);
                return true;

            case PlayerActionRequestKind.SetProductionPaused:
                receipt =
                    gateway.SubmitProductionPaused(
                        player,
                        request.RequestEntity,
                        request.Paused,
                        observedTick);
                return true;

            case PlayerActionRequestKind.CancelProduction:
                receipt =
                    gateway.SubmitProductionCancel(
                        player,
                        request.RequestEntity,
                        observedTick);
                return true;

            case PlayerActionRequestKind.QueueUnitProduction:
                receipt =
                    gateway.SubmitUnitProduction(
                        player,
                        request.Facility,
                        request.UnitId,
                        observedTick,
                        request.Priority);
                return true;

            case PlayerActionRequestKind.CancelUnitProduction:
                receipt =
                    gateway.SubmitUnitProductionCancel(
                        player,
                        request.RequestEntity,
                        observedTick);
                return true;

            case PlayerActionRequestKind.CreateCargoConvoy:
                receipt = gateway.SubmitCargoConvoy(
                    player, request.ConvoyTrucks ?? [],
                    request.ConvoyEscorts ?? [], observedTick);
                return true;

            case PlayerActionRequestKind.DissolveCargoConvoy:
                receipt = gateway.SubmitCargoConvoyDissolve(
                    player, request.ConvoyGroup, observedTick);
                return true;

            case PlayerActionRequestKind.SetStockPolicy:
                receipt =
                    gateway.SubmitLogisticsStockPolicy(
                        player,
                        request.Facility,
                        request.StockResourceId,
                        request.StockMinimum,
                        request.StockTarget,
                        request.StockMaximum,
                        request.StockPriority,
                        request.Enabled,
                        observedTick);
                return true;

            case PlayerActionRequestKind.RemoveStockPolicy:
                receipt =
                    gateway.SubmitRemoveLogisticsStockPolicy(
                        player,
                        request.RequestEntity,
                        observedTick);
                return true;

            case PlayerActionRequestKind.SetAutomaticResupplyPolicy:
                receipt =
                    gateway.SubmitAutomaticResupplyPolicy(
                        player,
                        request.Facility,
                        request.AutomaticAmmunitionThreshold,
                        request.AutomaticFuelThreshold,
                        request.Enabled,
                        observedTick);
                return true;

            case PlayerActionRequestKind.RequestResupply:
                receipt =
                    gateway.SubmitResupply(
                        player,
                        request.Facility,
                        observedTick);
                return true;

            case PlayerActionRequestKind.SetSupplyPriority:
                receipt =
                    gateway.SubmitSupplyPriority(
                        player,
                        request.Facility,
                        request.SupplyPriority,
                        observedTick);
                return true;

            case PlayerActionRequestKind.SubmitStopCombat:
                receipt =
                    gateway.SubmitStopCombat(
                        player,
                        request.TacticalEntities ?? [],
                        observedTick);
                return true;

            case PlayerActionRequestKind.SetFirePolicy:
                receipt = gateway.SubmitFirePolicy(player, request.TacticalEntities ?? [], request.FirePolicy, observedTick);
                return true;

            case PlayerActionRequestKind.SubmitHoldPosition:
                receipt =
                    gateway.SubmitHoldPosition(
                        player,
                        request.TacticalEntities ?? [],
                        observedTick);
                return true;

            case PlayerActionRequestKind.CancelFireMission:
                receipt =
                    gateway.SubmitCancelFireMission(
                        player,
                        request.TacticalEntities ?? [],
                        observedTick);
                return true;

            case PlayerActionRequestKind.SubmitAttack:
                receipt =
                    gateway.SubmitAttack(
                        player,
                        request.TacticalEntities ?? [],
                        request.TacticalTarget,
                        observedTick);
                return true;

            case PlayerActionRequestKind.SubmitAttackMove:
                receipt =
                    gateway.SubmitAttackMove(
                        player,
                        request.TacticalEntities ?? [],
                        request.TacticalWorldTarget,
                        observedTick,
                        request.TacticalFormation);
                return true;

            case PlayerActionRequestKind.SubmitRetreat:
                receipt =
                    gateway.SubmitRetreat(
                        player,
                        request.TacticalEntities ?? [],
                        request.TacticalWorldTarget,
                        observedTick,
                        request.TacticalFormation);
                return true;

            case PlayerActionRequestKind.SubmitRetreatToRecovery:
                receipt =
                    gateway.SubmitRetreatToRecovery(
                        player,
                        request.TacticalEntities ?? [],
                        observedTick,
                        request.TacticalFormation);
                return true;

            case PlayerActionRequestKind.SubmitFireMissionCoordinate:
                receipt =
                    gateway.SubmitFireMission(
                        player,
                        request.TacticalEntities ?? [],
                        request.TacticalWorldTarget,
                        request.TacticalRounds,
                        observedTick);
                return true;

            case PlayerActionRequestKind.SubmitFireMissionContact:
                receipt =
                    gateway.SubmitFireMission(
                        player,
                        request.TacticalEntities ?? [],
                        request.TacticalContactKey,
                        request.TacticalRounds,
                        observedTick);
                return true;

            case PlayerActionRequestKind.StartTechnologyResearch:
                receipt =
                    gateway.SubmitTechnologyResearch(
                        player,
                        request.TechnologyId,
                        request.Facility,
                        request.TechnologySourceInventory,
                        observedTick);
                return true;

            case PlayerActionRequestKind.CancelTechnologyResearch:
                receipt =
                    gateway.SubmitTechnologyResearchCancel(
                        player,
                        request.RequestEntity,
                        observedTick);
                return true;

            case PlayerActionRequestKind.Surrender:
                receipt =
                    gateway.SubmitSurrender(
                        player,
                        observedTick);
                return true;

            default:
                receipt = default;
                return false;
        }
    }
}
