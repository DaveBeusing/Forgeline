using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ContextCursorTests
{
    [Fact]
    public void CompletedTickPublishesOwnedMovementEvidenceAndHomeIdentity()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var buffer = WorldHoverExtractionTests.Observe(scenario, new PresentationInteractionState());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        Assert.True(RtsCameraFocusController.TryHome(snapshot, new(1), out var home));
        Assert.Equal(scenario.Simulation.Entities.GetComponent<WorldTransform>(scenario.GetBase(new PlayerId(1)).CommandCore).Position, home);
        Assert.Contains(snapshot.Instances.ToArray(), instance => instance.Selectable.Owner == new PlayerId(1) && instance.Selectable.CanMove);
        Assert.DoesNotContain(snapshot.Instances.ToArray(), instance => instance.Selectable.Owner != new PlayerId(1) && instance.Selectable.CanMove);
    }
    [Theory]
    [InlineData(TacticalTargetingMode.Attack, RtsCursorKind.Attack)]
    [InlineData(TacticalTargetingMode.AttackMove, RtsCursorKind.AttackMove)]
    [InlineData(TacticalTargetingMode.Retreat, RtsCursorKind.Move)]
    [InlineData(TacticalTargetingMode.FireMission, RtsCursorKind.Attack)]
    public void SupportedTargetingUsesItsValidatedModeAndUiCaptureAlwaysWins(TacticalTargetingMode mode, RtsCursorKind expected)
    {
        var context = new RtsCursorContext(true, false, false, false, false, mode, true, false, true);
        Assert.Equal(expected, RtsCursorResolver.Resolve(context));
        Assert.Equal(RtsCursorKind.Invalid, RtsCursorResolver.Resolve(context with { TacticalTargetValid = false }));
        Assert.Equal(RtsCursorKind.Default, RtsCursorResolver.Resolve(context with { PointerCaptured = true }));
    }

    [Fact]
    public void SupplyPanelAloneOrImmobileSelectionCannotPromiseAWorldAction()
    {
        var context = new RtsCursorContext(true, false, false, false, false, default, false, false, true,
            SupplyModeActive: true, MovementSelectionSupported: false);
        Assert.Equal(RtsCursorKind.Default, RtsCursorResolver.Resolve(context));
        Assert.Equal(RtsCursorKind.Select, RtsCursorResolver.Resolve(context with { HoveredSelectable = true }));
        Assert.Equal(RtsCursorKind.Move, RtsCursorResolver.Resolve(context with { MovementSelectionSupported = true }));
        Assert.Equal(RtsCursorKind.Invalid, RtsCursorResolver.Resolve(context with { BuildingPlacementActive = true }));
        Assert.Equal(RtsCursorKind.Build, RtsCursorResolver.Resolve(context with { BuildingPlacementActive = true, BuildingPlacementValid = true }));
    }

    [Fact]
    public void MovementRequiresCopiedOwnedVisibleCapabilityAndCurrentGeneration()
    {
        var unit = SameTypeSelectionTests.Unit(1, default);
        var selection = new SelectionSet();
        selection.SetSingle(unit.Entity);
        PresentationSnapshot Snapshot(RenderInstance instance) => new(new(1), TimeSpan.FromMilliseconds(50), 1, [instance], sessionId: new(1));
        Assert.True(RtsCursorResolver.CanMoveSelection(Snapshot(unit), selection, new(1)));
        Assert.False(RtsCursorResolver.CanMoveSelection(Snapshot(unit with { Selectable = new(new PlayerId(2), ControllableEntityCategory.Unit, true) }), selection, new(1)));
        Assert.False(RtsCursorResolver.CanMoveSelection(Snapshot(unit with { Visibility = RenderVisibilityMask.None }), selection, new(1)));
        Assert.False(RtsCursorResolver.CanMoveSelection(Snapshot(unit with { Selectable = unit.Selectable with { CanMove = false } }), selection, new(1)));
        Assert.False(RtsCursorResolver.CanMoveSelection(Snapshot(unit with { Entity = new(1, 2) }), selection, new(1)));
    }
}
