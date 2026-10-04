using ForgeLine.Platform;

namespace ForgeLine.Platform.Windows;

internal readonly record struct WindowSizeMessageTransition(
    bool ValidClientSizeObserved,
    bool ClientSizeChanged,
    bool BecameMinimized,
    bool Restored);

internal sealed class WindowLifecycleState
{
    private WindowSize _validClientSize;

    internal WindowMode CurrentMode { get; private set; } =
        WindowMode.Windowed;

    internal WindowMode RequestedMode { get; private set; } =
        WindowMode.Windowed;

    internal bool TransitionInProgress { get; private set; }

    internal WindowSize ValidClientSize => _validClientSize;

    internal bool HasValidClientSize { get; private set; }

    internal bool IsMinimized { get; private set; }

    internal ulong ResizeGeneration { get; private set; }

    internal void InitializeClientSize(
        in WindowSize clientSize)
    {
        if (HasValidClientSize)
        {
            throw new InvalidOperationException(
                "The initial client size has already been established.");
        }

        RequireValidClientSize(clientSize);
        _validClientSize = clientSize;
        HasValidClientSize = true;
    }

    internal bool BeginModeTransition(
        WindowMode requestedMode)
    {
        ValidateMode(requestedMode);

        if (TransitionInProgress)
        {
            throw new InvalidOperationException(
                "A window-mode transition is already in progress.");
        }

        RequestedMode = requestedMode;

        if (requestedMode == CurrentMode)
        {
            return false;
        }

        TransitionInProgress = true;
        return true;
    }

    internal void CompleteModeTransition(
        in WindowSize finalClientSize)
    {
        if (!TransitionInProgress)
        {
            throw new InvalidOperationException(
                "No window-mode transition is in progress.");
        }

        SetValidClientSize(finalClientSize);
        CurrentMode = RequestedMode;
        TransitionInProgress = false;
    }

    internal void AbortModeTransition()
    {
        RequestedMode = CurrentMode;
        TransitionInProgress = false;
    }

    internal WindowSizeMessageTransition ApplySizeMessage(
        int width,
        int height,
        bool minimized)
    {
        bool wasMinimized = IsMinimized;

        if (minimized)
        {
            IsMinimized = true;
            return new WindowSizeMessageTransition(
                ValidClientSizeObserved: false,
                ClientSizeChanged: false,
                BecameMinimized: !wasMinimized,
                Restored: false);
        }

        if (width <= 0 ||
            height <= 0)
        {
            return new WindowSizeMessageTransition(
                ValidClientSizeObserved: false,
                ClientSizeChanged: false,
                BecameMinimized: false,
                Restored: false);
        }

        IsMinimized = false;

        var clientSize =
            new WindowSize(
                width,
                height);
        bool changed =
            SetValidClientSize(
                clientSize);

        return new WindowSizeMessageTransition(
            ValidClientSizeObserved: true,
            ClientSizeChanged: changed,
            BecameMinimized: false,
            Restored: wasMinimized);
    }

    internal bool SetValidClientSize(
        in WindowSize clientSize)
    {
        RequireValidClientSize(clientSize);

        if (HasValidClientSize &&
            _validClientSize == clientSize)
        {
            return false;
        }

        _validClientSize = clientSize;
        HasValidClientSize = true;
        ResizeGeneration++;
        return true;
    }

    private static void RequireValidClientSize(
        in WindowSize clientSize)
    {
        if (clientSize.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clientSize),
                clientSize,
                "Window client dimensions must be non-zero.");
        }
    }

    private static void ValidateMode(
        WindowMode mode)
    {
        if (mode is not
            (WindowMode.Windowed or
             WindowMode.BorderlessFullscreen))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "Unsupported window mode.");
        }
    }
}
