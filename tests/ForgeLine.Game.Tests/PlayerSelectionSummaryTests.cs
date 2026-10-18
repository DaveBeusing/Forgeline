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

    [Fact]
    public void QueueDetailsAreCopiedOwnedAndDoNotReplaceBuildingProduction()
    {
        using var scenario = CentralDivideScenario.Create(seed: 6207);
        var entities = scenario.Simulation.Entities;
        var side = scenario.GetBase(new ForgeLine.Game.PlayerId(1));
        EntityId truck = side.StartingUnits[1];
        var entry = new VehicleQueueEntry(side.CommandCore, side.Player, 7, new(10), VehicleQueueService.CargoDelivery,
            Position: 2, WaitingCount: 3, ChangedAtTick: new(11));
        entities.AddComponent(truck, entry);
        entities.AddComponent(truck, new VehicleDockVisit(side.CommandCore, 0, VehicleDockStatus.Busy,
            VehicleDockBlockReason.Obstructed, default));
        var captured = CaptureSelection(scenario, [truck]);
        Assert.Equal(2, captured.Work.VehicleQueue!.Value.Position);
        Assert.Equal(new SimulationTick(11), captured.Work.VehicleQueue.Value.ChangedAtTick);
        Assert.Equal(captured, CaptureSelection(scenario, [truck]));
        Assert.Equal(1, CaptureSelection(scenario, [side.CommandCore]).Work.LoadingQueue!.Value.WaitingCount);
        entities.SetComponent(truck, entry with { Position = 1, ChangedAtTick = new(12) });
        Assert.Equal(2, captured.Work.VehicleQueue.Value.Position);
        Assert.Equal(1, CaptureSelection(scenario, [truck]).Work.VehicleQueue!.Value.Position);
        entities.AddComponent(side.CommandCore, new VehicleDockSlot(truck, 1, default, side.Player));
        entities.SetComponent(truck, new VehicleDockVisit(side.CommandCore, 1, VehicleDockStatus.Servicing,
            VehicleDockBlockReason.None, default));
        Assert.Equal(0, CaptureSelection(scenario, [truck]).Work.VehicleQueue!.Value.WaitingCount);
        Assert.Equal(0, CaptureSelection(scenario, [side.CommandCore]).Work.LoadingQueue!.Value.WaitingCount);
        entities.RemoveComponent<VehicleDockSlot>(side.CommandCore);
        entities.SetComponent(truck, new VehicleDockVisit(side.CommandCore, 0, VehicleDockStatus.Busy,
            VehicleDockBlockReason.Obstructed, default));
        EntityId foreign = scenario.GetBase(new ForgeLine.Game.PlayerId(2)).StartingUnits[1];
        entities.AddComponent(foreign, entry with { Owner = new(2) });
        entities.AddComponent(foreign, new VehicleDockVisit(side.CommandCore, 0, VehicleDockStatus.Busy,
            VehicleDockBlockReason.Obstructed, default));
        Assert.Equal(PlayerSelectionSummary.Empty, CaptureSelection(scenario, [foreign]));
        Assert.Equal(1, CaptureSelection(scenario, [side.CommandCore]).Work.LoadingQueue!.Value.WaitingCount);
        entities.RemoveComponent<VehicleQueueEntry>(truck);
        entities.AddComponent(truck, new VehicleQueueHolding(side.CommandCore, default, VehicleQueueWaitReason.Full, new(13)));
        var held = CaptureSelection(scenario, [truck]).Work;
        Assert.Equal(0, held.VehicleQueue!.Value.Position);
        Assert.Equal(VehicleQueueWaitReason.Full, held.VehicleQueue.Value.WaitReason);
        Assert.Equal("Waiting area full", held.BlockReason);
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
