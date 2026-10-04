namespace ForgeLine.Graphics;

internal readonly record struct GraphicsResizeRequest(
    int Width,
    int Height,
    ulong Generation);

internal sealed class GraphicsSurfaceLifecycleState
{
    private int _pendingWidth;
    private int _pendingHeight;

    internal GraphicsSurfaceLifecycleState(
        int width,
        int height,
        bool suspended)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
        IsSuspended = suspended;
    }

    internal int Width { get; private set; }

    internal int Height { get; private set; }

    internal bool IsSuspended { get; private set; }

    internal bool IsOccluded { get; private set; }

    internal bool HasPendingResize { get; private set; }

    internal ulong ResizeGeneration { get; private set; }

    internal ulong AppliedResizeGeneration { get; private set; }

    internal bool RequestResize(
        int width,
        int height)
    {
        if (width <= 0 ||
            height <= 0)
        {
            bool changed =
                !IsSuspended ||
                HasPendingResize;

            IsSuspended = true;
            HasPendingResize = false;
            _pendingWidth = 0;
            _pendingHeight = 0;

            if (changed)
            {
                ResizeGeneration++;
            }

            return changed;
        }

        if (!IsSuspended &&
            !HasPendingResize &&
            width == Width &&
            height == Height)
        {
            return false;
        }

        if (HasPendingResize &&
            _pendingWidth == width &&
            _pendingHeight == height &&
            !IsSuspended)
        {
            return false;
        }

        _pendingWidth = width;
        _pendingHeight = height;
        HasPendingResize = true;
        IsSuspended = false;
        ResizeGeneration++;
        return true;
    }

    internal bool TryGetPendingResize(
        out GraphicsResizeRequest request)
    {
        if (!HasPendingResize)
        {
            request = default;
            return false;
        }

        request =
            new GraphicsResizeRequest(
                _pendingWidth,
                _pendingHeight,
                ResizeGeneration);
        return true;
    }

    internal void CompleteResize(
        in GraphicsResizeRequest request)
    {
        if (!HasPendingResize)
        {
            throw new InvalidOperationException(
                "No graphics resize is pending.");
        }

        if (request.Generation != ResizeGeneration ||
            request.Width != _pendingWidth ||
            request.Height != _pendingHeight)
        {
            throw new InvalidOperationException(
                "The graphics resize request is stale.");
        }

        Width = request.Width;
        Height = request.Height;
        HasPendingResize = false;
        _pendingWidth = 0;
        _pendingHeight = 0;
        IsSuspended = false;
        IsOccluded = false;
        AppliedResizeGeneration =
            request.Generation;
    }

    internal bool MarkOccluded()
    {
        if (IsOccluded)
        {
            return false;
        }

        IsOccluded = true;
        return true;
    }

    internal bool MarkPresentable()
    {
        if (!IsOccluded)
        {
            return false;
        }

        IsOccluded = false;
        return true;
    }
}
