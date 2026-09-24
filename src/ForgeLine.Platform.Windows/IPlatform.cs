using ForgeLine.Core;

namespace ForgeLine.Platform;

public interface IPlatform : IDisposable
{
    IClock Clock { get; }

    IWindow CreateWindow(WindowConfiguration configuration);

    bool PumpEvents();

    void WaitForEvents(TimeSpan maximumWait);
}
