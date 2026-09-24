namespace ForgeLine.Navigation;

public enum NavigationMovementClass : byte
{
    Infantry = 1,
    Wheeled = 2,
    Tracked = 3
}

public readonly record struct NavigationCapabilities
{
    public NavigationCapabilities(
        NavigationMovementClass movementClass,
        float maximumSlopeDegrees,
        float slopeCostWeight = 1.0f)
    {
        if (!Enum.IsDefined(movementClass))
        {
            throw new ArgumentOutOfRangeException(nameof(movementClass));
        }

        if (!float.IsFinite(maximumSlopeDegrees) ||
            maximumSlopeDegrees < 0.0f ||
            maximumSlopeDegrees >= 90.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSlopeDegrees));
        }

        if (!float.IsFinite(slopeCostWeight) || slopeCostWeight < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(slopeCostWeight));
        }

        MovementClass = movementClass;
        MaximumSlopeDegrees = maximumSlopeDegrees;
        SlopeCostWeight = slopeCostWeight;
    }

    public NavigationMovementClass MovementClass { get; }

    public float MaximumSlopeDegrees { get; }

    public float SlopeCostWeight { get; }

    public static NavigationCapabilities For(
        NavigationMovementClass movementClass)
    {
        return movementClass switch
        {
            NavigationMovementClass.Infantry =>
                new NavigationCapabilities(movementClass, 45.0f, 0.75f),
            NavigationMovementClass.Wheeled =>
                new NavigationCapabilities(movementClass, 24.0f, 1.5f),
            NavigationMovementClass.Tracked =>
                new NavigationCapabilities(movementClass, 35.0f, 1.0f),
            _ => throw new ArgumentOutOfRangeException(
                nameof(movementClass),
                movementClass,
                null)
        };
    }
}
