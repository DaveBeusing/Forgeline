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

    public LoadGameEntry? FocusedEntry =>
        _entries.Length == 0
            ? null
            : _entries[_focusedIndex];

    public LoadGameEntry? Focus(int index)
    {
        if ((uint)index >= (uint)_entries.Length)
        {
            return null;
        }

        _focusedIndex = index;
        return FocusedEntry;
    }

    public LoadGameEntry? MoveNext()
    {
        if (_entries.Length == 0)
        {
            return null;
        }

        _focusedIndex =
            (_focusedIndex + 1) %
            _entries.Length;
        return FocusedEntry;
    }

    public LoadGameEntry? MovePrevious()
    {
        if (_entries.Length == 0)
        {
            return null;
        }

        _focusedIndex =
            (_focusedIndex - 1 + _entries.Length) %
            _entries.Length;
        return FocusedEntry;
    }

    public bool TryGetFocusedLoadTarget(
        out LoadGameEntry entry)
    {
        if (FocusedEntry is LoadGameEntry focused &&
            focused.CanLoad)
        {
            entry = focused;
            return true;
        }

        entry = default;
        return false;
    }

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
