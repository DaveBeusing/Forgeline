using ForgeLine.Platform;

namespace ForgeLine.Client;

internal sealed class ClientApplication
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmokeTestDuration = TimeSpan.FromMilliseconds(250);

    private readonly IPlatform _platform;

    internal ClientApplication(IPlatform platform)
    {
        _platform = platform;
    }

    internal int Run(bool smokeTest)
    {
        var configuration = new WindowConfiguration(
            "FORGELINE",
            1600,
            900,
            resizable: true,
            WindowMode.Windowed);

        using IWindow window = _platform.CreateWindow(configuration);
        long startedAt = _platform.Clock.GetTimestamp();

        WriteWindowState("started", window);

        while (window.IsOpen && _platform.PumpEvents())
        {
            DrainWindowEvents(window);

            if (smokeTest &&
                _platform.Clock.GetElapsedTime(startedAt, _platform.Clock.GetTimestamp()) >= SmokeTestDuration)
            {
                window.RequestClose();
            }

            if (window.IsOpen)
            {
                _platform.WaitForEvents(IdleWait);
            }
        }

        DrainWindowEvents(window);
        return 0;
    }

    private static void DrainWindowEvents(IWindow window)
    {
        while (window.TryDequeueEvent(out WindowEvent windowEvent))
        {
            Console.WriteLine(
                $"[platform:event] kind={windowEvent.Kind} " +
                $"size={windowEvent.ClientSize.Width}x{windowEvent.ClientSize.Height} " +
                $"dpi={windowEvent.Dpi} focused={windowEvent.IsFocused} " +
                $"minimized={windowEvent.IsMinimized} mode={windowEvent.Mode}");
        }
    }

    private static void WriteWindowState(string state, IWindow window)
    {
        Console.WriteLine(
            $"[platform:{state}] handle=0x{window.NativeHandle.Value:X} " +
            $"size={window.ClientSize.Width}x{window.ClientSize.Height} " +
            $"dpi={window.Dpi} focused={window.IsFocused} " +
            $"minimized={window.IsMinimized} mode={window.Mode}");
    }
}
