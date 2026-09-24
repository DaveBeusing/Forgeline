using System.Numerics;
using ForgeLine.Input;

namespace ForgeLine.Presentation;

public sealed class RtsCamera
{
    private const float RadiansToDegrees = 180.0f / MathF.PI;

    private readonly RtsCameraSettings _settings;

    private bool _hasPointerPosition;
    private Vector2 _pointerPosition;

    public RtsCamera(RtsCameraSettings? settings = null)
    {
        _settings = settings ?? new RtsCameraSettings();
        _settings.Validate();

        Target = _settings.InitialTarget;
        YawRadians = NormalizeAngle(_settings.InitialYawRadians);
        PitchRadians = Math.Clamp(
            _settings.InitialPitchRadians,
            _settings.MinimumPitchRadians,
            _settings.MaximumPitchRadians);
        Distance = Math.Clamp(
            _settings.InitialDistance,
            _settings.MinimumDistance,
            _settings.MaximumDistance);
    }

    public RtsCameraSettings Settings => _settings;

    public Vector3 Target { get; private set; }

    public float YawRadians { get; private set; }

    public float PitchRadians { get; private set; }

    public float Distance { get; private set; }

    public Vector3 Position => Target - GetViewForward() * Distance;

    public Vector3 GroundForward =>
        Vector3.Normalize(new Vector3(MathF.Sin(YawRadians), 0.0f, MathF.Cos(YawRadians)));

    public Vector3 GroundRight =>
        Vector3.Normalize(new Vector3(MathF.Cos(YawRadians), 0.0f, -MathF.Sin(YawRadians)));

    public void Update(
        RtsCameraInputFrame input,
        float deltaSeconds,
        int viewportWidth,
        int viewportHeight)
    {
        ValidateViewport(viewportWidth, viewportHeight);

        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        }

        if (input.HasPointerPosition)
        {
            _hasPointerPosition = true;
            _pointerPosition = input.PointerPosition;
        }

        if (input.Rotation != 0.0f && deltaSeconds > 0.0f)
        {
            YawRadians = NormalizeAngle(
                YawRadians +
                input.Rotation *
                _settings.RotationSpeedRadiansPerSecond *
                deltaSeconds);
        }

        if (input.Pitch != 0.0f && deltaSeconds > 0.0f)
        {
            PitchRadians = Math.Clamp(
                PitchRadians +
                input.Pitch *
                _settings.PitchSpeedRadiansPerSecond *
                deltaSeconds,
                _settings.MinimumPitchRadians,
                _settings.MaximumPitchRadians);
        }

        if (input.ZoomSteps != 0.0f)
        {
            if (!float.IsFinite(input.ZoomSteps))
            {
                throw new ArgumentOutOfRangeException(nameof(input));
            }

            float zoomedDistance =
                Distance * MathF.Pow(_settings.ZoomFactorPerStep, input.ZoomSteps);
            Distance = Math.Clamp(
                zoomedDistance,
                _settings.MinimumDistance,
                _settings.MaximumDistance);
        }

        Vector2 pan = input.Pan;
        if (_settings.EdgeScrollEnabled &&
            input.HasPointerPosition &&
            IsInsideViewport(input.PointerPosition, viewportWidth, viewportHeight))
        {
            pan += GetEdgeScroll(
                input.PointerPosition,
                viewportWidth,
                viewportHeight) *
                _settings.EdgeScrollSpeedMultiplier;
        }

        if (pan != Vector2.Zero && deltaSeconds > 0.0f)
        {
            if (pan.LengthSquared() > 1.0f)
            {
                pan = Vector2.Normalize(pan);
            }

            float panSpeed = _settings.BasePanSpeedUnitsPerSecond * GetZoomScale();
            Target +=
                (GroundRight * pan.X + GroundForward * pan.Y) *
                panSpeed *
                deltaSeconds;
        }

        if (input.DragPan && input.PointerDelta != Vector2.Zero)
        {
            float dragScale =
                _settings.DragPanUnitsPerPixelAtReferenceDistance *
                GetZoomScale();

            Target +=
                -GroundRight * input.PointerDelta.X * dragScale +
                GroundForward * input.PointerDelta.Y * dragScale;
        }
    }

    public CameraMatrices GetMatrices(int viewportWidth, int viewportHeight)
    {
        ValidateViewport(viewportWidth, viewportHeight);

        Vector3 position = Position;
        Matrix4x4 view = Matrix4x4.CreateLookAt(position, Target, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            _settings.VerticalFieldOfViewRadians,
            viewportWidth / (float)viewportHeight,
            _settings.NearPlane,
            _settings.FarPlane);

        return new CameraMatrices(view, projection, view * projection);
    }

    public CameraRay ScreenPointToWorldRay(
        Vector2 screenPoint,
        int viewportWidth,
        int viewportHeight)
    {
        CameraMatrices matrices = GetMatrices(viewportWidth, viewportHeight);

        if (!Matrix4x4.Invert(matrices.ViewProjection, out Matrix4x4 inverse))
        {
            throw new InvalidOperationException("The RTS camera view-projection matrix is not invertible.");
        }

        float x = (2.0f * screenPoint.X / viewportWidth) - 1.0f;
        float y = 1.0f - (2.0f * screenPoint.Y / viewportHeight);

        Vector3 nearPoint = Unproject(new Vector3(x, y, 0.0f), inverse);
        Vector3 farPoint = Unproject(new Vector3(x, y, 1.0f), inverse);
        Vector3 direction = Vector3.Normalize(farPoint - nearPoint);

        return new CameraRay(Position, direction);
    }

    public bool TryScreenPointToWorldOnHorizontalPlane(
        Vector2 screenPoint,
        float worldY,
        int viewportWidth,
        int viewportHeight,
        out Vector3 worldPoint)
    {
        if (!float.IsFinite(worldY))
        {
            throw new ArgumentOutOfRangeException(nameof(worldY));
        }

        CameraRay ray = ScreenPointToWorldRay(
            screenPoint,
            viewportWidth,
            viewportHeight);

        if (MathF.Abs(ray.Direction.Y) <= 1e-6f)
        {
            worldPoint = default;
            return false;
        }

        float distance = (worldY - ray.Origin.Y) / ray.Direction.Y;
        if (!float.IsFinite(distance) || distance < 0.0f)
        {
            worldPoint = default;
            return false;
        }

        worldPoint = ray.Origin + ray.Direction * distance;
        return true;
    }

    public ScreenProjection WorldToScreen(
        Vector3 worldPoint,
        int viewportWidth,
        int viewportHeight)
    {
        CameraMatrices matrices = GetMatrices(viewportWidth, viewportHeight);
        Vector4 clip = Vector4.Transform(new Vector4(worldPoint, 1.0f), matrices.ViewProjection);

        if (!float.IsFinite(clip.W) || clip.W <= 0.0f)
        {
            return new ScreenProjection(Vector2.Zero, float.NaN, false);
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        var screen = new Vector2(
            (ndc.X + 1.0f) * 0.5f * viewportWidth,
            (1.0f - ndc.Y) * 0.5f * viewportHeight);

        bool visible =
            ndc.X >= -1.0f && ndc.X <= 1.0f &&
            ndc.Y >= -1.0f && ndc.Y <= 1.0f &&
            ndc.Z >= 0.0f && ndc.Z <= 1.0f;

        return new ScreenProjection(screen, ndc.Z, visible);
    }

    public RtsCameraDiagnostics GetDiagnostics() =>
        new(
            Position,
            Target,
            YawRadians * RadiansToDegrees,
            PitchRadians * RadiansToDegrees,
            Distance,
            _hasPointerPosition,
            _pointerPosition);

    private Vector3 GetViewForward()
    {
        float cosPitch = MathF.Cos(PitchRadians);
        return Vector3.Normalize(
            new Vector3(
                MathF.Sin(YawRadians) * cosPitch,
                MathF.Sin(PitchRadians),
                MathF.Cos(YawRadians) * cosPitch));
    }

    private float GetZoomScale() =>
        Math.Clamp(
            Distance / _settings.PanReferenceDistance,
            _settings.MinimumPanSpeedScale,
            _settings.MaximumPanSpeedScale);

    private Vector2 GetEdgeScroll(
        Vector2 pointer,
        int viewportWidth,
        int viewportHeight)
    {
        float zone = Math.Min(
            _settings.EdgeScrollZonePixels,
            MathF.Min(viewportWidth, viewportHeight) * 0.5f);

        if (zone <= 0.0f)
        {
            return Vector2.Zero;
        }

        float x = 0.0f;
        float y = 0.0f;

        if (pointer.X < zone)
        {
            x = -1.0f + (pointer.X / zone);
        }
        else if (pointer.X > viewportWidth - zone)
        {
            x = (pointer.X - (viewportWidth - zone)) / zone;
        }

        if (pointer.Y < zone)
        {
            y = 1.0f - (pointer.Y / zone);
        }
        else if (pointer.Y > viewportHeight - zone)
        {
            y = -((pointer.Y - (viewportHeight - zone)) / zone);
        }

        return new Vector2(x, y);
    }

    private static Vector3 Unproject(Vector3 normalizedDevicePoint, Matrix4x4 inverse)
    {
        Vector4 world = Vector4.Transform(
            new Vector4(normalizedDevicePoint, 1.0f),
            inverse);

        if (!float.IsFinite(world.W) || MathF.Abs(world.W) <= float.Epsilon)
        {
            throw new InvalidOperationException("The RTS camera projection produced an invalid homogeneous point.");
        }

        return new Vector3(world.X, world.Y, world.Z) / world.W;
    }

    private static bool IsInsideViewport(Vector2 point, int width, int height) =>
        point.X >= 0.0f &&
        point.Y >= 0.0f &&
        point.X < width &&
        point.Y < height;

    private static void ValidateViewport(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
    }

    private static float NormalizeAngle(float radians)
    {
        float normalized = radians % MathF.Tau;

        if (normalized < -MathF.PI)
        {
            normalized += MathF.Tau;
        }
        else if (normalized > MathF.PI)
        {
            normalized -= MathF.Tau;
        }

        return normalized;
    }
}
