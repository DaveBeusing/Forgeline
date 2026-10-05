using ForgeLine.Assets;

namespace ForgeLine.Client;

internal readonly record struct RuntimeAssetPathResolution(
    string? RuntimeRoot,
    IReadOnlyList<string> CandidateRoots)
{
    public bool Found =>
        RuntimeRoot is not null;
}

internal static class RuntimeAssetPathResolver
{
    internal const string OverrideEnvironmentVariable =
        "FORGELINE_RUNTIME_ASSETS";

    public static RuntimeAssetPathResolution Resolve() =>
        Resolve(
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            Environment.GetEnvironmentVariable(
                OverrideEnvironmentVariable));

    internal static RuntimeAssetPathResolution Resolve(
        string applicationBaseDirectory,
        string currentDirectory,
        string? overrideRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            currentDirectory);

        string[] candidates =
            BuildCandidateRoots(
                applicationBaseDirectory,
                currentDirectory,
                overrideRoot);

        foreach (string candidate in candidates)
        {
            if (File.Exists(
                    Path.Combine(
                        candidate,
                        RuntimeAssetCatalog.ManifestFileName)))
            {
                return new RuntimeAssetPathResolution(
                    candidate,
                    candidates);
            }
        }

        return new RuntimeAssetPathResolution(
            null,
            candidates);
    }

    private static string[] BuildCandidateRoots(
        string applicationBaseDirectory,
        string currentDirectory,
        string? overrideRoot)
    {
        var roots =
            new List<string>();
        var seen =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        void Add(
            string? candidate,
            string relativeBase)
        {
            if (string.IsNullOrWhiteSpace(
                    candidate))
            {
                return;
            }

            string resolved =
                Path.GetFullPath(
                    Path.IsPathRooted(
                        candidate)
                        ? candidate
                        : Path.Combine(
                            relativeBase,
                            candidate));

            if (seen.Add(
                    resolved))
            {
                roots.Add(
                    resolved);
            }
        }

        Add(
            overrideRoot,
            currentDirectory);

        string? applicationRepositoryRoot =
            FindRepositoryRoot(
                applicationBaseDirectory);
        if (applicationRepositoryRoot is not null)
        {
            Add(
                Path.Combine(
                    applicationRepositoryRoot,
                    "assets",
                    "runtime"),
                currentDirectory);
        }

        Add(
            Path.Combine(
                applicationBaseDirectory,
                "assets",
                "runtime"),
            currentDirectory);
        Add(
            Path.Combine(
                currentDirectory,
                "assets",
                "runtime"),
            currentDirectory);

        string? currentRepositoryRoot =
            FindRepositoryRoot(
                currentDirectory);
        if (currentRepositoryRoot is not null)
        {
            Add(
                Path.Combine(
                    currentRepositoryRoot,
                    "assets",
                    "runtime"),
                currentDirectory);
        }

        return roots.ToArray();
    }

    internal static string? FindRepositoryRoot(
        string startDirectory)
    {
        DirectoryInfo? directory =
            new(
                Path.GetFullPath(
                    startDirectory));

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "ForgeLine.sln")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        return null;
    }
}
