namespace ForgeLine.Core;

public readonly record struct EngineDiagnostic(
    DiagnosticCategory Category,
    DiagnosticSeverity Severity,
    string Code,
    string Message);
