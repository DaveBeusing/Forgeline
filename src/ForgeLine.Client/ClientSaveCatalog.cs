using ForgeLine.Game;

namespace ForgeLine.Client;

internal static class ClientSaveCatalog
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MatchRuntime, PlayerControlGroupSaveData> s_groups = new();
    internal static PlayerControlGroupSaveData? TakeControlGroups(MatchRuntime runtime)
    {
        if (!s_groups.TryGetValue(runtime, out var data)) return null;
        s_groups.Remove(runtime);
        return data;
    }

    internal static IReadOnlyList<ForgeLine.UI.LoadGameEntry> Discover(
        string directory) => Discover(directory, CancellationToken.None);

    internal static IReadOnlyList<ForgeLine.UI.LoadGameEntry> Discover(
            string directory,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        cancellationToken.ThrowIfCancellationRequested();

        string fullDirectory =
            Path.GetFullPath(directory);

        if (!Directory.Exists(fullDirectory))
        {
            return [];
        }

        string[] paths =
            Directory.GetFiles(
                fullDirectory,
                "*.save.json",
                SearchOption.TopDirectoryOnly);
        Array.Sort(
            paths,
            StringComparer.OrdinalIgnoreCase);

        var entries =
            new List<ForgeLine.UI.LoadGameEntry>(
                paths.Length);

        for (int index = 0; index < paths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = paths[index];
            string id =
                Path.GetFileName(path);

            try
            {
                MatchSaveData save =
                    MatchPersistenceSerializer.ReadSave(path);
                entries.Add(
                    new ForgeLine.UI.LoadGameEntry(
                        id,
                        Path.GetFileNameWithoutExtension(
                            Path.GetFileNameWithoutExtension(path)),
                        path,
                        save.SavedTick,
                        ForgeLine.UI.LoadGameEntryState.Available));
            }
            catch (MatchPersistenceException exception)
            {
                ForgeLine.UI.LoadGameEntryState state =
                    exception.Reason ==
                        MatchPersistenceFailureReason.IncompatibleVersion
                        ? ForgeLine.UI.LoadGameEntryState.Incompatible
                        : ForgeLine.UI.LoadGameEntryState.Corrupt;

                entries.Add(
                    new ForgeLine.UI.LoadGameEntry(
                        id,
                        Path.GetFileName(path),
                        path,
                        0,
                        state,
                        exception.Message));
            }
            catch (IOException exception)
            {
                entries.Add(
                    new ForgeLine.UI.LoadGameEntry(
                        id,
                        Path.GetFileName(path),
                        path,
                        0,
                        ForgeLine.UI.LoadGameEntryState.Corrupt,
                        exception.Message));
            }
            catch (UnauthorizedAccessException exception)
            {
                entries.Add(
                    new ForgeLine.UI.LoadGameEntry(
                        id,
                        Path.GetFileName(path),
                        path,
                        0,
                        ForgeLine.UI.LoadGameEntryState.Corrupt,
                        exception.Message));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return entries.AsReadOnly();
    }

    internal static MatchRuntime Restore(
        ForgeLine.UI.LoadGameEntry entry,
        Func<string, MatchComposition>? resolveComposition = null) =>
        RestoreCancellable(entry, CancellationToken.None, resolveComposition: resolveComposition);

    internal static MatchRuntime RestoreCancellable(
        ForgeLine.UI.LoadGameEntry entry,
        CancellationToken cancellationToken,
        Action<MatchRestorationProgress>? reportProgress = null,
        Func<string, MatchComposition>? resolveComposition = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!entry.CanLoad)
        {
            throw new InvalidOperationException(
                "Only an available save can be restored.");
        }

        MatchSaveData save =
            MatchPersistenceSerializer.ReadSave(
                entry.Path);
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = MatchPersistenceService.RestoreCancellable(
            save, cancellationToken, reportProgress, resolveComposition);
        if (save.ControlGroups is { } groups) s_groups.Add(runtime, groups);
        return runtime;
    }
}
