using System.Numerics;

namespace ForgeLine.Presentation;

public enum RtsReferenceZoom
{
    CloseTactical,
    NormalGameplay,
    Strategic
}

public readonly record struct RtsReferenceDisplay(int Width, int Height);

public static class RtsVisualReference
{
    public const int Width = 2560;
    public const int Height = 1440;
    public const float YawRadians = MathF.PI / 4.0f;
    public const float PitchRadians = -55.0f * MathF.PI / 180.0f;
    public const float VerticalFieldOfViewRadians = MathF.PI / 4.0f;
    public const float CloseDistance = 120.0f;
    public const float NormalDistance = 420.0f;
    public const float StrategicDistance = 1000.0f;

    public static IReadOnlyList<RtsReferenceDisplay> Displays { get; } =
        Array.AsReadOnly<RtsReferenceDisplay>([
            new(1920, 1080), new(Width, Height), new(3840, 2160), new(3440, 1440)]);

    public static RtsCameraSettings CreateCameraSettings(Vector3 target, RtsReferenceZoom zoom = RtsReferenceZoom.NormalGameplay) =>
        new()
        {
            InitialTarget = target,
            InitialYawRadians = YawRadians,
            InitialPitchRadians = PitchRadians,
            VerticalFieldOfViewRadians = VerticalFieldOfViewRadians,
            InitialDistance = GetDistance(zoom),
            MinimumDistance = 20.0f,
            MaximumDistance = 1200.0f,
            PanReferenceDistance = 180.0f,
            MaximumPanSpeedScale = 5.0f
        };

    public static float GetDistance(RtsReferenceZoom zoom) => zoom switch
    {
        RtsReferenceZoom.CloseTactical => CloseDistance,
        RtsReferenceZoom.NormalGameplay => NormalDistance,
        RtsReferenceZoom.Strategic => StrategicDistance,
        _ => throw new ArgumentOutOfRangeException(nameof(zoom))
    };
}
