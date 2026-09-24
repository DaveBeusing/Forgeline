namespace ForgeLine.Presentation;

public sealed class PresentationSnapshotBuffer
{
    private PresentationSnapshot? _latest;

    public void Publish(PresentationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _latest, snapshot);
    }

    public bool TryReadLatest(out PresentationSnapshot snapshot)
    {
        PresentationSnapshot? latest = Volatile.Read(ref _latest);
        if (latest is null)
        {
            snapshot = null!;
            return false;
        }

        snapshot = latest;
        return true;
    }
}
