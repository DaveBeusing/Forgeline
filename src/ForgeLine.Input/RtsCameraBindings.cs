using ForgeLine.Platform;

namespace ForgeLine.Input;

public sealed record RtsCameraBindings
{
    public RtsCameraBindings WithoutRetiredFunctionKeys()
    {
        PlatformKey[] keys = [PanForward, PanForwardAlternate, PanBackward, PanBackwardAlternate,
            PanLeft, PanLeftAlternate, PanRight, PanRightAlternate, RotateLeft, RotateRight, PitchUp, PitchDown];
        if (!keys.Any(GameplayBindingRegistry.IsRetiredFunctionKey)) return this;
        PlatformKey[] defaults = [PlatformKey.W, PlatformKey.Up, PlatformKey.S, PlatformKey.Down,
            PlatformKey.A, PlatformKey.Left, PlatformKey.D, PlatformKey.Right, PlatformKey.Q, PlatformKey.E, PlatformKey.R, PlatformKey.F];
        var used = keys.Where(k => !GameplayBindingRegistry.IsRetiredFunctionKey(k)).ToHashSet();
        for (int i = 0; i < keys.Length; i++)
            if (GameplayBindingRegistry.IsRetiredFunctionKey(keys[i]))
            {
                keys[i] = !used.Contains(defaults[i]) ? defaults[i] : defaults.First(k => !used.Contains(k));
                used.Add(keys[i]);
            }
        return this with
        {
            PanForward = keys[0], PanForwardAlternate = keys[1], PanBackward = keys[2], PanBackwardAlternate = keys[3],
            PanLeft = keys[4], PanLeftAlternate = keys[5], PanRight = keys[6], PanRightAlternate = keys[7],
            RotateLeft = keys[8], RotateRight = keys[9], PitchUp = keys[10], PitchDown = keys[11]
        };
    }

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
