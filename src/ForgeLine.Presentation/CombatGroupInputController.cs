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
    int Slot,
    bool FocusRequested = false)
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
    private readonly ulong[] _pressSequences = new ulong[CombatGroupRegistry.SlotCount];
    private readonly TimeSpan _doubleTapInterval;
    private TimeSpan _time;
    private TimeSpan _lastRecallTime;
    private int _lastRecallSlot = -1;
    private SimulationSessionId _session;

    public CombatGroupInputController(TimeSpan? doubleTapInterval = null)
    {
        _doubleTapInterval = doubleTapInterval ?? TimeSpan.FromMilliseconds(350);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_doubleTapInterval, TimeSpan.Zero);
    }
    private readonly bool[] _held =
        new bool[
            CombatGroupRegistry.SlotCount];

    public CombatGroupInputResult Update(
        InputState input,
        PresentationSnapshot? snapshot,
        CombatGroupRegistry registry,
        SelectionSet selection,
        bool inputBlocked = false,
        TimeSpan elapsed = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        _time += elapsed;
        if (_session != snapshot?.SessionId || inputBlocked || input.FocusLostThisFrame || snapshot?.PlayerExperience?.IsMatchComplete == true)
            _lastRecallSlot = -1;
        _session = snapshot?.SessionId ?? default;

        CombatGroupOperationalSnapshot? operational = snapshot?.CombatGroups;
        if (operational is not null && (operational.Tick != snapshot!.Tick ||
            (operational.SessionId.IsSpecified && operational.SessionId != snapshot.SessionId))) operational = null;
        IReadOnlyCollection<ForgeLine.Core.EntityId> valid =
            operational?.EligibleEntities ??
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
        if (control || shift) _lastRecallSlot = -1;

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
            ulong sequence = input.KeyPressSequence(key);
            bool pressed = input.WasKeyPressed(key) && sequence != _pressSequences[slot];
            _pressSequences[slot] = sequence;
            _held[slot] =
                down;

            if (!pressed ||
                inputBlocked ||
                input.FocusLostThisFrame ||
                terminal ||
                operational is null ||
                result.Handled)
            {
                continue;
            }

            if (control &&
                shift)
            {
                _lastRecallSlot = -1;
                registry.Clear(
                    slot);
                result =
                    new CombatGroupInputResult(
                        CombatGroupInputAction.Cleared,
                        slot);
            }
            else if (control)
            {
                _lastRecallSlot = -1;
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
                        slot,
                        _lastRecallSlot == slot && _time - _lastRecallTime <= _doubleTapInterval);
                _lastRecallSlot = result.FocusRequested ? -1 : slot;
                _lastRecallTime = _time;
            }
            else _lastRecallSlot = -1;
        }

        return result;
    }

    public void Reset()
    {
        Array.Clear(
            _held);
        _lastRecallSlot = -1;
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
