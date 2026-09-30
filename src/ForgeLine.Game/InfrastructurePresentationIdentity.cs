namespace ForgeLine.Game;

public enum InfrastructurePresentationKind : byte
{
    RoadSegment = 1,
    RoadBridge = 2,
    Ford = 3
}

public readonly record struct InfrastructurePresentationIdentity
{
    public InfrastructurePresentationIdentity(
        InfrastructurePresentationKind kind,
        string key)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Kind = kind;
        Key = key;
    }

    public InfrastructurePresentationKind Kind { get; }

    public string Key { get; }
}
