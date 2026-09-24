using System.Runtime.CompilerServices;

namespace ForgeLine.Core;

public static class EngineInvariant
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Require(
        bool condition,
        DiagnosticCategory category,
        string code,
        string message)
    {
        if (condition)
        {
            return;
        }

        Throw(category, code, message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Throw(
        DiagnosticCategory category,
        string code,
        string message)
    {
        throw new EngineInvariantException(category, code, message);
    }
}
