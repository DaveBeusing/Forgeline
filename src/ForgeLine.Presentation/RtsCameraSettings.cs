using System.Numerics;

namespace ForgeLine.Presentation;

public sealed record RtsCameraSettings
{
    public Vector3 InitialTarget { get; init; } = Vector3.Zero;

    public float InitialYawRadians { get; init; }

    public float InitialPitchRadians { get; init; } = DegreesToRadians(-50.0f);

    public float InitialDistance { get; init; } = 90.0f;

    public float MinimumDistance { get; init; } = 12.0f;

    public float MaximumDistance { get; init; } = 260.0f;

    public float MinimumPitchRadians { get; init; } = DegreesToRadians(-80.0f);

    public float MaximumPitchRadians { get; init; } = DegreesToRadians(-25.0f);

    public float VerticalFieldOfViewRadians { get; init; } = DegreesToRadians(55.0f);

    public float NearPlane { get; init; } = 0.25f;

    public float FarPlane { get; init; } = 10_000.0f;

    public float BasePanSpeedUnitsPerSecond { get; init; } = 42.0f;

    public float PanReferenceDistance { get; init; } = 90.0f;

    public float MinimumPanSpeedScale { get; init; } = 0.35f;

    public float MaximumPanSpeedScale { get; init; } = 3.0f;

    public float RotationSpeedRadiansPerSecond { get; init; } = DegreesToRadians(90.0f);

    public float PitchSpeedRadiansPerSecond { get; init; } = DegreesToRadians(55.0f);

    public float ZoomFactorPerStep { get; init; } = 0.88f;

    public float DragPanUnitsPerPixelAtReferenceDistance { get; init; } = 0.11f;

    public bool EdgeScrollEnabled { get; init; } = true;

    public float EdgeScrollZonePixels { get; init; } = 18.0f;

    public float EdgeScrollSpeedMultiplier { get; init; } = 1.0f;

    public void Validate()
    {
        if (!IsFinite(InitialTarget))
        {
            throw new ArgumentOutOfRangeException(nameof(InitialTarget));
        }

        RequireFinite(InitialYawRadians, nameof(InitialYawRadians));
        RequireFinite(InitialPitchRadians, nameof(InitialPitchRadians));
        RequirePositive(InitialDistance, nameof(InitialDistance));
        RequirePositive(MinimumDistance, nameof(MinimumDistance));
        RequirePositive(MaximumDistance, nameof(MaximumDistance));

        if (MaximumDistance < MinimumDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumDistance),
                "Maximum camera distance must be greater than or equal to the minimum.");
        }

        RequireFinite(MinimumPitchRadians, nameof(MinimumPitchRadians));
        RequireFinite(MaximumPitchRadians, nameof(MaximumPitchRadians));

        if (MinimumPitchRadians >= MaximumPitchRadians ||
            MinimumPitchRadians <= DegreesToRadians(-89.0f) ||
            MaximumPitchRadians >= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumPitchRadians),
                "Pitch limits must stay between -89 and 0 degrees and preserve a non-empty range.");
        }

        if (VerticalFieldOfViewRadians <= 0.0f ||
            VerticalFieldOfViewRadians >= MathF.PI ||
            !float.IsFinite(VerticalFieldOfViewRadians))
        {
            throw new ArgumentOutOfRangeException(nameof(VerticalFieldOfViewRadians));
        }

        RequirePositive(NearPlane, nameof(NearPlane));
        RequirePositive(FarPlane, nameof(FarPlane));

        if (FarPlane <= NearPlane)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FarPlane),
                "The far plane must be farther than the near plane.");
        }

        RequirePositive(BasePanSpeedUnitsPerSecond, nameof(BasePanSpeedUnitsPerSecond));
        RequirePositive(PanReferenceDistance, nameof(PanReferenceDistance));
        RequirePositive(MinimumPanSpeedScale, nameof(MinimumPanSpeedScale));
        RequirePositive(MaximumPanSpeedScale, nameof(MaximumPanSpeedScale));

        if (MaximumPanSpeedScale < MinimumPanSpeedScale)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPanSpeedScale));
        }

        RequirePositive(RotationSpeedRadiansPerSecond, nameof(RotationSpeedRadiansPerSecond));
        RequirePositive(PitchSpeedRadiansPerSecond, nameof(PitchSpeedRadiansPerSecond));

        if (!float.IsFinite(ZoomFactorPerStep) ||
            ZoomFactorPerStep <= 0.0f ||
            ZoomFactorPerStep >= 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ZoomFactorPerStep),
                "Zoom factor must be greater than zero and less than one.");
        }

        RequirePositive(
            DragPanUnitsPerPixelAtReferenceDistance,
            nameof(DragPanUnitsPerPixelAtReferenceDistance));

        if (!float.IsFinite(EdgeScrollZonePixels) || EdgeScrollZonePixels < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeScrollZonePixels));
        }

        if (!float.IsFinite(EdgeScrollSpeedMultiplier) || EdgeScrollSpeedMultiplier < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeScrollSpeedMultiplier));
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static void RequireFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequirePositive(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180.0f);
}
