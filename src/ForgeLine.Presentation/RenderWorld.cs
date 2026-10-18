using ForgeLine.Core;

namespace ForgeLine.Presentation;

public sealed class RenderWorld
{
    private PresentationSnapshot? _previous;
    private PresentationSnapshot? _current;
    private readonly RenderSnapshotIndex _index = new(false);
    private readonly InstanceVisibilityIndex _visibility = new();
    internal bool ClusterVisible(int firstIndex, in ViewFrustum frustum) => _visibility.IsVisible(firstIndex, frustum);
    internal long IndexRetainedBytes => _index.RetainedBytes + _visibility.RetainedBytes;

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

        if (_current is not null &&
            latest.SessionId == _current.SessionId &&
            latest.Tick < _current.Tick)
        {
            return false;
        }

        if (_current is not null &&
            latest.SessionId != _current.SessionId)
        {
            _previous = null;
            _current = latest;
            _index.Rebuild(latest, _previous);
        _visibility.Rebuild(latest, _previous, _index);
            return true;
        }

        _previous = _current;
        _current = latest;
        _index.Rebuild(latest, _previous);
        _visibility.Rebuild(latest, _previous, _index);
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

        if (currentInstance.VfxFeature.IsSpecified || currentInstance.DecalFeature.IsSpecified && currentInstance.DecalFeature.Surface == DecalSurface.Ground)
        {
            return currentInstance;
        }

        int previousIndex = _index.PreviousIndex(index);
        if (_previous is null || previousIndex < 0)
        {
            return currentInstance;
        }

        RenderInstance previousInstance = _previous.GetInstance(previousIndex);
        return currentInstance with
        {
            Transform = RenderTransform.Interpolate(
                previousInstance.Transform,
                currentInstance.Transform,
                alpha),
            MechanicalPose = MechanicalPose.Interpolate(previousInstance.MechanicalPose, currentInstance.MechanicalPose, alpha),
            UnitFeature = currentInstance.UnitFeature with { AimYawRadians = InterpolateYaw(previousInstance.UnitFeature.AimYawRadians, currentInstance.UnitFeature.AimYawRadians, alpha) }
        };
    }

    public bool TryGetInterpolatedInstance(
        EntityId entity,
        float alpha,
        out RenderInstance instance)
    {
        if (_current is null ||
            !TryFindByEntity(_current, entity, out RenderInstance current))
        {
            instance = default;
            return false;
        }

        if (current.VfxFeature.IsSpecified || current.DecalFeature.IsSpecified && current.DecalFeature.Surface == DecalSurface.Ground ||
            _previous is null ||
            !TryFindByEntity(
                _previous,
                entity,
                out RenderInstance previous))
        {
            instance = current;
            return true;
        }

        instance = current with
        {
            Transform = RenderTransform.Interpolate(
                previous.Transform,
                current.Transform,
                alpha),
            MechanicalPose = MechanicalPose.Interpolate(previous.MechanicalPose, current.MechanicalPose, alpha),
            UnitFeature = current.UnitFeature with { AimYawRadians = InterpolateYaw(previous.UnitFeature.AimYawRadians, current.UnitFeature.AimYawRadians, alpha) }
        };
        return true;
    }

    private static float InterpolateYaw(float previous, float current, float alpha) =>
        previous + MathF.IEEERemainder(current - previous, MathF.Tau) * (float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1);

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
