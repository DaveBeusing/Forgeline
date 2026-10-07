using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CombatGroupRegistryTests
{
    [Fact]
    public void AssignRecallAndClearUseExistingSelectionAuthority()
    {
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();
        var session =
            new SimulationSessionId(10);
        EntityId first =
            new(4, 1);
        EntityId second =
            new(8, 1);
        EntityId invalid =
            new(12, 1);

        registry.Synchronize(
            session,
            [first, second]);
        selection.Replace(
            [first, second, invalid]);

        Assert.Equal(
            2,
            registry.Assign(
                1,
                selection.Entities,
                [first, second]));

        selection.SetSingle(
            first);

        Assert.True(
            registry.Recall(
                1,
                selection));
        Assert.Equal(
            new[] { first, second },
            selection.ToArray());
        Assert.Equal(
            1,
            registry.ActiveSlot);

        registry.Clear(
            1);

        Assert.False(
            registry.GetSlot(
                1).IsAssigned);
        Assert.Equal(
            CombatGroupRegistry.NoActiveSlot,
            registry.ActiveSlot);
    }

    [Fact]
    public void StaleGenerationCannotBecomeReplacementMember()
    {
        var registry =
            new CombatGroupRegistry();
        var session =
            new SimulationSessionId(20);
        EntityId original =
            new(5, 1);
        EntityId reusedSlot =
            new(5, 2);

        registry.Synchronize(
            session,
            [original]);
        registry.Assign(
            2,
            [original],
            [original]);

        registry.Synchronize(
            session,
            [reusedSlot]);

        Assert.Empty(
            registry.GetValidMembers(
                2));
        Assert.DoesNotContain(
            reusedSlot,
            registry.GetValidMembers(
                2));
    }

    [Fact]
    public void SessionReplacementClearsAssignmentsNamesAndActiveSlot()
    {
        var registry =
            new CombatGroupRegistry();
        EntityId unit =
            new(3, 1);

        registry.Synchronize(
            new SimulationSessionId(30),
            [unit]);
        registry.Assign(
            3,
            [unit],
            [unit]);
        Assert.True(
            registry.Rename(
                3,
                "SPEARHEAD"));

        registry.Synchronize(
            new SimulationSessionId(31),
            [unit]);

        CombatGroupSlotView slot =
            registry.GetSlot(
                3);
        Assert.Empty(
            slot.Members);
        Assert.Equal(
            "GROUP 3",
            slot.Label);
        Assert.Equal(
            CombatGroupRegistry.NoActiveSlot,
            registry.ActiveSlot);
    }

    [Fact]
    public void EmptyRecallDoesNotReplaceCurrentSelection()
    {
        var registry =
            new CombatGroupRegistry();
        var selection =
            new SelectionSet();
        EntityId unit =
            new(7, 1);

        registry.Synchronize(
            new SimulationSessionId(40),
            [unit]);
        selection.SetSingle(
            unit);

        Assert.False(
            registry.Recall(
                8,
                selection));
        Assert.Equal(
            new[] { unit },
            selection.ToArray());
    }
}
