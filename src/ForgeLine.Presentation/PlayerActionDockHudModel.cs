using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;

namespace ForgeLine.Presentation;

internal readonly record struct PlayerActionDockItemState(
    bool CanActivate,
    string DisabledReason)
{
    public static PlayerActionDockItemState Enabled =>
        new(
            true,
            string.Empty);

    public static PlayerActionDockItemState Disabled(
        string reason) =>
        new(
            false,
            reason);
}

internal static class PlayerActionDockHudModel
{
    public static string ResolveModeLabel(
        PlayerActionPanelMode mode) =>
        mode switch
        {
            PlayerActionPanelMode.Construction =>
                "BUILD",
            PlayerActionPanelMode.Production =>
                "PROCESS",
            PlayerActionPanelMode.UnitProduction =>
                "UNITS",
            PlayerActionPanelMode.Logistics =>
                "LOGISTICS",
            PlayerActionPanelMode.Supply =>
                "SUPPLY",
            PlayerActionPanelMode.Tactical =>
                "COMBAT",
            _ =>
                "ACTIONS"
        };

    public static string ResolveModeShortcut(
        PlayerActionPanelMode mode) =>
        mode switch
        {
            PlayerActionPanelMode.Construction =>
                "B",
            PlayerActionPanelMode.Production =>
                "P",
            PlayerActionPanelMode.UnitProduction =>
                "U",
            PlayerActionPanelMode.Logistics =>
                "L",
            PlayerActionPanelMode.Supply =>
                "Y",
            PlayerActionPanelMode.Tactical =>
                "K",
            _ =>
                string.Empty
        };

    public static RtsUiIcon ResolveModeIcon(
        PlayerActionPanelMode mode) =>
        mode switch
        {
            PlayerActionPanelMode.Construction =>
                RtsUiIcon.CommandBuild,
            PlayerActionPanelMode.Production =>
                RtsUiIcon.BuildingProcessing,
            PlayerActionPanelMode.UnitProduction =>
                RtsUiIcon.BuildingFactory,
            PlayerActionPanelMode.Logistics =>
                RtsUiIcon.UnitLogistics,
            PlayerActionPanelMode.Supply =>
                RtsUiIcon.CommandSupply,
            PlayerActionPanelMode.Tactical =>
                RtsUiIcon.CommandAttack,
            _ =>
                RtsUiIcon.CursorSelect
        };

    public static RtsUiIcon ResolveItemIcon(
        PlayerActionPanelMode mode,
        int index,
        PlayerActionSnapshot? actions)
    {
        if (actions is null ||
            index < 0)
        {
            return RtsUiIcon.CursorInvalid;
        }

        switch (mode)
        {
            case PlayerActionPanelMode.Construction:
                return index <
                    actions.Construction.Count
                    ? RtsUiIconCatalog.ResolveBuildingRole(
                        actions.Construction[index].BuildingId)
                    : RtsUiIcon.CursorInvalid;

            case PlayerActionPanelMode.Production:
                if (actions.Production is not
                    PlayerProductionFacilityActionReadModel production)
                {
                    return RtsUiIcon.CursorInvalid;
                }

                return index <
                    production.Recipes.Count
                    ? RtsUiIcon.BuildingProcessing
                    : RtsUiIcon.StatusAlert;

            case PlayerActionPanelMode.UnitProduction:
                if (actions.UnitProduction is not
                    PlayerUnitProductionFacilityActionReadModel units)
                {
                    return RtsUiIcon.CursorInvalid;
                }

                if (index < units.Units.Count)
                {
                    return RtsUiIconCatalog.ResolveUnitRole(
                        units.Units[index].UnitId);
                }

                return RtsUiIcon.StatusAlert;

            case PlayerActionPanelMode.Logistics:
                return RtsUiIcon.UnitLogistics;

            case PlayerActionPanelMode.Supply:
                return index switch
                {
                    0 =>
                        RtsUiIcon.StatusFuel,
                    1 =>
                        RtsUiIcon.StatusAmmunition,
                    2 =>
                        RtsUiIcon.CommandSupply,
                    _ =>
                        RtsUiIcon.CursorInvalid
                };

            case PlayerActionPanelMode.Tactical:
                return index switch
                {
                    0 =>
                        RtsUiIcon.CommandAttack,
                    1 =>
                        RtsUiIcon.CommandAttackMove,
                    2 =>
                        RtsUiIcon.CommandStop,
                    3 =>
                        RtsUiIcon.CommandHold,
                    4 =>
                        RtsUiIcon.CommandMove,
                    5 =>
                        RtsUiIcon.UnitArtillery,
                    6 =>
                        RtsUiIcon.CommandCancel,
                    7 =>
                        RtsUiIcon.CommandSupply,
                    _ =>
                        RtsUiIcon.CursorInvalid
                };

            default:
                return RtsUiIcon.CursorInvalid;
        }
    }

    public static string ResolveItemTitle(
        PlayerActionPanelMode mode,
        int index,
        PlayerActionSnapshot? actions)
    {
        if (actions is null ||
            index < 0)
        {
            return string.Empty;
        }

        switch (mode)
        {
            case PlayerActionPanelMode.Construction:
                return index <
                    actions.Construction.Count
                    ? actions.Construction[index].DisplayName
                    : string.Empty;

            case PlayerActionPanelMode.Production:
                if (actions.Production is not
                    PlayerProductionFacilityActionReadModel production)
                {
                    return string.Empty;
                }

                if (index <
                    production.Recipes.Count)
                {
                    return production.Recipes[index].DisplayName;
                }

                int productionRequestIndex =
                    index -
                    production.Recipes.Count;

                return productionRequestIndex >= 0 &&
                       productionRequestIndex <
                           production.Requests.Count
                    ? production.Requests[
                        productionRequestIndex].DisplayName
                    : string.Empty;

            case PlayerActionPanelMode.UnitProduction:
                if (actions.UnitProduction is not
                    PlayerUnitProductionFacilityActionReadModel units)
                {
                    return string.Empty;
                }

                if (index <
                    units.Units.Count)
                {
                    return units.Units[index].DisplayName;
                }

                int unitRequestIndex =
                    index -
                    units.Units.Count;

                return unitRequestIndex >= 0 &&
                       unitRequestIndex <
                           units.Requests.Count
                    ? units.Requests[
                        unitRequestIndex].DisplayName
                    : string.Empty;

            case PlayerActionPanelMode.Logistics:
                return actions.Logistics is
                           PlayerLogisticsActionReadModel logistics &&
                       index <
                           logistics.Policies.Count
                    ? logistics.Policies[index].DisplayName
                    : string.Empty;

            case PlayerActionPanelMode.Supply:
                return index switch
                {
                    0 =>
                        "FUEL THRESHOLD",
                    1 =>
                        "AMMO THRESHOLD",
                    2 =>
                        "REQUEST RESUPPLY",
                    _ =>
                        string.Empty
                };

            case PlayerActionPanelMode.Tactical:
                return index switch
                {
                    0 =>
                        "ATTACK",
                    1 =>
                        "ATTACK MOVE",
                    2 =>
                        "STOP",
                    3 =>
                        "HOLD",
                    4 =>
                        "RETREAT",
                    5 =>
                        "FIRE MISSION",
                    6 =>
                        "CANCEL FIRE",
                    7 =>
                        "RECOVERY",
                    _ =>
                        string.Empty
                };

            default:
                return string.Empty;
        }
    }

    public static PlayerActionDockItemState ResolveItemState(
        PlayerActionPanelMode mode,
        int index,
        PlayerActionSnapshot? actions)
    {
        if (actions is null ||
            index < 0)
        {
            return PlayerActionDockItemState.Disabled(
                "NO DATA");
        }

        switch (mode)
        {
            case PlayerActionPanelMode.Construction:
                if (index >=
                    actions.Construction.Count)
                {
                    return PlayerActionDockItemState.Disabled(
                        "UNAVAILABLE");
                }

                return actions.Construction[index]
                    .HasRequiredResources
                    ? PlayerActionDockItemState.Enabled
                    : PlayerActionDockItemState.Disabled(
                        "MISSING MATERIALS");

            case PlayerActionPanelMode.Production:
                if (actions.Production is not
                    PlayerProductionFacilityActionReadModel production)
                {
                    return PlayerActionDockItemState.Disabled(
                        "SELECT PROCESSOR");
                }

                return index <
                       production.Recipes.Count +
                       production.Requests.Count
                    ? PlayerActionDockItemState.Enabled
                    : PlayerActionDockItemState.Disabled(
                        "UNAVAILABLE");

            case PlayerActionPanelMode.UnitProduction:
                if (actions.UnitProduction is not
                    PlayerUnitProductionFacilityActionReadModel units)
                {
                    return PlayerActionDockItemState.Disabled(
                        "SELECT FACTORY");
                }

                if (index <
                    units.Units.Count)
                {
                    return PlayerActionDockItemState.Enabled;
                }

                return index <
                       units.Units.Count +
                       units.Requests.Count
                    ? PlayerActionDockItemState.Disabled(
                        "CANCEL ONLY")
                    : PlayerActionDockItemState.Disabled(
                        "UNAVAILABLE");

            case PlayerActionPanelMode.Logistics:
                return actions.Logistics is
                           PlayerLogisticsActionReadModel logistics &&
                       index <
                           logistics.Policies.Count
                    ? PlayerActionDockItemState.Enabled
                    : PlayerActionDockItemState.Disabled(
                        "SELECT LOGISTICS");

            case PlayerActionPanelMode.Supply:
                return actions.Supply.HasValue &&
                       index < 3
                    ? PlayerActionDockItemState.Enabled
                    : PlayerActionDockItemState.Disabled(
                        "SELECT SUPPLY UNIT");

            case PlayerActionPanelMode.Tactical:
                return ResolveTacticalItemState(
                    index,
                    actions.Tactical);

            default:
                return PlayerActionDockItemState.Disabled(
                    "CLOSED");
        }
    }

    public static bool CanCancel(
        PlayerActionPanelMode mode,
        int index,
        PlayerActionSnapshot? actions)
    {
        if (actions is null ||
            index < 0)
        {
            return false;
        }

        if (mode ==
                PlayerActionPanelMode.Production &&
            actions.Production is
                PlayerProductionFacilityActionReadModel production)
        {
            int requestIndex =
                index -
                production.Recipes.Count;

            return requestIndex >= 0 &&
                   requestIndex <
                       production.Requests.Count;
        }

        if (mode ==
                PlayerActionPanelMode.UnitProduction &&
            actions.UnitProduction is
                PlayerUnitProductionFacilityActionReadModel units)
        {
            int requestIndex =
                index -
                units.Units.Count;

            return requestIndex >= 0 &&
                   requestIndex <
                       units.Requests.Count;
        }

        return mode ==
                   PlayerActionPanelMode.Logistics &&
               actions.Logistics is
                   PlayerLogisticsActionReadModel logistics &&
               index <
                   logistics.Policies.Count &&
               logistics.Policies[index].HasPolicy;
    }

    public static bool CanCyclePrimary(
        PlayerActionPanelMode mode,
        PlayerActionSnapshot? actions) =>
        mode switch
        {
            PlayerActionPanelMode.Production =>
                actions?.Production is not null,
            PlayerActionPanelMode.UnitProduction =>
                actions?.UnitProduction is not null,
            PlayerActionPanelMode.Logistics =>
                actions?.Logistics is not null,
            PlayerActionPanelMode.Supply =>
                actions?.Supply is not null,
            _ =>
                false
        };

    public static bool CanCycleSecondary(
        PlayerActionPanelMode mode,
        PlayerActionSnapshot? actions) =>
        mode switch
        {
            PlayerActionPanelMode.Production =>
                actions?.Production is not null,
            PlayerActionPanelMode.Logistics =>
                actions?.Logistics is not null,
            PlayerActionPanelMode.Supply =>
                actions?.Supply is not null,
            _ =>
                false
        };

    public static bool CanAdjust(
        PlayerActionPanelMode mode,
        int index,
        in PlayerActionPanelView panel,
        PlayerActionSnapshot? actions)
    {
        if (actions is null ||
            index < 0)
        {
            return false;
        }

        if (mode ==
                PlayerActionPanelMode.Production &&
            panel.ProductionMode ==
                ProductionRequestMode.DesiredStock &&
            actions.Production is
                PlayerProductionFacilityActionReadModel production &&
            index <
                production.Recipes.Count)
        {
            return production.Recipes[index]
                .Outputs.Count >
                0;
        }

        if (mode ==
                PlayerActionPanelMode.Logistics &&
            actions.Logistics is
                PlayerLogisticsActionReadModel logistics)
        {
            return index <
                logistics.Policies.Count;
        }

        return mode ==
                   PlayerActionPanelMode.Supply &&
               actions.Supply.HasValue &&
               index <= 1;
    }

    public static string ResolveFooterLabel(
        PlayerActionDockControlKind control,
        PlayerActionPanelMode mode) =>
        control switch
        {
            PlayerActionDockControlKind.Activate =>
                "ACT",
            PlayerActionDockControlKind.Cancel =>
                "CANCEL",
            PlayerActionDockControlKind.CyclePrimary =>
                mode switch
                {
                    PlayerActionPanelMode.Production or
                    PlayerActionPanelMode.UnitProduction =>
                        "PRIOR",
                    PlayerActionPanelMode.Logistics =>
                        "PRIOR",
                    PlayerActionPanelMode.Supply =>
                        "PRIOR",
                    _ =>
                        string.Empty
                },
            PlayerActionDockControlKind.CycleSecondary =>
                mode switch
                {
                    PlayerActionPanelMode.Production =>
                        "MODE",
                    PlayerActionPanelMode.Logistics =>
                        "FIELD",
                    PlayerActionPanelMode.Supply =>
                        "AUTO",
                    _ =>
                        string.Empty
                },
            PlayerActionDockControlKind.Decrease =>
                "-",
            PlayerActionDockControlKind.Increase =>
                "+",
            _ =>
                string.Empty
        };

    public static string ResolvePlacementFailureLabel(
        BuildingPlacementFailureReason failure) =>
        failure switch
        {
            BuildingPlacementFailureReason.None =>
                "VALID",
            BuildingPlacementFailureReason.UnknownBuilding =>
                "UNKNOWN BUILDING",
            BuildingPlacementFailureReason.TerrainUnavailable =>
                "NO TERRAIN",
            BuildingPlacementFailureReason.OutsideWorldBounds =>
                "OUT OF BOUNDS",
            BuildingPlacementFailureReason.SlopeTooSteep =>
                "SLOPE",
            BuildingPlacementFailureReason.Obstructed =>
                "OBSTRUCTED",
            BuildingPlacementFailureReason.OutsideBuildableArea =>
                "NO BUILD AREA",
            BuildingPlacementFailureReason.ResourceDepositRequired =>
                "DEPOSIT REQUIRED",
            _ =>
                "INVALID"
        };

    public static string ResolveFeedbackStateLabel(
        PlayerCommandFeedbackState state) =>
        state switch
        {
            PlayerCommandFeedbackState.Accepted =>
                "OK",
            PlayerCommandFeedbackState.Partial =>
                "PART",
            PlayerCommandFeedbackState.Rejected =>
                "FAIL",
            _ =>
                "NONE"
        };

    public static string ResolveCommandFeedbackReason(
        in PlayerCommandFeedback feedback)
    {
        if (feedback.PlacementFailure !=
            BuildingPlacementFailureReason.None)
        {
            return ResolvePlacementFailureLabel(
                feedback.PlacementFailure);
        }

        if (feedback.BuildRejection !=
            BuildCommandRejectionReason.None)
        {
            return feedback.BuildRejection switch
            {
                BuildCommandRejectionReason.UnknownBuilding =>
                    "UNKNOWN BUILDING",
                BuildCommandRejectionReason.InvalidSource =>
                    "INVALID SOURCE",
                BuildCommandRejectionReason.SourceOwnerMismatch =>
                    "SOURCE OWNER",
                BuildCommandRejectionReason.InsufficientResources =>
                    "NO MATERIALS",
                BuildCommandRejectionReason.InvalidPlacement =>
                    "PLACEMENT",
                _ =>
                    "BUILD REJECTED"
            };
        }

        if (feedback.ActionFailure !=
            PlayerLogisticsActionFailureReason.None)
        {
            return feedback.ActionFailure switch
            {
                PlayerLogisticsActionFailureReason.InvalidTarget =>
                    "INVALID TARGET",
                PlayerLogisticsActionFailureReason.ForeignTarget =>
                    "FOREIGN TARGET",
                PlayerLogisticsActionFailureReason.UnsupportedTarget =>
                    "UNSUPPORTED",
                PlayerLogisticsActionFailureReason.PolicyNotFound =>
                    "NO POLICY",
                PlayerLogisticsActionFailureReason.InvalidThresholds =>
                    "THRESHOLDS",
                PlayerLogisticsActionFailureReason.ResupplyUnavailable =>
                    "NO RESUPPLY",
                _ =>
                    "ACTION REJECTED"
            };
        }

        if (feedback.TacticalFailure !=
            PlayerTacticalActionFailureReason.None)
        {
            return feedback.TacticalFailure switch
            {
                PlayerTacticalActionFailureReason.NoEligibleUnits =>
                    "NO UNITS",
                PlayerTacticalActionFailureReason.NoTarget =>
                    "NO TARGET",
                PlayerTacticalActionFailureReason.TargetNotIdentified =>
                    "TARGET UNKNOWN",
                PlayerTacticalActionFailureReason.FriendlyTarget =>
                    "FRIENDLY TARGET",
                PlayerTacticalActionFailureReason.TargetNotCombatant =>
                    "NOT TARGETABLE",
                PlayerTacticalActionFailureReason.NoCompatibleWeapon =>
                    "NO WEAPON",
                PlayerTacticalActionFailureReason.NoArtillery =>
                    "NO ARTILLERY",
                PlayerTacticalActionFailureReason.InvalidFireMissionTarget =>
                    "NO ARTY TARGET",
                PlayerTacticalActionFailureReason.OutOfRange =>
                    "OUT OF RANGE",
                PlayerTacticalActionFailureReason.NoRecoveryProvider =>
                    "NO RECOVERY",
                _ =>
                    "TACTICAL REJECTED"
            };
        }

        return string.Empty;
    }

    public static string ResolveProductionStatusLabel(
        ProductionStatus status) =>
        status switch
        {
            ProductionStatus.Idle =>
                "IDLE",
            ProductionStatus.Running =>
                "RUNNING",
            ProductionStatus.NoInput =>
                "NO INPUT",
            ProductionStatus.OutputFull =>
                "OUTPUT FULL",
            ProductionStatus.NoPower =>
                "NO POWER",
            ProductionStatus.Paused =>
                "PAUSED",
            _ =>
                "BLOCKED"
        };

    public static string ResolveUnitProductionStatusLabel(
        UnitProductionStatus status) =>
        status switch
        {
            UnitProductionStatus.Idle =>
                "IDLE",
            UnitProductionStatus.Running =>
                "RUNNING",
            UnitProductionStatus.NoInput =>
                "NO INPUT",
            UnitProductionStatus.NoPower =>
                "NO POWER",
            UnitProductionStatus.Paused =>
                "PAUSED",
            _ =>
                "BLOCKED"
        };

    public static string ResolveDistributionStateLabel(
        PlayerDistributionActionState state) =>
        state switch
        {
            PlayerDistributionActionState.Pending =>
                "PENDING",
            PlayerDistributionActionState.Assigned =>
                "ASSIGNED",
            PlayerDistributionActionState.Loading =>
                "LOADING",
            PlayerDistributionActionState.Traveling =>
                "TRAVELING",
            PlayerDistributionActionState.Unloading =>
                "UNLOADING",
            PlayerDistributionActionState.Waiting =>
                "WAITING",
            PlayerDistributionActionState.Failed =>
                "FAILED",
            _ =>
                "IDLE"
        };

    public static string ResolveCargoStateLabel(
        CargoTransportLifecycleState state) =>
        state switch
        {
            CargoTransportLifecycleState.Idle =>
                "IDLE",
            CargoTransportLifecycleState.ToOrigin =>
                "TO ORIGIN",
            CargoTransportLifecycleState.Loading =>
                "LOADING",
            CargoTransportLifecycleState.ToDestination =>
                "TRAVELING",
            CargoTransportLifecycleState.Unloading =>
                "UNLOADING",
            CargoTransportLifecycleState.Waiting =>
                "WAITING",
            CargoTransportLifecycleState.Failed =>
                "FAILED",
            _ =>
                "CARGO"
        };

    public static string ResolveSupplyProviderStateLabel(
        PlayerSupplyProviderState state) =>
        state switch
        {
            PlayerSupplyProviderState.Available =>
                "AVAILABLE",
            PlayerSupplyProviderState.Assigned =>
                "ASSIGNED",
            PlayerSupplyProviderState.Traveling =>
                "TRAVELING",
            PlayerSupplyProviderState.Transferring =>
                "TRANSFER",
            PlayerSupplyProviderState.Empty =>
                "EMPTY",
            PlayerSupplyProviderState.Blocked =>
                "BLOCKED",
            _ =>
                "NONE"
        };

    public static string ResolveProductionBlockLabel(
        ProductionBlockReason reason) =>
        reason switch
        {
            ProductionBlockReason.None =>
                string.Empty,
            ProductionBlockReason.NoInput =>
                "NO INPUT",
            ProductionBlockReason.OutputFull =>
                "OUTPUT FULL",
            ProductionBlockReason.NoPower =>
                "NO POWER",
            ProductionBlockReason.Paused =>
                "PAUSED",
            ProductionBlockReason.DesiredStockReached =>
                "TARGET REACHED",
            ProductionBlockReason.UnsupportedRecipe =>
                "UNSUPPORTED",
            ProductionBlockReason.InvalidInventory =>
                "INVENTORY",
            _ =>
                "BLOCKED"
        };

    public static string ResolveUnitProductionBlockLabel(
        UnitProductionBlockReason reason) =>
        reason switch
        {
            UnitProductionBlockReason.None =>
                string.Empty,
            UnitProductionBlockReason.NoInput =>
                "NO INPUT",
            UnitProductionBlockReason.NoPower =>
                "NO POWER",
            UnitProductionBlockReason.Paused =>
                "PAUSED",
            UnitProductionBlockReason.UnsupportedUnit =>
                "UNSUPPORTED",
            UnitProductionBlockReason.InvalidFacility =>
                "INVALID FACILITY",
            _ =>
                "BLOCKED"
        };

    public static string ResolveTransportFailureLabel(
        LogisticsTransportRequestFailureReason reason) =>
        reason switch
        {
            LogisticsTransportRequestFailureReason.None =>
                string.Empty,
            LogisticsTransportRequestFailureReason.DestinationUnavailable =>
                "DEST UNAVAILABLE",
            LogisticsTransportRequestFailureReason.NoSourceSurplus =>
                "NO SOURCE STOCK",
            LogisticsTransportRequestFailureReason.NoRoute =>
                "NO ROUTE",
            LogisticsTransportRequestFailureReason.NoTruckAvailable =>
                "NO TRUCK",
            LogisticsTransportRequestFailureReason.ReservationFailed =>
                "RESERVATION",
            LogisticsTransportRequestFailureReason.AssignmentFailed =>
                "ASSIGNMENT",
            LogisticsTransportRequestFailureReason.TransportFailed =>
                "TRANSPORT",
            LogisticsTransportRequestFailureReason.RetryLimitReached =>
                "RETRY LIMIT",
            LogisticsTransportRequestFailureReason.CapacitySaturated =>
                "CAPACITY",
            LogisticsTransportRequestFailureReason.DestinationFull =>
                "DEST FULL",
            _ =>
                "DISTRIBUTION"
        };

    public static string ResolveBottleneckLabel(
        LogisticsBottleneckReason reason) =>
        reason switch
        {
            LogisticsBottleneckReason.None =>
                string.Empty,
            LogisticsBottleneckReason.InsufficientSourceStock =>
                "SOURCE STOCK",
            LogisticsBottleneckReason.InsufficientTruckCapacity =>
                "TRUCK CAPACITY",
            LogisticsBottleneckReason.SaturatedLinkOrHub =>
                "LINK SATURATED",
            LogisticsBottleneckReason.DisconnectedRoute =>
                "NO ROUTE",
            LogisticsBottleneckReason.DestinationFull =>
                "DEST FULL",
            LogisticsBottleneckReason.DestinationUnavailable =>
                "DEST UNAVAILABLE",
            LogisticsBottleneckReason.TransportFailure =>
                "TRANSPORT",
            _ =>
                "BOTTLENECK"
        };

    public static string ResolveCargoWaitLabel(
        CargoTransportWaitReason reason) =>
        reason switch
        {
            CargoTransportWaitReason.None =>
                string.Empty,
            CargoTransportWaitReason.OriginUnavailable =>
                "ORIGIN UNAVAILABLE",
            CargoTransportWaitReason.OriginResourceUnavailable =>
                "SOURCE EMPTY",
            CargoTransportWaitReason.RouteUnavailable =>
                "NO ROUTE",
            CargoTransportWaitReason.RouteInvalidated =>
                "ROUTE INVALID",
            CargoTransportWaitReason.DestinationUnavailable =>
                "DEST UNAVAILABLE",
            CargoTransportWaitReason.DestinationCapacity =>
                "DEST FULL",
            _ =>
                "WAITING"
        };

    public static string ResolveCargoFailureLabel(
        CargoTransportFailureReason reason) =>
        reason switch
        {
            CargoTransportFailureReason.None =>
                string.Empty,
            CargoTransportFailureReason.InvalidTransport =>
                "INVALID TRUCK",
            CargoTransportFailureReason.InvalidOrigin =>
                "INVALID ORIGIN",
            CargoTransportFailureReason.InvalidDestination =>
                "INVALID DEST",
            CargoTransportFailureReason.InvalidRouteAnchor =>
                "ROUTE ANCHOR",
            CargoTransportFailureReason.CargoInventoryUnavailable =>
                "CARGO INVENTORY",
            CargoTransportFailureReason.SourceInventoryUnavailable =>
                "SOURCE INVENTORY",
            CargoTransportFailureReason.DestinationInventoryUnavailable =>
                "DEST INVENTORY",
            CargoTransportFailureReason.CargoCapacityInsufficient =>
                "NO CAPACITY",
            CargoTransportFailureReason.TransferFailed =>
                "TRANSFER",
            CargoTransportFailureReason.NavigationFailed =>
                "NAVIGATION",
            _ =>
                "TRANSPORT"
        };

    public static string ResolvePriorityLabel(
        ProductionPriority priority) =>
        priority switch
        {
            ProductionPriority.High =>
                "HIGH",
            ProductionPriority.Low =>
                "LOW",
            _ =>
                "NORMAL"
        };

    public static string ResolveLogisticsPriorityLabel(
        LogisticsStockPriority priority) =>
        priority switch
        {
            LogisticsStockPriority.Critical =>
                "CRITICAL",
            LogisticsStockPriority.High =>
                "HIGH",
            LogisticsStockPriority.Low =>
                "LOW",
            _ =>
                "NORMAL"
        };

    public static string ResolveSupplyPriorityLabel(
        BattlefieldSupplyPriority priority) =>
        priority switch
        {
            BattlefieldSupplyPriority.Critical =>
                "CRITICAL",
            BattlefieldSupplyPriority.High =>
                "HIGH",
            BattlefieldSupplyPriority.Low =>
                "LOW",
            _ =>
                "NORMAL"
        };

    public static string ResolveProductionModeLabel(
        ProductionRequestMode mode) =>
        mode switch
        {
            ProductionRequestMode.Repeat =>
                "REPEAT",
            ProductionRequestMode.DesiredStock =>
                "STOCK",
            _ =>
                "ONCE"
        };

    private static PlayerActionDockItemState ResolveTacticalItemState(
        int index,
        PlayerTacticalActionReadModel? tactical)
    {
        if (tactical is null)
        {
            return PlayerActionDockItemState.Disabled(
                "SELECT UNITS");
        }

        if (index < 0 ||
            index >= 8)
        {
            return PlayerActionDockItemState.Disabled(
                "UNAVAILABLE");
        }

        if (tactical.CombatEligibleCount <= 0)
        {
            return PlayerActionDockItemState.Disabled(
                "NO ELIGIBLE UNITS");
        }

        if (index == 0 &&
            tactical.Targets.Count == 0)
        {
            return PlayerActionDockItemState.Disabled(
                "NO IDENTIFIED TARGET");
        }

        if (index == 5)
        {
            bool hasAmmo = false;

            for (int artilleryIndex = 0;
                 artilleryIndex <
                     tactical.Artillery.Count;
                 artilleryIndex++)
            {
                if (tactical.Artillery[
                        artilleryIndex]
                    .AmmunitionQuantity >
                    0.0)
                {
                    hasAmmo = true;
                    break;
                }
            }

            if (tactical.Artillery.Count == 0)
            {
                return PlayerActionDockItemState.Disabled(
                    "NO ARTILLERY");
            }

            if (!hasAmmo)
            {
                return PlayerActionDockItemState.Disabled(
                    "NO AMMO");
            }
        }

        if (index == 6)
        {
            bool hasMission = false;

            for (int artilleryIndex = 0;
                 artilleryIndex <
                     tactical.Artillery.Count;
                 artilleryIndex++)
            {
                if (tactical.Artillery[
                        artilleryIndex]
                    .HasActiveMission)
                {
                    hasMission = true;
                    break;
                }
            }

            if (!hasMission)
            {
                return PlayerActionDockItemState.Disabled(
                    "NO ACTIVE MISSION");
            }
        }

        return PlayerActionDockItemState.Enabled;
    }
}
