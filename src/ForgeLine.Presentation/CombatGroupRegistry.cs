using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct CombatGroupSlotView(
    int Slot,
    string Label,
    EntityId[] Members,
    bool IsActive)
{
    public bool IsAssigned =>
        Members.Length >
        0;
}

public sealed class CombatGroupRegistry
{
    public const int SlotCount = 10;
    public const int NoActiveSlot = -1;

    private readonly SortedSet<EntityId>[] _members;
    private readonly string[] _labels;
    private SimulationSessionId _sessionId;

    public CombatGroupRegistry()
    {
        _members =
            Enumerable.Range(
                    0,
                    SlotCount)
                .Select(
                    static _ =>
                        new SortedSet<EntityId>())
                .ToArray();
        _labels =
            Enumerable.Range(
                    0,
                    SlotCount)
                .Select(
                    static slot =>
                        DefaultLabel(
                            slot))
                .ToArray();
    }

    public SimulationSessionId SessionId =>
        _sessionId;

    public int ActiveSlot { get; private set; } =
        NoActiveSlot;

    public void Synchronize(
        SimulationSessionId sessionId,
        IReadOnlyCollection<EntityId> validMembers)
    {
        ArgumentNullException.ThrowIfNull(validMembers);

        if (sessionId.IsSpecified &&
            sessionId !=
                _sessionId)
        {
            _sessionId =
                sessionId;
            ResetSlots();
        }

        if (!_sessionId.IsSpecified)
        {
            return;
        }

        Prune(
            validMembers);
    }

    public int Assign(
        int slot,
        IReadOnlyCollection<EntityId> selection,
        IReadOnlyCollection<EntityId> validMembers)
    {
        ValidateSlot(
            slot);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(validMembers);

        var valid =
            new HashSet<EntityId>(
                validMembers);
        SortedSet<EntityId> target =
            _members[slot];
        target.Clear();

        foreach (EntityId entity in selection)
        {
            if (entity.IsValid &&
                valid.Contains(
                    entity))
            {
                target.Add(
                    entity);
                if (target.Count == PlayerControlGroupSaveData.MaximumMembersPerSlot) break;
            }
        }

        if (target.Count >
            0)
        {
            ActiveSlot =
                slot;
        }
        else if (ActiveSlot ==
                 slot)
        {
            ActiveSlot =
                NoActiveSlot;
        }

        return target.Count;
    }

    public bool Recall(
        int slot,
        SelectionSet selection)
    {
        ValidateSlot(
            slot);
        ArgumentNullException.ThrowIfNull(selection);

        SortedSet<EntityId> members =
            _members[slot];

        if (members.Count ==
            0)
        {
            return false;
        }

        selection.Replace(
            members.ToArray());
        ActiveSlot =
            slot;
        return true;
    }

    public void Clear(
        int slot)
    {
        ValidateSlot(
            slot);
        _members[slot].Clear();

        if (ActiveSlot ==
            slot)
        {
            ActiveSlot =
                NoActiveSlot;
        }
    }

    public bool Rename(
        int slot,
        string label)
    {
        ValidateSlot(
            slot);

        if (string.IsNullOrWhiteSpace(
                label) || label.Any(char.IsControl))
        {
            return false;
        }

        string normalized =
            label.Trim();

        if (normalized.Length >
            24)
        {
            normalized =
                normalized[..24];
        }

        _labels[slot] =
            normalized;
        return true;
    }

    public CombatGroupSlotView GetSlot(
        int slot)
    {
        ValidateSlot(
            slot);

        return new CombatGroupSlotView(
            slot,
            _labels[slot],
            _members[slot].ToArray(),
            ActiveSlot ==
                slot);
    }

    public EntityId[] GetActiveMembers() =>
        ActiveSlot is >= 0 and < SlotCount
            ? _members[
                    ActiveSlot]
                .ToArray()
            : [];

    public EntityId[] GetValidMembers(
        int slot)
    {
        ValidateSlot(
            slot);
        return _members[
                slot]
            .ToArray();
    }

    public PlayerControlGroupSaveData Capture(PlayerId player)
    {
        var slots = new List<PlayerControlGroupSlot>(SlotCount);
        for (int i = 0; i < SlotCount; i++)
            if (_members[i].Count > 0)
                slots.Add(new(i, _labels[i], Array.AsReadOnly(_members[i].ToArray())));
        return new(player, ActiveSlot, slots.AsReadOnly());
    }

    public bool Restore(SimulationSessionId session, PlayerId player, PlayerControlGroupSaveData data,
        IReadOnlyCollection<EntityId> validMembers)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(validMembers);
        if (!session.IsSpecified || !player.IsSpecified || data.Player != player || data.Slots is null || data.Slots.Count > SlotCount ||
            data.ActiveSlot is < -1 or >= SlotCount) return false;
        var seen = new HashSet<int>();
        foreach (var slot in data.Slots)
            if (slot is null || slot.Slot is < 0 or >= SlotCount || !seen.Add(slot.Slot) ||
                string.IsNullOrWhiteSpace(slot.Label) || slot.Label.Length > 24 || slot.Label.Any(char.IsControl) ||
                slot.Members is null || slot.Members.Count > PlayerControlGroupSaveData.MaximumMembersPerSlot) return false;
        _sessionId = session;
        ResetSlots();
        foreach (var slot in data.Slots)
        {
            Assign(slot.Slot, slot.Members.ToArray(), validMembers);
            Rename(slot.Slot, slot.Label);
        }
        ActiveSlot = data.ActiveSlot >= 0 && _members[data.ActiveSlot].Count > 0 ? data.ActiveSlot : NoActiveSlot;
        return true;
    }

    private void Prune(
        IReadOnlyCollection<EntityId> validMembers)
    {
        var valid =
            new HashSet<EntityId>(
                validMembers);

        for (int slot = 0;
             slot < SlotCount;
             slot++)
        {
            _members[slot].RemoveWhere(
                entity =>
                    !valid.Contains(
                        entity));
        }

        if (ActiveSlot is >= 0 and < SlotCount &&
            _members[
                ActiveSlot].Count ==
            0)
        {
            ActiveSlot =
                NoActiveSlot;
        }
    }

    private void ResetSlots()
    {
        for (int slot = 0;
             slot < SlotCount;
             slot++)
        {
            _members[slot].Clear();
            _labels[slot] =
                DefaultLabel(
                    slot);
        }

        ActiveSlot =
            NoActiveSlot;
    }

    private static string DefaultLabel(
        int slot) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"GROUP {slot}");

    private static void ValidateSlot(
        int slot)
    {
        if (slot < 0 ||
            slot >=
                SlotCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slot));
        }
    }
}
