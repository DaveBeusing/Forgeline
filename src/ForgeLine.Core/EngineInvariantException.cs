namespace ForgeLine.Core;

public sealed class EngineInvariantException : InvalidOperationException
{
    public EngineInvariantException(
        DiagnosticCategory category,
        string code,
        string message)
        : base($"[{category}:{code}] {message}")
    {
        Category = category;
        Code = code;
    }

    public DiagnosticCategory Category { get; }

    public string Code { get; }
}
