using System.Numerics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class RtsMinimapInteractionLayout
{
    public static float SelectorHeight(
        in GameplayHudLayout layout) =>
        MathF.Min(
            layout.Minimap.Height,
            24.0f *
            layout.Scale);

    public static float LegendHeight(
        in GameplayHudLayout layout) =>
        MathF.Min(
            layout.Minimap.Height,
            18.0f *
            layout.Scale);

    public static HudRect GetSelectorRect(
        in GameplayHudLayout layout) =>
        new(
            layout.Minimap.X,
            layout.Minimap.Y,
            layout.Minimap.Width,
            SelectorHeight(
                layout));

    public static HudRect GetLegendRect(
        in GameplayHudLayout layout)
    {
        float height =
            LegendHeight(
                layout);

        return new HudRect(
            layout.Minimap.X,
            layout.Minimap.Bottom -
                height,
            layout.Minimap.Width,
            height);
    }

    public static HudRect GetMapRect(
        in GameplayHudLayout layout)
    {
        float gap =
            3.0f *
            layout.Scale;
        HudRect selector =
            GetSelectorRect(
                layout);
        HudRect legend =
            GetLegendRect(
                layout);
        float top =
            MathF.Min(
                layout.Minimap.Bottom,
                selector.Bottom +
                    gap);
        float bottom =
            MathF.Max(
                top,
                legend.Y -
                    gap);

        return new HudRect(
            layout.Minimap.X,
            top,
            layout.Minimap.Width,
            MathF.Max(
                0.0f,
                bottom -
                    top));
    }

    public static bool TryMapPointerToWorld(
        Vector2 pointer,
        in GameplayHudLayout layout,
        in AxisAlignedBounds worldBounds,
        out Vector3 worldTarget)
    {
        HudRect map =
            GetMapRect(
                layout);

        if (!map.Contains(
                pointer))
        {
            worldTarget = default;
            return false;
        }

        float worldWidth =
            worldBounds.Maximum.X -
            worldBounds.Minimum.X;
        float worldDepth =
            worldBounds.Maximum.Z -
            worldBounds.Minimum.Z;

        if (!float.IsFinite(
                worldWidth) ||
            !float.IsFinite(
                worldDepth) ||
            worldWidth <= 0.0f ||
            worldDepth <= 0.0f)
        {
            worldTarget = default;
            return false;
        }

        float normalizedX =
            Math.Clamp(
                (pointer.X -
                 map.X) /
                map.Width,
                0.0f,
                1.0f);
        float normalizedZ =
            Math.Clamp(
                1.0f -
                (pointer.Y -
                 map.Y) /
                map.Height,
                0.0f,
                1.0f);

        worldTarget =
            new Vector3(
                worldBounds.Minimum.X +
                    normalizedX *
                    worldWidth,
                0.0f,
                worldBounds.Minimum.Z +
                    normalizedZ *
                    worldDepth);
        return true;
    }

    public static Vector2 MapWorldToPointer(
        Vector3 worldPosition,
        in GameplayHudLayout layout,
        in AxisAlignedBounds worldBounds)
    {
        HudRect map =
            GetMapRect(
                layout);
        Vector2 normalized =
            RtsMinimapModelBuilder.NormalizeWorldPosition(
                worldPosition,
                worldBounds);

        return new Vector2(
            map.X +
                normalized.X *
                map.Width,
            map.Y +
                (1.0f -
                 normalized.Y) *
                map.Height);
    }
}

internal readonly record struct RtsMinimapInteractionView(
    bool PointerCaptured,
    bool IsCameraDragging,
    bool PointerWorldValid,
    Vector3 PointerWorldTarget,
    RtsCursorKind Cursor)
{
    public static RtsMinimapInteractionView Empty =>
        new(
            false,
            false,
            false,
            default,
            RtsCursorKind.Default);
}

internal sealed class RtsMinimapInteractionController
{
    private SimulationSessionId _sessionId;
    private bool _primaryWasDown;
    private bool _cameraDragging;

    public bool PointerCaptured { get; private set; }

    public RtsMinimapInteractionView View { get; private set; } =
        RtsMinimapInteractionView.Empty;

    public void UpdateCamera(
        InputState input,
        RtsCamera camera,
        ITerrainQuery terrain,
        PresentationSnapshot? snapshot,
        in GameplayHudLayout layout,
        bool inputBlocked = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(terrain);

        SynchronizeSession(
            snapshot?.SessionId ??
            SimulationSessionId.None);

        bool terminal =
            snapshot?.PlayerExperience?.IsMatchComplete ==
            true;
        bool primaryDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);
        bool primaryPressed =
            primaryDown &&
            !_primaryWasDown;
        bool pointerInHud =
            input.HasPointerPosition &&
            layout.Minimap.Contains(
                input.PointerPosition);
        bool pointerInMap =
            input.HasPointerPosition &&
            RtsMinimapInteractionLayout.GetMapRect(
                layout).Contains(
                input.PointerPosition);

        PointerCaptured =
            pointerInHud ||
            _cameraDragging;

        bool hasWorldTarget =
            pointerInMap &&
            RtsMinimapInteractionLayout.TryMapPointerToWorld(
                input.PointerPosition,
                layout,
                terrain.WorldBounds,
                out Vector3 flatTarget) &&
            terrain.TrySampleHeight(
                flatTarget.X,
                flatTarget.Z,
                out float terrainHeight);
        Vector3 worldTarget =
            hasWorldTarget
                ? new Vector3(
                    flatTarget.X,
                    terrainHeight,
                    flatTarget.Z)
                : default;

        if (inputBlocked ||
            terminal)
        {
            _cameraDragging = false;
        }
        else
        {
            if (primaryPressed &&
                pointerInMap &&
                hasWorldTarget)
            {
                _cameraDragging = true;
                camera.CenterOn(
                    worldTarget);
            }
            else if (_cameraDragging &&
                     primaryDown &&
                     pointerInMap &&
                     hasWorldTarget)
            {
                camera.CenterOn(
                    worldTarget);
            }

            if (!primaryDown)
            {
                _cameraDragging = false;
            }
        }

        _primaryWasDown =
            primaryDown;
        View =
            new RtsMinimapInteractionView(
                PointerCaptured,
                _cameraDragging,
                hasWorldTarget,
                worldTarget,
                pointerInHud
                    ? RtsCursorKind.Pan
                    : RtsCursorKind.Default);
    }

    public void Reset()
    {
        _sessionId =
            SimulationSessionId.None;
        _primaryWasDown = false;
        _cameraDragging = false;
        PointerCaptured = false;
        View =
            RtsMinimapInteractionView.Empty;
    }

    private void SynchronizeSession(
        SimulationSessionId sessionId)
    {
        if (!sessionId.IsSpecified ||
            sessionId ==
            _sessionId)
        {
            return;
        }

        _sessionId =
            sessionId;
        _primaryWasDown = false;
        _cameraDragging = false;
        PointerCaptured = false;
        View =
            RtsMinimapInteractionView.Empty;
    }
}
