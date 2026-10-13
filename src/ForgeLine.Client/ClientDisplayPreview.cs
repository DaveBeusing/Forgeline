using System.Diagnostics;
using ForgeLine.Platform;

namespace ForgeLine.Client;

internal sealed class ClientDisplayPreview : IDisposable
{
    private readonly IWindow _window;
    private readonly Func<double> _seconds;
    private WindowConfiguration? _previous;
    private double _deadline;

    internal ClientDisplayPreview(IWindow window, Func<double>? seconds = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        long origin = Stopwatch.GetTimestamp();
        _seconds = seconds ?? (() => Stopwatch.GetElapsedTime(origin).TotalSeconds);
    }

    internal bool Active => _previous is not null;
    internal int RemainingSeconds => Active ? Math.Max(0, (int)Math.Ceiling(_deadline - _seconds())) : 0;

    internal void Begin(WindowConfiguration previous, WindowConfiguration candidate)
    {
        if (Active) throw new InvalidOperationException("A display preview is already active.");
        _previous = _window.Mode == WindowMode.Windowed && !_window.ClientSize.IsEmpty
            ? new(previous.Title, _window.ClientSize.Width, _window.ClientSize.Height, previous.Resizable, _window.Mode)
            : previous;
        _deadline = _seconds() + 15;
        try { _window.ConfigureDisplay(candidate); }
        catch { Cancel(); throw; }
    }

    internal bool Expire()
    {
        if (!Active || _seconds() < _deadline) return false;
        Cancel();
        return true;
    }

    internal void Keep() => _previous = null;

    internal void Cancel()
    {
        WindowConfiguration? previous = _previous;
        _previous = null;
        if (previous is null || !_window.IsOpen) return;
        try { _window.ConfigureDisplay(previous); }
        catch { _window.ConfigureDisplay(new(previous.Title, previous.Width, previous.Height, previous.Resizable, WindowMode.Windowed)); }
    }

    public void Dispose() => Cancel();
}
