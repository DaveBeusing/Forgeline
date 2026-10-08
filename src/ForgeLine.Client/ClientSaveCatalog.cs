using ForgeLine.Game;

namespace ForgeLine.Client;

internal static class ClientSaveCatalog
{
    internal static IReadOnlyList<ForgeLine.UI.LoadGameEntry> Discover(
        string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

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

        return entries;
    }

    internal static MatchRuntime Restore(
        ForgeLine.UI.LoadGameEntry entry,
        Func<string, MatchComposition>? resolveComposition = null)
    {
        if (!entry.CanLoad)
        {
            throw new InvalidOperationException(
                "Only an available save can be restored.");
        }

        MatchSaveData save =
            MatchPersistenceSerializer.ReadSave(
                entry.Path);
        return MatchPersistenceService.Restore(
            save, resolveComposition);
    }
}
