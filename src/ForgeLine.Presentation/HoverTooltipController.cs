using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Input;
using ForgeLine.Intelligence;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct HoverTooltipView(
    SimulationSessionId SessionId,
    EntityId Entity,
    IntelligenceContactKey Contact,
    Vector2 PointerPosition,
    bool Ready,
    int ViewportWidth,
    int ViewportHeight,
    float Scale,
    PlayerActionPanelMode DockMode = PlayerActionPanelMode.Closed,
    int DockControl = 0,
    int DockIndex = -1);

public sealed class HoverTooltipController
{
    public static TimeSpan DefaultDelay => TimeSpan.FromMilliseconds(175);
    private HoverTooltipView _candidate;
    private Vector2 _anchor;
    private TimeSpan _stable;

    public HoverTooltipView Update(InputState input, PresentationSnapshot? snapshot,
        EntityId hoveredEntity, RtsCamera camera, in GameplayHudLayout layout,
        in PlayerActionPanelView panel, bool blocked, bool worldPointerCaptured, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(camera);
        Vector2 pointer = input.PointerPosition;
        if (blocked || input.FocusLostThisFrame || !input.HasPointerPosition ||
            input.IsMouseButtonDown(PlatformMouseButton.Left) ||
            input.IsMouseButtonDown(PlatformMouseButton.Middle) ||
            input.IsMouseButtonDown(PlatformMouseButton.Right) || input.WheelDelta != 0 ||
            snapshot is null || !snapshot.SessionId.IsSpecified ||
            snapshot.PlayerExperience is not { IsMatchComplete: false } || layout.SafeArea.IsEmpty ||
            !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y) ||
            pointer.X < 0 || pointer.Y < 0 || pointer.X >= layout.ViewportWidth || pointer.Y >= layout.ViewportHeight)
            return Reset();

        var next = new HoverTooltipView(snapshot.SessionId, EntityId.Invalid, default, pointer,
            false, layout.ViewportWidth, layout.ViewportHeight, layout.Scale);
        int count = PlayerActionDockInteractionLayout.GetItemCount(panel.Mode, snapshot.PlayerActions);
        if (PlayerActionDockInteractionLayout.TryHit(pointer, layout, panel.IsOpen, count, out var hit))
        {
            next = next with
            {
                DockMode = hit.Kind == PlayerActionDockControlKind.Mode ? hit.Mode : panel.Mode,
                DockControl = (int)hit.Kind,
                DockIndex = hit.Kind == PlayerActionDockControlKind.Item ? hit.ItemIndex : panel.SelectedIndex
            };
        }
        else if (!worldPointerCaptured &&
            !HudInteractionContext.BlocksWorldPointer(pointer, layout, true, panel.IsOpen))
        {
            next = next with { Entity = hoveredEntity };
            // Current detected contacts use copied opaque contact positions, never an ECS raycast.
            if (!hoveredEntity.IsValid && snapshot.Intelligence is { } intelligence &&
                intelligence.Tick == snapshot.Tick)
            {
                float nearest = 12 * layout.Scale;
                nearest *= nearest;
                foreach (var contact in intelligence.Contacts)
                {
                    if (!contact.IsCurrent || contact.State != IntelligenceState.Detected) continue;
                    var projection = camera.WorldToScreen(contact.LastKnownPosition, layout.ViewportWidth, layout.ViewportHeight);
                    float distance = Vector2.DistanceSquared(projection.Position, pointer);
                    if (!projection.IsVisible || distance > nearest) continue;
                    nearest = distance;
                    next = next with { Contact = contact.ContactKey };
                }
            }
        }
        if (!next.Entity.IsValid && !next.Contact.IsSpecified && next.DockControl == 0) return Reset();

        bool changed = next.SessionId != _candidate.SessionId || next.Entity != _candidate.Entity ||
            next.Contact != _candidate.Contact || next.DockMode != _candidate.DockMode ||
            next.DockControl != _candidate.DockControl || next.DockIndex != _candidate.DockIndex ||
            next.ViewportWidth != _candidate.ViewportWidth || next.ViewportHeight != _candidate.ViewportHeight ||
            next.Scale != _candidate.Scale || Vector2.DistanceSquared(pointer, _anchor) > 9 * layout.Scale * layout.Scale;
        if (changed)
        {
            _anchor = pointer;
            _stable = TimeSpan.Zero;
        }
        else if (elapsed > TimeSpan.Zero && elapsed <= TimeSpan.FromMilliseconds(250)) _stable += elapsed;
        else if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMilliseconds(250)) _stable = TimeSpan.Zero;
        _candidate = next with { Ready = _stable >= DefaultDelay };
        return _candidate;
    }

    public HoverTooltipView Reset()
    {
        _candidate = default;
        _stable = TimeSpan.Zero;
        return default;
    }
}

internal static class HoverTooltipPlacement
{
    public static HudRect Resolve(Vector2 pointer, in GameplayHudLayout layout, float width, float height)
    {
        var safe = layout.SafeArea;
        if (safe.IsEmpty || !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y)) return default;
        width = MathF.Min(width, safe.Width);
        height = MathF.Min(height, safe.Height);
        float gap = 18 * layout.Scale;
        // At extreme scale, fit the vertical space beside the cursor before clamping.
        float below = MathF.Max(0, safe.Bottom - pointer.Y - gap);
        float above = MathF.Max(0, pointer.Y - gap - safe.Y);
        height = MathF.Min(height, MathF.Max(above, below));
        if (height <= 0) return default;
        float x = pointer.X + gap;
        float y = pointer.Y + gap;
        if (x + width > safe.Right) x = pointer.X - gap - width;
        if (y + height > safe.Bottom || (layout.ActionDock.Contains(pointer) && above >= height))
            y = pointer.Y - gap - height;
        return new(Math.Clamp(x, safe.X, safe.Right - width), Math.Clamp(y, safe.Y, safe.Bottom - height), width, height);
    }
}
