using ForgeLine.Core;

namespace ForgeLine.Game;

public enum ContentAvailabilityTier : byte
{
    Bootstrap = 0,
    IndustrialFoundation = 1,
    MechanizedWarfare = 2
}

public readonly record struct BuildingAvailability(
    BuildingId BuildingId,
    ContentAvailabilityTier Tier);

public readonly record struct UnitAvailability(
    UnitId UnitId,
    ContentAvailabilityTier Tier);

public sealed record FactionContentDefinition
{
    public required FactionId Id { get; init; }

    public required string Key { get; init; }

    public required string ContentNamespace { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<BuildingAvailability> Buildings { get; init; }

    public required IReadOnlyList<UnitAvailability> Units { get; init; }

    public void Validate()
    {
        if (!Id.IsSpecified)
        {
            throw new InvalidOperationException(
                "Faction content requires a stable faction ID.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(ContentNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentNullException.ThrowIfNull(Buildings);
        ArgumentNullException.ThrowIfNull(Units);

        if (Buildings.Count == 0 || Units.Count == 0)
        {
            throw new InvalidOperationException(
                $"Faction '{Key}' requires both building and unit content.");
        }

        var buildingIds = new HashSet<BuildingId>();
        foreach (BuildingAvailability entry in Buildings)
        {
            if (!entry.BuildingId.IsSpecified ||
                !Enum.IsDefined(entry.Tier) ||
                !buildingIds.Add(entry.BuildingId))
            {
                throw new InvalidOperationException(
                    $"Faction '{Key}' contains invalid or duplicate building availability.");
            }
        }

        var unitIds = new HashSet<UnitId>();
        foreach (UnitAvailability entry in Units)
        {
            if (!entry.UnitId.IsSpecified ||
                !Enum.IsDefined(entry.Tier) ||
                !unitIds.Add(entry.UnitId))
            {
                throw new InvalidOperationException(
                    $"Faction '{Key}' contains invalid or duplicate unit availability.");
            }
        }
    }

    public bool IsBuildingAvailable(
        BuildingId buildingId,
        ContentAvailabilityTier tier) =>
        Buildings.Any(
            entry =>
                entry.BuildingId == buildingId &&
                entry.Tier <= tier);

    public bool IsUnitAvailable(
        UnitId unitId,
        ContentAvailabilityTier tier) =>
        Units.Any(
            entry =>
                entry.UnitId == unitId &&
                entry.Tier <= tier);

    public IReadOnlyList<BuildingId> GetBuildMenu(
        ContentAvailabilityTier tier) =>
        Buildings
            .Where(entry => entry.Tier <= tier)
            .OrderBy(static entry => entry.Tier)
            .ThenBy(static entry => entry.BuildingId)
            .Select(static entry => entry.BuildingId)
            .ToArray();

    public IReadOnlyList<UnitId> GetUnitRoster(
        ContentAvailabilityTier tier) =>
        Units
            .Where(entry => entry.Tier <= tier)
            .OrderBy(static entry => entry.Tier)
            .ThenBy(static entry => entry.UnitId)
            .Select(static entry => entry.UnitId)
            .ToArray();
}
