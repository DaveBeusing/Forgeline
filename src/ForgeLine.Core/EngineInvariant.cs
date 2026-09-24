using System.Diagnostics.CodeAnalysis;
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

        Fail(category, code, message);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Fail(
        DiagnosticCategory category,
        string code,
        string message)
    {
        throw new EngineInvariantException(category, code, message);
    }
}
