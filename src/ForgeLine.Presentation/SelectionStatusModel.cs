using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal static class SelectionInspectorHudModel
{
    public static RtsUiIcon ResolveRoleIcon(
        in PlayerSelectionSummary selection)
    {
        if (selection.CommonUnitId.IsSpecified)
        {
            return RtsUiIconCatalog.ResolveUnitRole(
                selection.CommonUnitId);
        }

        if (selection.CommonBuildingId.IsSpecified)
        {
            return RtsUiIconCatalog.ResolveBuildingRole(
                selection.CommonBuildingId);
        }

        return selection.Kind switch
        {
            PlayerSelectionKind.Infrastructure => RtsUiIcon.CommandBuild,
            PlayerSelectionKind.Unit =>
                RtsUiIcon.MinimapSelectedGroup,
            PlayerSelectionKind.Building =>
                RtsUiIcon.MinimapBuilding,
            PlayerSelectionKind.Construction =>
                RtsUiIcon.CommandBuild,
            PlayerSelectionKind.Mixed =>
                RtsUiIcon.MinimapSelectedGroup,
            _ =>
                RtsUiIcon.CursorSelect
        };
    }

    public static string ResolveKindLabel(
        PlayerSelectionKind kind) =>
        kind switch
        {
            PlayerSelectionKind.Infrastructure => "INFRASTRUCTURE",
            PlayerSelectionKind.Unit =>
                "UNIT",
            PlayerSelectionKind.Building =>
                "BUILDING",
            PlayerSelectionKind.Construction =>
                "CONSTRUCTION",
            PlayerSelectionKind.Mixed =>
                "MIXED",
            _ =>
                "SELECTION"
        };

    public static string ResolveSupplyLabel(
        BattlefieldSupplyStatus status) =>
        status switch
        {
            BattlefieldSupplyStatus.Supplied =>
                "SUPPLIED",
            BattlefieldSupplyStatus.LowSupply =>
                "LOW SUPPLY",
            BattlefieldSupplyStatus.Critical =>
                "CRITICAL",
            BattlefieldSupplyStatus.Unsupplied =>
                "UNSUPPLIED",
            _ =>
                "SUPPLY"
        };

    public static string ResolvePowerLabel(
        PowerOperationalState state) =>
        state switch
        {
            PowerOperationalState.Powered =>
                "POWERED",
            PowerOperationalState.Brownout =>
                "BROWNOUT",
            _ =>
                "OFFLINE"
        };

    public static string ResolveWorkStateLabel(
        PlayerWorkState state) =>
        state switch
        {
            PlayerWorkState.Idle =>
                "IDLE",
            PlayerWorkState.Running =>
                "RUNNING",
            PlayerWorkState.Blocked =>
                "BLOCKED",
            _ =>
                "NONE"
        };

    public static RtsUiIcon ResolveWorkIcon(
        PlayerWorkKind kind) =>
        kind switch
        {
            PlayerWorkKind.Construction =>
                RtsUiIcon.CommandBuild,
            PlayerWorkKind.UnitProduction =>
                RtsUiIcon.BuildingFactory,
            PlayerWorkKind.Processing =>
                RtsUiIcon.BuildingProcessing,
            _ =>
                RtsUiIcon.CursorSelect
        };
}
