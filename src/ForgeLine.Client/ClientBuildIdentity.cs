using System.Reflection;

namespace ForgeLine.Client;

internal static class ClientBuildIdentity
{
    internal static string InformationalVersion =>
        typeof(ClientBuildIdentity).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";

    internal static string ProductVersion =>
        InformationalVersion.Split('+')[0];

    internal static string SourceRevision =>
        InformationalVersion.Contains('+', StringComparison.Ordinal)
            ? InformationalVersion.Split('+')[1]
            : "unknown";

    internal static string Channel =>
        ProductVersion.Contains('-', StringComparison.Ordinal)
            ? ProductVersion.Split('-')[1].Split('.')[0]
            : "stable";

    internal static string BuildId =>
        typeof(ClientBuildIdentity).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildId")?
            .Value ?? "local";

    internal static string BuildVariant =>
        typeof(ClientBuildIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "BuildVariant").Value ?? "unknown";

    internal static bool DeveloperToolsEnabled => ForgeLine.Game.DeveloperToolsCapability.Current.IsCompiled;

    internal static string DisplayVersion =>
        $"{ProductVersion}+{SourceRevision[..Math.Min(12, SourceRevision.Length)]} [{BuildVariant}]";
}
