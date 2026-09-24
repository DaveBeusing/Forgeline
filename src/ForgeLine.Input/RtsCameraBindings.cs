using ForgeLine.Platform;

namespace ForgeLine.Input;

public sealed record RtsCameraBindings
{
    public PlatformKey PanForward { get; init; } = PlatformKey.W;

    public PlatformKey PanForwardAlternate { get; init; } = PlatformKey.Up;

    public PlatformKey PanBackward { get; init; } = PlatformKey.S;

    public PlatformKey PanBackwardAlternate { get; init; } = PlatformKey.Down;

    public PlatformKey PanLeft { get; init; } = PlatformKey.A;

    public PlatformKey PanLeftAlternate { get; init; } = PlatformKey.Left;

    public PlatformKey PanRight { get; init; } = PlatformKey.D;

    public PlatformKey PanRightAlternate { get; init; } = PlatformKey.Right;

    public PlatformKey RotateLeft { get; init; } = PlatformKey.Q;

    public PlatformKey RotateRight { get; init; } = PlatformKey.E;

    public PlatformKey PitchUp { get; init; } = PlatformKey.R;

    public PlatformKey PitchDown { get; init; } = PlatformKey.F;

    public PlatformMouseButton DragPanButton { get; init; } = PlatformMouseButton.Middle;
}
