namespace ForgeLine.Game;

public enum InfrastructurePresentationKind : byte
{
    RoadSegment = 1,
    RoadBridge = 2,
    Ford = 3,
    RoadShoulder = 4,
    RoadCurveShort = 5,
    RoadCurveLong = 6,
    RoadJunctionT = 7,
    RoadJunctionCross = 8
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
