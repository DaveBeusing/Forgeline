namespace ForgeLine.Game;

/// <summary>Developer-only composition hook. Tool owners replace missing slots when integrated.</summary>
internal static class CompiledDeveloperToolsHost
{
    internal static DeveloperToolsProviders Create() => new(DeveloperToolsCapability.Current);
}
