using ForgeLine.Core;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CombatGroupInputControllerTests
{
    [Fact]
    public void ControlDigitAssignsAndPlainDigitRecallsExistingSelection()
    {
        EntityId first =
            new(10, 1);
        EntityId second =
            new(11, 1);
        PresentationSnapshot snapshot =
            Snapshot(
                new SimulationSessionId(1),
                [first, second]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();
        var controller =
            new CombatGroupInputController();
        var input =
            new InputState();

        selection.Replace(
            [first, second]);
        KeyDown(
            input,
            PlatformKey.LeftControl);
        KeyDown(
            input,
            PlatformKey.D1);

        CombatGroupInputResult assigned =
            controller.Update(
                input,
                snapshot,
                registry,
                selection);

        Assert.Equal(
            CombatGroupInputAction.Assigned,
            assigned.Action);
        Assert.Equal(
            1,
            assigned.Slot);

        KeyUp(
            input,
            PlatformKey.D1);
        KeyUp(
            input,
            PlatformKey.LeftControl);
        controller.Update(
            input,
            snapshot,
            registry,
            selection);

        selection.SetSingle(
            first);
        KeyDown(
            input,
            PlatformKey.D1);

        CombatGroupInputResult recalled =
            controller.Update(
                input,
                snapshot,
                registry,
                selection);

        Assert.Equal(
            CombatGroupInputAction.Recalled,
            recalled.Action);
        Assert.Equal(
            new[] { first, second },
            selection.ToArray());
    }

    [Fact]
    public void ControlShiftDigitClearsAssignedGroup()
    {
        EntityId unit =
            new(20, 1);
        PresentationSnapshot snapshot =
            Snapshot(
                new SimulationSessionId(2),
                [unit]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();
        var controller =
            new CombatGroupInputController();
        var input =
            new InputState();

        registry.Synchronize(
            snapshot.SessionId,
            snapshot.CombatGroups!.EligibleEntities);
        registry.Assign(
            2,
            [unit],
            snapshot.CombatGroups.EligibleEntities);

        KeyDown(
            input,
            PlatformKey.LeftControl);
        KeyDown(
            input,
            PlatformKey.LeftShift);
        KeyDown(
            input,
            PlatformKey.D2);

        CombatGroupInputResult result =
            controller.Update(
                input,
                snapshot,
                registry,
                selection);

        Assert.Equal(
            CombatGroupInputAction.Cleared,
            result.Action);
        Assert.Empty(
            registry.GetValidMembers(
                2));
    }

    [Fact]
    public void BlockedInputDoesNotTriggerDelayedActionWhenKeyRemainsHeld()
    {
        EntityId unit =
            new(30, 1);
        PresentationSnapshot snapshot =
            Snapshot(
                new SimulationSessionId(3),
                [unit]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();
        var controller =
            new CombatGroupInputController();
        var input =
            new InputState();

        selection.SetSingle(
            unit);
        KeyDown(
            input,
            PlatformKey.LeftControl);
        KeyDown(
            input,
            PlatformKey.D3);

        CombatGroupInputResult blocked =
            controller.Update(
                input,
                snapshot,
                registry,
                selection,
                inputBlocked: true);
        CombatGroupInputResult stillHeld =
            controller.Update(
                input,
                snapshot,
                registry,
                selection,
                inputBlocked: false);

        Assert.False(
            blocked.Handled);
        Assert.False(
            stillHeld.Handled);
        Assert.Empty(
            registry.GetValidMembers(
                3));
    }

    [Fact]
    public void SessionReplacementPrunesAssignmentsBeforeRecall()
    {
        EntityId oldUnit =
            new(40, 1);
        EntityId newUnit =
            new(41, 1);
        PresentationSnapshot first =
            Snapshot(
                new SimulationSessionId(4),
                [oldUnit]);
        PresentationSnapshot replacement =
            Snapshot(
                new SimulationSessionId(5),
                [newUnit]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();
        var controller =
            new CombatGroupInputController();
        var input =
            new InputState();

        registry.Synchronize(
            first.SessionId,
            first.CombatGroups!.EligibleEntities);
        registry.Assign(
            4,
            [oldUnit],
            first.CombatGroups.EligibleEntities);
        selection.SetSingle(
            newUnit);

        KeyDown(
            input,
            PlatformKey.D4);

        CombatGroupInputResult result =
            controller.Update(
                input,
                replacement,
                registry,
                selection);

        Assert.False(
            result.Handled);
        Assert.Equal(
            new[] { newUnit },
            selection.ToArray());
        Assert.Empty(
            registry.GetValidMembers(
                4));
    }

    [Fact]
    public void ShiftDigitDoesNotRecallOrModifyAssignedGroup()
    {
        EntityId first =
            new(50, 1);
        EntityId second =
            new(51, 1);
        PresentationSnapshot snapshot =
            Snapshot(
                new SimulationSessionId(6),
                [first, second]);
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();
        var controller =
            new CombatGroupInputController();
        var input =
            new InputState();

        registry.Synchronize(
            snapshot.SessionId,
            snapshot.CombatGroups!.EligibleEntities);
        registry.Assign(
            5,
            [first, second],
            snapshot.CombatGroups.EligibleEntities);
        selection.SetSingle(
            first);

        KeyDown(
            input,
            PlatformKey.LeftShift);
        KeyDown(
            input,
            PlatformKey.D5);

        CombatGroupInputResult result =
            controller.Update(
                input,
                snapshot,
                registry,
                selection);

        Assert.False(
            result.Handled);
        Assert.Equal(
            new[] { first },
            selection.ToArray());
        Assert.Equal(
            new[] { first, second },
            registry.GetValidMembers(
                5));
    }

    [Theory]
    [InlineData(0, PlatformKey.D0)]
    [InlineData(1, PlatformKey.D1)]
    [InlineData(5, PlatformKey.D5)]
    [InlineData(9, PlatformKey.D9)]
    public void SlotKeysFollowRtsDigitConvention(
        int slot,
        PlatformKey expected) =>
        Assert.Equal(
            expected,
            CombatGroupInputController.ResolveKey(
                slot));

    private static PresentationSnapshot Snapshot(
        SimulationSessionId session,
        IReadOnlyList<EntityId> entities)
    {
        var members =
            entities
                .Select(
                    entity =>
                        new CombatGroupMemberReadModel(
                            entity,
                            Game.ControllableEntityCategory.Unit,
                            HasHealth: false,
                            HealthFraction: 0.0,
                            HasSupply: false,
                            Game.BattlefieldSupplyStatus.Supplied,
                            FuelFraction: 0.0,
                            AmmunitionFraction: 0.0,
                            HasReadiness: false,
                            Strength: 0.0,
                            Readiness: 0.0,
                            HasFormation: false,
                            Game.FormationTemplate.Compact,
                            HasOrder: false,
                            Order: default,
                            HasOrderStatus: false,
                            OrderStatus: default))
                .ToArray();

        return new PresentationSnapshot(
            new SimulationTick(1),
            TimeSpan.FromMilliseconds(50),
            entities.Count,
            ReadOnlySpan<RenderInstance>.Empty,
            sessionId:
                session,
            combatGroups:
                new CombatGroupOperationalSnapshot(
                    new SimulationTick(1),
                    members));
    }

    private static void KeyDown(
        InputState input,
        PlatformKey key) =>
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                key));

    private static void KeyUp(
        InputState input,
        PlatformKey key) =>
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyUp,
                key));
}
