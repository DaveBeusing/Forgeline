using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public enum CombatGroupInputAction : byte
{
    None = 0,
    Assigned,
    Recalled,
    Cleared
}

public readonly record struct CombatGroupInputResult(
    CombatGroupInputAction Action,
    int Slot)
{
    public bool Handled =>
        Action !=
        CombatGroupInputAction.None;

    public static CombatGroupInputResult None =>
        new(
            CombatGroupInputAction.None,
            CombatGroupRegistry.NoActiveSlot);
}

public sealed class CombatGroupInputController
{
    private readonly bool[] _held =
        new bool[
            CombatGroupRegistry.SlotCount];

    public CombatGroupInputResult Update(
        InputState input,
        PresentationSnapshot? snapshot,
        CombatGroupRegistry registry,
        SelectionSet selection,
        bool inputBlocked = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(selection);

        IReadOnlyCollection<ForgeLine.Core.EntityId> valid =
            snapshot?.CombatGroups?.EligibleEntities ??
            Array.Empty<ForgeLine.Core.EntityId>();
        registry.Synchronize(
            snapshot?.SessionId ??
            SimulationSessionId.None,
            valid);

        bool terminal =
            snapshot?.PlayerExperience?.IsMatchComplete ==
            true;
        bool control =
            input.IsKeyDown(
                PlatformKey.LeftControl) ||
            input.IsKeyDown(
                PlatformKey.RightControl);
        bool shift =
            input.IsKeyDown(
                PlatformKey.LeftShift) ||
            input.IsKeyDown(
                PlatformKey.RightShift);

        CombatGroupInputResult result =
            CombatGroupInputResult.None;

        for (int slot = 0;
             slot <
                 CombatGroupRegistry.SlotCount;
             slot++)
        {
            PlatformKey key =
                ResolveKey(
                    slot);
            bool down =
                input.IsKeyDown(
                    key);
            bool pressed =
                down &&
                !_held[slot];
            _held[slot] =
                down;

            if (!pressed ||
                inputBlocked ||
                terminal ||
                snapshot?.CombatGroups is null ||
                result.Handled)
            {
                continue;
            }

            if (control &&
                shift)
            {
                registry.Clear(
                    slot);
                result =
                    new CombatGroupInputResult(
                        CombatGroupInputAction.Cleared,
                        slot);
            }
            else if (control)
            {
                int assigned =
                    registry.Assign(
                        slot,
                        selection.Entities,
                        valid);

                if (assigned >
                    0)
                {
                    result =
                        new CombatGroupInputResult(
                            CombatGroupInputAction.Assigned,
                            slot);
                }
            }
            else if (!shift &&
                     registry.Recall(
                         slot,
                         selection))
            {
                result =
                    new CombatGroupInputResult(
                        CombatGroupInputAction.Recalled,
                        slot);
            }
        }

        return result;
    }

    public void Reset()
    {
        Array.Clear(
            _held);
    }

    public static PlatformKey ResolveKey(
        int slot) =>
        slot switch
        {
            0 => PlatformKey.D0,
            1 => PlatformKey.D1,
            2 => PlatformKey.D2,
            3 => PlatformKey.D3,
            4 => PlatformKey.D4,
            5 => PlatformKey.D5,
            6 => PlatformKey.D6,
            7 => PlatformKey.D7,
            8 => PlatformKey.D8,
            9 => PlatformKey.D9,
            _ => throw new ArgumentOutOfRangeException(
                nameof(slot))
        };
}
