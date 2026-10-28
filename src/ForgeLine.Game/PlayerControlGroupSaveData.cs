using ForgeLine.Core;

namespace ForgeLine.Game;

// Optional local-player organization. These facts grant no simulation command authority.
public sealed record PlayerControlGroupSlot(int Slot, string Label, IReadOnlyList<EntityId> Members);
public sealed record PlayerControlGroupSaveData(PlayerId Player, int ActiveSlot, IReadOnlyList<PlayerControlGroupSlot> Slots)
{
    public const int MaximumSlots = 10;
    public const int MaximumMembersPerSlot = 1024;

    internal static void Validate(PlayerControlGroupSaveData? data, PersistedMatchConfiguration configuration, int schema)
    {
        if (data is null) return;
        MatchPersistenceValidation.Require(schema >= 12 && data.Player.IsSpecified &&
            configuration.Participants.Any(p => p.Player == data.Player) && data.ActiveSlot is >= -1 and < MaximumSlots &&
            data.Slots is { Count: <= MaximumSlots }, "Invalid saved control groups.");
        var seen = new HashSet<int>();
        foreach (var slot in data.Slots)
            MatchPersistenceValidation.Require(slot is not null && slot.Slot is >= 0 and < MaximumSlots && seen.Add(slot.Slot) &&
                !string.IsNullOrWhiteSpace(slot.Label) && slot.Label.Length <= 24 && !slot.Label.Any(char.IsControl) &&
                slot.Members is { Count: <= MaximumMembersPerSlot } && slot.Members.All(e => e.IsValid) &&
                slot.Members.Distinct().Count() == slot.Members.Count, "Invalid saved control-group slot.");
    }
}
