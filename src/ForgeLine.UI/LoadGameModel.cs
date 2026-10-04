namespace ForgeLine.UI;

public enum LoadGameEntryState : byte
{
    Available = 1,
    Corrupt = 2,
    Incompatible = 3
}

public readonly record struct LoadGameEntry(
    string Id,
    string DisplayName,
    string Path,
    ulong SavedTick,
    LoadGameEntryState State,
    string? Detail = null)
{
    public bool CanLoad =>
        State == LoadGameEntryState.Available;
}

public sealed class LoadGameModel
{
    private int _focusedIndex;
    private readonly LoadGameEntry[] _entries;

    public LoadGameModel(IEnumerable<LoadGameEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToArray();
    }

    public IReadOnlyList<LoadGameEntry> Entries => _entries;

    public bool HasSaves => _entries.Length > 0;

    public LoadGameEntry? ContinueTarget =>
        _entries
            .Where(static entry => entry.CanLoad)
            .OrderByDescending(static entry => entry.SavedTick)
            .Cast<LoadGameEntry?>()
            .FirstOrDefault();

    public bool CanContinue =>
        ContinueTarget.HasValue;

    public bool TryGetLoadTarget(
        string id,
        out LoadGameEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        for (int index = 0; index < _entries.Length; index++)
        {
            LoadGameEntry candidate = _entries[index];

            if (candidate.CanLoad &&
                string.Equals(
                    candidate.Id,
                    id,
                    StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        entry = default;
        return false;
    }

    public static GameFrontendAction Back() =>
        GameFrontendAction.Back;
}
