using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Intelligence;
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

    public static HudRect GetSelectorButtonRect(
        in GameplayHudLayout layout,
        int index)
    {
        if (index < 0 ||
            index >=
                RtsStrategicOverlayHudModel.SelectorButtonCount)
        {
            return default;
        }

        HudRect selector =
            GetSelectorRect(
                layout);
        float gap =
            2.0f *
            layout.Scale;
        float totalGap =
            gap *
            (RtsStrategicOverlayHudModel.SelectorButtonCount -
             1);
        float width =
            MathF.Max(
                0.0f,
                (selector.Width -
                 totalGap) /
                RtsStrategicOverlayHudModel.SelectorButtonCount);

        return new HudRect(
            selector.X +
                index *
                (width + gap),
            selector.Y,
            width,
            selector.Height);
    }

    public static bool TryHitOverlay(
        Vector2 pointer,
        in GameplayHudLayout layout,
        out StrategicOverlayMode mode)
    {
        for (int index = 0;
             index <
                 RtsStrategicOverlayHudModel.SelectorButtonCount;
             index++)
        {
            if (!GetSelectorButtonRect(
                    layout,
                    index).Contains(
                    pointer))
            {
                continue;
            }

            mode =
                RtsStrategicOverlayHudModel.ModeForButton(
                    index);
            return true;
        }

        mode =
            StrategicOverlayMode.None;
        return false;
    }

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
    private bool _secondaryWasDown;
    private bool _cameraDragging;
    private MovementOrderRequest? _pendingMovement;
    private PlayerActionRequest? _pendingAction;
    private StrategicOverlayMode? _pendingOverlay;

    public bool PointerCaptured { get; private set; }

    public RtsMinimapInteractionView View { get; private set; } =
        RtsMinimapInteractionView.Empty;

    public void Update(
        InputState input,
        RtsCamera camera,
        ITerrainQuery terrain,
        PresentationSnapshot? snapshot,
        in GameplayHudLayout layout,
        IReadOnlyCollection<EntityId> selectedEntities,
        TacticalTargetingMode targetingMode,
        FormationTemplate formation,
        bool minimapEnabled = true,
        bool inputBlocked = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(selectedEntities);

        SynchronizeSession(
            snapshot?.SessionId ??
            SimulationSessionId.None);

        bool terminal =
            snapshot?.PlayerExperience?.IsMatchComplete ==
            true;
        bool primaryDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Left);
        bool secondaryDown =
            input.IsMouseButtonDown(
                PlatformMouseButton.Right);
        bool primaryPressed =
            primaryDown &&
            !_primaryWasDown;
        bool secondaryPressed =
            secondaryDown &&
            !_secondaryWasDown;

        if (!minimapEnabled)
        {
            _cameraDragging = false;
            PointerCaptured = false;
            _primaryWasDown =
                primaryDown;
            _secondaryWasDown =
                secondaryDown;
            View =
                RtsMinimapInteractionView.Empty;
            return;
        }

        bool pointerInHud =
            input.HasPointerPosition &&
            layout.Minimap.Contains(
                input.PointerPosition);
        bool pointerInMap =
            input.HasPointerPosition &&
            RtsMinimapInteractionLayout.GetMapRect(
                layout).Contains(
                input.PointerPosition);
        bool pointerInSelector =
            input.HasPointerPosition &&
            RtsMinimapInteractionLayout.GetSelectorRect(
                layout).Contains(
                input.PointerPosition);

        PointerCaptured =
            pointerInHud ||
            _cameraDragging;

        Vector3 flatTarget = default;
        float terrainHeight = 0.0f;
        bool hasWorldTarget =
            pointerInMap &&
            RtsMinimapInteractionLayout.TryMapPointerToWorld(
                input.PointerPosition,
                layout,
                terrain.WorldBounds,
                out flatTarget) &&
            terrain.TrySampleHeight(
                flatTarget.X,
                flatTarget.Z,
                out terrainHeight);
        Vector3 worldTarget =
            hasWorldTarget
                ? new Vector3(
                    flatTarget.X,
                    terrainHeight,
                    flatTarget.Z)
                : default;

        bool interactionAllowed =
            !inputBlocked &&
            !terminal;

        if (!interactionAllowed)
        {
            _cameraDragging = false;
        }
        else if (primaryPressed &&
                 pointerInSelector &&
                 RtsMinimapInteractionLayout.TryHitOverlay(
                     input.PointerPosition,
                     layout,
                     out StrategicOverlayMode overlay))
        {
            _cameraDragging = false;
            _pendingOverlay =
                overlay;
        }
        else if (targetingMode !=
                 TacticalTargetingMode.None)
        {
            _cameraDragging = false;

            if (primaryPressed &&
                pointerInMap &&
                hasWorldTarget &&
                TryCreateTacticalRequest(
                    snapshot,
                    targetingMode,
                    worldTarget,
                    terrain.WorldBounds,
                    layout,
                    formation,
                    out PlayerActionRequest request))
            {
                _pendingAction =
                    request;
            }
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

            if (secondaryPressed &&
                pointerInMap &&
                hasWorldTarget &&
                selectedEntities.Count > 0)
            {
                _pendingMovement =
                    new MovementOrderRequest(
                        selectedEntities.ToArray(),
                        worldTarget);
            }

            if (!primaryDown)
            {
                _cameraDragging = false;
            }
        }

        _primaryWasDown =
            primaryDown;
        _secondaryWasDown =
            secondaryDown;

        View =
            new RtsMinimapInteractionView(
                PointerCaptured,
                _cameraDragging,
                hasWorldTarget,
                worldTarget,
                ResolveCursor(
                    pointerInHud,
                    pointerInMap,
                    targetingMode,
                    snapshot,
                    worldTarget,
                    terrain.WorldBounds,
                    layout));
    }

    public bool TryTakeMovementRequest(
        out MovementOrderRequest request)
    {
        if (_pendingMovement is null)
        {
            request = null!;
            return false;
        }

        request =
            _pendingMovement;
        _pendingMovement =
            null;
        return true;
    }

    public bool TryTakeActionRequest(
        out PlayerActionRequest request)
    {
        if (!_pendingAction.HasValue)
        {
            request = default;
            return false;
        }

        request =
            _pendingAction.Value;
        _pendingAction =
            null;
        return true;
    }

    public bool TryTakeOverlaySelection(
        out StrategicOverlayMode mode)
    {
        if (!_pendingOverlay.HasValue)
        {
            mode =
                StrategicOverlayMode.None;
            return false;
        }

        mode =
            _pendingOverlay.Value;
        _pendingOverlay =
            null;
        return true;
    }

    public void Reset()
    {
        _sessionId =
            SimulationSessionId.None;
        _primaryWasDown = false;
        _secondaryWasDown = false;
        _cameraDragging = false;
        _pendingMovement = null;
        _pendingAction = null;
        _pendingOverlay = null;
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
        _secondaryWasDown = false;
        _cameraDragging = false;
        _pendingMovement = null;
        _pendingAction = null;
        _pendingOverlay = null;
        PointerCaptured = false;
        View =
            RtsMinimapInteractionView.Empty;
    }

    private static RtsCursorKind ResolveCursor(
        bool pointerInHud,
        bool pointerInMap,
        TacticalTargetingMode targetingMode,
        PresentationSnapshot? snapshot,
        Vector3 worldTarget,
        in AxisAlignedBounds worldBounds,
        in GameplayHudLayout layout)
    {
        if (!pointerInHud)
        {
            return RtsCursorKind.Default;
        }

        if (!pointerInMap)
        {
            return RtsCursorKind.Select;
        }

        return targetingMode switch
        {
            TacticalTargetingMode.Attack =>
                TryPickIdentifiedTarget(
                    snapshot?.PlayerActions?.Tactical,
                    worldTarget,
                    worldBounds,
                    layout,
                    out _)
                    ? RtsCursorKind.Attack
                    : RtsCursorKind.Invalid,
            TacticalTargetingMode.AttackMove =>
                RtsCursorKind.AttackMove,
            TacticalTargetingMode.Retreat =>
                RtsCursorKind.Move,
            TacticalTargetingMode.FireMission =>
                HasFireMissionTarget(
                    snapshot,
                    worldTarget,
                    worldBounds,
                    layout)
                    ? RtsCursorKind.Attack
                    : RtsCursorKind.Invalid,
            _ =>
                RtsCursorKind.Pan
        };
    }

    private static bool TryCreateTacticalRequest(
        PresentationSnapshot? snapshot,
        TacticalTargetingMode targetingMode,
        Vector3 worldTarget,
        in AxisAlignedBounds worldBounds,
        in GameplayHudLayout layout,
        FormationTemplate formation,
        out PlayerActionRequest request)
    {
        request = default;

        if (snapshot?.PlayerActions?.Tactical is not
            PlayerTacticalActionReadModel tactical ||
            tactical.SelectedEntities.Count == 0)
        {
            return false;
        }

        switch (targetingMode)
        {
            case TacticalTargetingMode.Attack:
                if (!TryPickIdentifiedTarget(
                        tactical,
                        worldTarget,
                        worldBounds,
                        layout,
                        out EntityId target))
                {
                    return false;
                }

                request =
                    PlayerActionRequest.Attack(
                        tactical.SelectedEntities,
                        target);
                return true;

            case TacticalTargetingMode.AttackMove:
                request =
                    PlayerActionRequest.AttackMove(
                        tactical.SelectedEntities,
                        worldTarget,
                        formation);
                return true;

            case TacticalTargetingMode.Retreat:
                request =
                    PlayerActionRequest.Retreat(
                        tactical.SelectedEntities,
                        worldTarget,
                        formation);
                return true;

            case TacticalTargetingMode.FireMission:
                if (TryPickContact(
                        snapshot.Intelligence,
                        worldTarget,
                        worldBounds,
                        layout,
                        out IntelligenceContactKey contact))
                {
                    request =
                        PlayerActionRequest.FireMission(
                            tactical.SelectedEntities,
                            contact,
                            3);
                    return true;
                }

                if (!IsTerrainVisible(
                        snapshot.Intelligence,
                        worldTarget))
                {
                    return false;
                }

                request =
                    PlayerActionRequest.FireMission(
                        tactical.SelectedEntities,
                        worldTarget,
                        3);
                return true;

            default:
                return false;
        }
    }

    private static bool HasFireMissionTarget(
        PresentationSnapshot? snapshot,
        Vector3 worldTarget,
        in AxisAlignedBounds worldBounds,
        in GameplayHudLayout layout) =>
        snapshot is not null &&
        (TryPickContact(
             snapshot.Intelligence,
             worldTarget,
             worldBounds,
             layout,
             out _) ||
         IsTerrainVisible(
             snapshot.Intelligence,
             worldTarget));

    private static bool TryPickIdentifiedTarget(
        PlayerTacticalActionReadModel? tactical,
        Vector3 worldTarget,
        in AxisAlignedBounds worldBounds,
        in GameplayHudLayout layout,
        out EntityId target)
    {
        target =
            EntityId.Invalid;

        if (tactical is null)
        {
            return false;
        }

        float toleranceSquared =
            ResolveWorldPickToleranceSquared(
                worldBounds,
                layout);
        float nearest =
            toleranceSquared;

        for (int index = 0;
             index < tactical.Targets.Count;
             index++)
        {
            PlayerTacticalTargetReadModel candidate =
                tactical.Targets[index];

            if (!candidate.Entity.IsValid ||
                candidate.State !=
                    IntelligenceState.Identified ||
                candidate.CompatibleUnitCount <= 0)
            {
                continue;
            }

            float dx =
                candidate.LastKnownPosition.X -
                worldTarget.X;
            float dz =
                candidate.LastKnownPosition.Z -
                worldTarget.Z;
            float distanceSquared =
                dx * dx +
                dz * dz;

            if (distanceSquared >
                    nearest ||
                (distanceSquared ==
                     nearest &&
                 target.IsValid &&
                 candidate.Entity >=
                     target))
            {
                continue;
            }

            nearest =
                distanceSquared;
            target =
                candidate.Entity;
        }

        return target.IsValid;
    }

    private static bool TryPickContact(
        FactionIntelligenceSnapshot? intelligence,
        Vector3 worldTarget,
        in AxisAlignedBounds worldBounds,
        in GameplayHudLayout layout,
        out IntelligenceContactKey contactKey)
    {
        contactKey =
            IntelligenceContactKey.None;

        if (intelligence is null)
        {
            return false;
        }

        float toleranceSquared =
            ResolveWorldPickToleranceSquared(
                worldBounds,
                layout);
        float nearest =
            toleranceSquared;

        for (int index = 0;
             index < intelligence.Contacts.Count;
             index++)
        {
            IntelligenceContact contact =
                intelligence.Contacts[index];

            if (!contact.ContactKey.IsSpecified ||
                contact.State is not
                    IntelligenceState.Detected and not
                    IntelligenceState.Identified)
            {
                continue;
            }

            float dx =
                contact.LastKnownPosition.X -
                worldTarget.X;
            float dz =
                contact.LastKnownPosition.Z -
                worldTarget.Z;
            float distanceSquared =
                dx * dx +
                dz * dz;

            if (distanceSquared >
                    nearest ||
                (distanceSquared ==
                     nearest &&
                 contactKey.IsSpecified &&
                 contact.ContactKey >=
                     contactKey))
            {
                continue;
            }

            nearest =
                distanceSquared;
            contactKey =
                contact.ContactKey;
        }

        return contactKey.IsSpecified;
    }

    private static bool IsTerrainVisible(
        FactionIntelligenceSnapshot? intelligence,
        Vector3 worldTarget)
    {
        if (intelligence is null ||
            intelligence.CellSizeMeters <=
                0.0f)
        {
            return false;
        }

        var cell =
            new VisibilityCellCoordinate(
                (int)MathF.Floor(
                    worldTarget.X /
                    intelligence.CellSizeMeters),
                (int)MathF.Floor(
                    worldTarget.Z /
                    intelligence.CellSizeMeters));

        for (int index = 0;
             index < intelligence.Cells.Count;
             index++)
        {
            VisibilityCellSnapshot candidate =
                intelligence.Cells[index];

            if (candidate.Cell ==
                cell)
            {
                return candidate.State ==
                    IntelligenceState.Visible;
            }
        }

        return false;
    }

    private static float ResolveWorldPickToleranceSquared(
        in AxisAlignedBounds worldBounds,
        in GameplayHudLayout layout)
    {
        HudRect map =
            RtsMinimapInteractionLayout.GetMapRect(
                layout);
        float worldWidth =
            MathF.Max(
                1.0f,
                worldBounds.Maximum.X -
                worldBounds.Minimum.X);
        float worldDepth =
            MathF.Max(
                1.0f,
                worldBounds.Maximum.Z -
                worldBounds.Minimum.Z);
        float worldPerPixel =
            MathF.Max(
                worldWidth /
                    MathF.Max(
                        1.0f,
                        map.Width),
                worldDepth /
                    MathF.Max(
                        1.0f,
                        map.Height));
        float tolerance =
            9.0f *
            layout.Scale *
            worldPerPixel;

        return tolerance *
            tolerance;
    }
}
