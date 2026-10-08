using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PlayerSelectionSummaryTests
{
    [Fact]
    public void SingleBuildingCarriesAuthoritativeIdentityAndDetails()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 6201);

        PlayerSelectionSummary selection =
            CaptureSelection(
                scenario,
                [scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore]);

        Assert.Equal(
            1,
            selection.Count);
        Assert.Equal(
            PlayerSelectionKind.Building,
            selection.Kind);
        Assert.Equal(
            BuildingIds.CommandCore,
            selection.CommonBuildingId);
        Assert.Equal(
            UnitId.None,
            selection.CommonUnitId);
        Assert.True(
            selection.HasSingleEntityDetails);
        Assert.True(
            selection.HasCommonIdentity);
        Assert.True(
            selection.HasHealth);
        Assert.True(
            selection.HasPower);
        Assert.Equal(
            scenario.Services.BuildingDefinitions[
                BuildingIds.CommandCore].DisplayName,
            selection.DisplayName);
    }

    [Fact]
    public void SameTypeMultiSelectionKeepsOnlySafeCommonIdentity()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 6202);
        EntityId firstCargo =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[1];
        EntityId secondCargo =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[2];

        PlayerSelectionSummary selection =
            CaptureSelection(
                scenario,
                [firstCargo, secondCargo]);

        Assert.Equal(
            2,
            selection.Count);
        Assert.Equal(
            PlayerSelectionKind.Unit,
            selection.Kind);
        Assert.Equal(
            UnitIds.CargoTruck,
            selection.CommonUnitId);
        Assert.Equal(
            BuildingId.None,
            selection.CommonBuildingId);
        Assert.Equal(
            scenario.Services.UnitDefinitions[
                UnitIds.CargoTruck].DisplayName,
            selection.DisplayName);
        Assert.False(
            selection.HasSingleEntityDetails);
        Assert.False(
            selection.HasHealth);
        Assert.False(
            selection.HasSupply);
        Assert.False(
            selection.HasReadiness);
        Assert.False(
            selection.HasPower);
        Assert.Equal(
            PlayerWorkKind.None,
            selection.Work.Kind);
    }

    [Fact]
    public void HeterogeneousUnitSelectionDoesNotBorrowPrimaryEntityStatus()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 6203);
        EntityId engineer =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId cargo =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[1];

        PlayerSelectionSummary selection =
            CaptureSelection(
                scenario,
                [engineer, cargo]);

        Assert.Equal(
            2,
            selection.Count);
        Assert.Equal(
            PlayerSelectionKind.Unit,
            selection.Kind);
        Assert.Equal(
            UnitId.None,
            selection.CommonUnitId);
        Assert.Equal(
            "Units",
            selection.DisplayName);
        Assert.False(
            selection.HasSingleEntityDetails);
        Assert.False(
            selection.HasHealth);
        Assert.False(
            selection.HasSupply);
        Assert.Equal(
            PlayerWorkKind.None,
            selection.Work.Kind);
    }

    [Fact]
    public void MixedSelectionUsesGenericAggregateSummary()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 6204);

        PlayerSelectionSummary selection =
            CaptureSelection(
                scenario,
                [
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0],
                    scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore
                ]);

        Assert.Equal(
            2,
            selection.Count);
        Assert.Equal(
            PlayerSelectionKind.Mixed,
            selection.Kind);
        Assert.Equal(
            UnitId.None,
            selection.CommonUnitId);
        Assert.Equal(
            BuildingId.None,
            selection.CommonBuildingId);
        Assert.Equal(
            "Mixed Selection",
            selection.DisplayName);
        Assert.False(
            selection.HasSingleEntityDetails);
    }

    [Fact]
    public void ForeignAndDestroyedEntitiesAreFilteredBeforeInspection()
    {
        using MatchRuntime scenario =
            CentralDivideScenario.Create(
                seed: 6205);
        EntityId owned =
            scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingUnits[0];
        EntityId foreign =
            scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[0];

        PlayerSelectionSummary filtered =
            CaptureSelection(
                scenario,
                [owned, foreign]);

        Assert.Equal(
            1,
            filtered.Count);
        Assert.Equal(
            owned,
            filtered.PrimaryEntity);

        Assert.True(
            scenario.Simulation.Entities.DestroyEntity(
                owned));

        PlayerSelectionSummary cleared =
            CaptureSelection(
                scenario,
                [owned, foreign]);

        Assert.Equal(
            PlayerSelectionSummary.Empty,
            cleared);
    }

    private static PlayerSelectionSummary CaptureSelection(
        MatchRuntime scenario,
        IReadOnlyCollection<EntityId> selected)
    {
        PlayerExperienceSnapshot experience =
            PlayerExperienceSnapshotFactory.Capture(
                scenario.Simulation.Entities,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).Player,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).CommandCore,
                scenario.GetBase(new ForgeLine.Game.PlayerId(1)).StartingInventory,
                scenario.BattlefieldRuntime.MatchStateEntity,
                selected,
                scenario.Inventories,
                scenario.Power,
                scenario.Production,
                scenario.UnitProduction,
                PlayerCommandFeedback.None,
                scenario.Intelligence,
                scenario.Services.UnitDefinitions,
                scenario.Services.BuildingDefinitions,
                scenario.Simulation.CurrentTick,
                scenario.Simulation.Clock.TicksPerSecond);

        return experience.Selection;
    }
}
