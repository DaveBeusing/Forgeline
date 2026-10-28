namespace ForgeLine.World;

public enum WorldGenerationError
{
    UnknownGeneratorVersion = 1,
    InvalidLimits = 2,
    InvalidProfileId = 3,
    UnknownOptions = 4,
    InvalidContent = 5
}

public readonly record struct WorldGenerationDiagnostic(WorldGenerationError Code, string Message);

/// <summary>A completed value or an explicit failure, usable without a client or renderer.</summary>
public sealed class WorldGenerationResult<T> where T : class
{
    public WorldGenerationResult(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public WorldGenerationResult(WorldGenerationDiagnostic diagnostic)
    {
        if (!Enum.IsDefined(diagnostic.Code) || string.IsNullOrWhiteSpace(diagnostic.Message) ||
            diagnostic.Message.Length > 1024)
            throw new ArgumentException("A known error and bounded message are required.", nameof(diagnostic));
        Diagnostic = diagnostic;
    }

    public bool Succeeded => Diagnostic is null;
    public T? Value { get; }
    public WorldGenerationDiagnostic? Diagnostic { get; }
}
