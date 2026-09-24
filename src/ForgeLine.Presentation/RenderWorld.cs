using ForgeLine.Core;

namespace ForgeLine.Presentation;

public sealed class RenderWorld
{
    private PresentationSnapshot? _previous;
    private PresentationSnapshot? _current;

    public PresentationSnapshot? CurrentSnapshot => _current;

    public PresentationSnapshot? PreviousSnapshot => _previous;

    public int InstanceCount => _current?.InstanceCount ?? 0;

    public bool Update(PresentationSnapshotBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        if (!buffer.TryReadLatest(out PresentationSnapshot latest))
        {
            return false;
        }

        if (ReferenceEquals(_current, latest))
        {
            return false;
        }

        if (_current is not null && latest.Tick < _current.Tick)
        {
            return false;
        }

        _previous = _current;
        _current = latest;
        return true;
    }

    public RenderInstance GetInterpolatedInstance(
        int index,
        float alpha)
    {
        PresentationSnapshot current = _current ??
            throw new InvalidOperationException(
                "No presentation snapshot has been received.");

        if ((uint)index >= (uint)current.InstanceCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        RenderInstance currentInstance = current.GetInstance(index);
        if (_previous is null ||
            !TryFindByEntity(
                _previous,
                currentInstance.Entity,
                out RenderInstance previousInstance))
        {
            return currentInstance;
        }

        return currentInstance with
        {
            Transform = RenderTransform.Interpolate(
                previousInstance.Transform,
                currentInstance.Transform,
                alpha)
        };
    }

    private static bool TryFindByEntity(
        PresentationSnapshot snapshot,
        EntityId entity,
        out RenderInstance instance)
    {
        int low = 0;
        int high = snapshot.InstanceCount - 1;

        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            RenderInstance candidate = snapshot.GetInstance(middle);
            int comparison = candidate.Entity.CompareTo(entity);

            if (comparison == 0)
            {
                instance = candidate;
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        instance = default;
        return false;
    }
}
