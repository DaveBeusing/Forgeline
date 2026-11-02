using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BuildingConveyorSelectionPrivacyTests
{
    [Fact]
    public void InspectorRequiresCurrentSingleBuildingGenerationAndTick()
    {
        var building = new EntityId(7, 1);
        var selection = Selection(building);
        var connections = new BuildingConveyorConnectionSnapshot(building, false, []);
        var current = Snapshot(selection, new SimulationTick(3), new SimulationTick(3), connections);
        var stale = Snapshot(selection, new SimulationTick(3), new SimulationTick(2), connections);

        Assert.Same(connections, SelectionInspectorHudModel.ResolveConveyors(current, selection));
        Assert.Null(SelectionInspectorHudModel.ResolveConveyors(current,
            selection with { Count = 2 }));
        Assert.Null(SelectionInspectorHudModel.ResolveConveyors(current,
            selection with { Kind = PlayerSelectionKind.Unit }));
        Assert.Null(SelectionInspectorHudModel.ResolveConveyors(current,
            selection with { PrimaryEntity = new EntityId(7, 2) }));
        Assert.Null(SelectionInspectorHudModel.ResolveConveyors(stale, selection));
    }

    [Fact]
    public void UnavailableBuildingStateRemainsDistinctFromMissingInspectorData()
    {
        var building = new EntityId(9, 1);
        var selection = Selection(building);
        var unavailable = new BuildingConveyorConnectionSnapshot(building, true, []);
        var snapshot = Snapshot(selection, new SimulationTick(3), new SimulationTick(3), unavailable);

        Assert.True(SelectionInspectorHudModel.ResolveConveyors(snapshot, selection)!.Unavailable);
        Assert.Null(SelectionInspectorHudModel.ResolveConveyors(snapshot,
            selection with { PrimaryEntity = new EntityId(10, 1) }));
    }

    private static PlayerSelectionSummary Selection(EntityId building) =>
        new(1, building, PlayerSelectionKind.Building, "Storage Depot",
            false, 0, false, BattlefieldSupplyStatus.Supplied, 0, 0,
            false, 0, false, PowerOperationalState.Offline, false, 0,
            PlayerWorkSummary.None);

    private static PresentationSnapshot Snapshot(PlayerSelectionSummary selection,
        SimulationTick tick, SimulationTick conveyorTick, BuildingConveyorConnectionSnapshot summary)
    {
        var experience = new PlayerExperienceSnapshot(new PlayerId(1), tick,
            PlayerMatchStatus.Active, default, default, default, default,
            selection, PlayerAlertState.None, 0, 0, PlayerCommandFeedback.None, default);
        return new PresentationSnapshot(tick, TimeSpan.FromMilliseconds(50), 0, [],
            playerExperience: experience)
        {
            Conveyors = new ConveyorConstructionReadModel(0, conveyorTick, null, 0, [], [])
            {
                SelectedBuildingConnections = summary
            }
        };
    }
}
