using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ContextFeedbackTests
{
    [Theory]
    [InlineData(BuildingPlacementFailureReason.UnknownBuilding, "UNKNOWN BUILDING")]
    [InlineData(BuildingPlacementFailureReason.OutsideWorldBounds, "OUT OF BOUNDS")]
    [InlineData(BuildingPlacementFailureReason.SlopeTooSteep, "SLOPE")]
    [InlineData(BuildingPlacementFailureReason.Obstructed, "OBSTRUCTED")]
    [InlineData(BuildingPlacementFailureReason.ResourceDepositRequired, "DEPOSIT REQUIRED")]
    [InlineData(BuildingPlacementFailureReason.TerrainUnavailable, "NO TERRAIN")]
    [InlineData(BuildingPlacementFailureReason.OutsideBuildableArea, "NO BUILD AREA")]
    public void CurrentInvalidPreviewUsesAllowedReasonWithoutBlockerIdentity(BuildingPlacementFailureReason reason, string expected)
    {
        var preview = new BuildingPlacementPreview(BuildingIds.Extractor, "", "Mine / Extractor", default,
            BuildingOrientation.North, default, false, reason, new EntityId(999, 55));
        var view = PlacementContextFeedback.Resolve(BuildingIds.Extractor, PlacementPreviewFreshness.Current, preview, Snapshot());
        Assert.Equal(PlacementContextState.Invalid, view.State);
        Assert.Equal(expected, view.Reason);
        Assert.DoesNotContain("999", view.Hint, StringComparison.Ordinal);
        Assert.Null(view.Costs);
    }

    [Theory]
    [InlineData(PlacementPreviewFreshness.Stale, PlacementContextState.Stale)]
    [InlineData(PlacementPreviewFreshness.Unavailable, PlacementContextState.Pending)]
    public void OldValidPreviewDoesNotClaimCurrentValidity(PlacementPreviewFreshness freshness, PlacementContextState expected)
    {
        var view = PlacementContextFeedback.Resolve(BuildingIds.VehicleFactory, freshness, ValidPreview(), Snapshot());
        Assert.Equal(expected, view.State);
        Assert.Null(view.Costs);
    }

    [Fact]
    public void ShortageReportsOnlyCopiedCoreQuantitiesAndMissingModelsOmitAmounts()
    {
        var costs = new[] { new PlayerActionResourceAmount(ResourceIds.Steel, "Steel", 250, 10) };
        var action = new PlayerConstructionActionReadModel(BuildingIds.VehicleFactory, "Vehicle Factory", costs, false);
        var snapshot = Snapshot(new PlayerActionSnapshot(new SimulationSessionId(1), new SimulationTick(1), [action], 0, null, null));
        var shortage = PlacementContextFeedback.Resolve(BuildingIds.VehicleFactory, PlacementPreviewFreshness.Current, ValidPreview(), snapshot);
        Assert.Equal(PlacementContextState.MissingMaterials, shortage.State);
        Assert.Equal(10, Assert.Single(shortage.Costs!).AvailableQuantity);
        Assert.Equal(250, Assert.Single(shortage.Costs!).RequiredQuantity);
        var missing = PlacementContextFeedback.Resolve(BuildingIds.VehicleFactory, PlacementPreviewFreshness.Current, ValidPreview(), Snapshot());
        Assert.Equal(PlacementContextState.Valid, missing.State);
        Assert.Null(missing.Costs);
        Assert.Contains("REQUEST", missing.Hint, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProductionBlockReason.NoInput, "CURRENT WORK: NO INPUT", "DELIVER INPUTS")]
    [InlineData(ProductionBlockReason.NoPower, "CURRENT WORK: NO POWER", "ADD POWER")]
    [InlineData(ProductionBlockReason.OutputFull, "CURRENT WORK: OUTPUT FULL", "MOVE OUTPUT")]
    [InlineData(ProductionBlockReason.Paused, "CURRENT WORK: PAUSED", "CHECK PAUSED")]
    public void DockWorkExplanationUsesCapturedBlockReasonWithoutChangingQueueAvailability(ProductionBlockReason reason, string label, string explanation)
    {
        var facility = new PlayerProductionFacilityActionReadModel(new EntityId(3, 1), EntityId.Invalid, RecipeId.None,
            ProductionStatus.Idle, reason, 0,
            [new PlayerProductionRecipeActionReadModel(RecipeIds.Steel, "Steel",
                [new PlayerActionResourceAmount(ResourceIds.FerrousOre, "Ferrous Ore", 10, 20)], [])], []);
        var snapshot = Snapshot(new PlayerActionSnapshot(new SimulationSessionId(1), new SimulationTick(1), [], 1, facility, null));
        var panel = default(PlayerActionPanelView) with { Mode = PlayerActionPanelMode.Production };
        var hover = new HoverTooltipView(snapshot.SessionId, EntityId.Invalid, default, default, true, 1600, 900, 1,
            panel.Mode, (int)PlayerActionDockControlKind.Item, 0);
        var content = Assert.IsType<HoverTooltipContent>(HoverTooltipResolver.Resolve(snapshot, hover, panel));
        Assert.Equal(label, content.WorkStatus);
        Assert.StartsWith(explanation, content.Explanation, StringComparison.Ordinal);
        Assert.True(PlayerActionDockHudModel.ResolveItemState(panel.Mode, 0, snapshot.PlayerActions).CanActivate);
    }

    private static BuildingPlacementPreview ValidPreview() => new(BuildingIds.VehicleFactory, "", "Vehicle Factory",
        default, BuildingOrientation.North, default, true, BuildingPlacementFailureReason.None, EntityId.Invalid);
    private static PresentationSnapshot Snapshot(PlayerActionSnapshot? actions = null) => new(new SimulationTick(1), TimeSpan.Zero, 0, [],
        sessionId: new SimulationSessionId(1), playerExperience: default(PlayerExperienceSnapshot) with { Player = new PlayerId(1) }, playerActions: actions);
}
