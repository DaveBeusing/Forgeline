using System.Diagnostics.CodeAnalysis;

namespace ForgeLine.Economy;

public sealed record PowerProfileDefinition
{
    public required string Key { get; init; }

    public double GenerationCapacity { get; init; }

    public double Demand { get; init; }

    public PowerPriority Priority { get; init; } = PowerPriority.Industrial;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Key);

        if (!double.IsFinite(GenerationCapacity) || GenerationCapacity < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(GenerationCapacity));
        }

        if (!double.IsFinite(Demand) || Demand < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(Demand));
        }

        if (GenerationCapacity <= 0.0 && Demand <= 0.0)
        {
            throw new InvalidOperationException(
                "Power profiles must define generation capacity, demand, or both.");
        }

        if (Priority is not PowerPriority.Critical
            and not PowerPriority.Industrial
            and not PowerPriority.Optional)
        {
            throw new ArgumentOutOfRangeException(nameof(Priority));
        }
    }

    public PowerGenerator CreateGenerator(bool enabled = true)
    {
        Validate();

        if (GenerationCapacity <= 0.0)
        {
            throw new InvalidOperationException(
                $"Power profile '{Key}' does not define generation capacity.");
        }

        return new PowerGenerator(GenerationCapacity, enabled);
    }

    public PowerConsumer CreateConsumer(bool enabled = true)
    {
        Validate();

        if (Demand <= 0.0)
        {
            throw new InvalidOperationException(
                $"Power profile '{Key}' does not define consumer demand.");
        }

        return new PowerConsumer(Demand, Priority, enabled);
    }
}

public sealed class PowerProfileCatalog
{
    private readonly Dictionary<string, PowerProfileDefinition> _profiles;

    public PowerProfileCatalog(IEnumerable<PowerProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        _profiles = new Dictionary<string, PowerProfileDefinition>(StringComparer.Ordinal);

        foreach (PowerProfileDefinition profile in profiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
            profile.Validate();

            if (!_profiles.TryAdd(profile.Key, profile))
            {
                throw new ArgumentException(
                    $"Duplicate power profile key '{profile.Key}'.",
                    nameof(profiles));
            }
        }
    }

    public int Count => _profiles.Count;

    public PowerProfileDefinition this[string key] =>
        _profiles.TryGetValue(key, out PowerProfileDefinition? profile)
            ? profile
            : throw new KeyNotFoundException($"Unknown power profile '{key}'.");

    public bool TryGet(
        string key,
        [NotNullWhen(true)] out PowerProfileDefinition? profile)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _profiles.TryGetValue(key, out profile);
    }
}
