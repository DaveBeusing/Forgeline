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
    int DockIndex = -1,
    bool Contextual = false,
    bool SelectionStatus = false,
    ulong SelectionRevision = 0,
    ulong MenuRevision = 0);

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
        bool statusAvailable = panel.Ring is { Open: true, Resolved: true, Geometry.IsValid: true };
        if (!input.HasPointerPosition && statusAvailable) pointer = panel.Ring.Geometry.Center;
        if (blocked || input.FocusLostThisFrame || (!input.HasPointerPosition && !statusAvailable) ||
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
        int count = panel.SelectionPending ? 0 : PlayerActionDockInteractionLayout.GetItemCount(panel.Mode, snapshot.PlayerActions);
        // The selected-object inspector is persistent while its ring is open;
        // hovering an action sector must not replace the authorized object facts.
        if (statusAvailable && !panel.Ring.Global && !panel.SelectionPending)
        {
            next = next with { SelectionStatus = true, SelectionRevision = panel.Ring.SelectionRevision,
                MenuRevision = panel.Ring.Revision, PointerPosition = panel.Ring.Geometry.Center };
        }
        else if (panel.Ring.TryHit(pointer, out var radial))
        {
            var target = radial.Navigation is ActionRingNavigation.Parameters or ActionRingNavigation.NextItem
                ? new PlayerActionDockHitTarget(PlayerActionDockControlKind.Item,panel.Mode,panel.SelectedIndex)
                : radial.Navigation != ActionRingNavigation.None ? new(PlayerActionDockControlKind.Page,panel.Mode,panel.SelectedIndex) : radial.Target;
            next = next with { DockMode = target.Mode, DockControl = (int)target.Kind,
                DockIndex = radial.Navigation == ActionRingNavigation.GroupControl ? -100 - radial.Target.ItemIndex :
                    radial.Navigation == ActionRingNavigation.Group ? -99 : target.ItemIndex };
        }
        else if (statusAvailable && !worldPointerCaptured)
        {
            next = next with { SelectionStatus = true, SelectionRevision = panel.Ring.SelectionRevision,
                MenuRevision = panel.Ring.Revision, PointerPosition = panel.Ring.Geometry.Center };
        }
        else if (!worldPointerCaptured &&
            !HudInteractionContext.BlocksWorldPointer(pointer, layout, true, panel.DockExpanded, includePrimaryCommands: !panel.Ring.UsesRing))
        {
            next = next with { Entity = hoveredEntity };
            // Current detected contacts use copied opaque contact positions, never an ECS raycast.
            if (!hoveredEntity.IsValid && snapshot.Intelligence is { } intelligence &&
                intelligence.Tick == snapshot.Tick)
            {
                float nearest = 12 * layout.Scale;
                nearest *= nearest;
                for (int i = 0; i < intelligence.Contacts.Count; i++)
                {
                    var contact = intelligence.Contacts[i];
                    if (!contact.IsCurrent || contact.State != IntelligenceState.Detected) continue;
                    var projection = camera.WorldToScreen(contact.LastKnownPosition, layout.ViewportWidth, layout.ViewportHeight);
                    float distance = Vector2.DistanceSquared(projection.Position, pointer);
                    if (!projection.IsVisible || distance > nearest) continue;
                    nearest = distance;
                    next = next with { Contact = contact.ContactKey };
                }
            }
        }
        if (!next.SelectionStatus && !next.Entity.IsValid && !next.Contact.IsSpecified && next.DockControl == 0) return Reset();

        bool changed = next.SessionId != _candidate.SessionId || next.Entity != _candidate.Entity ||
            next.Contact != _candidate.Contact || next.DockMode != _candidate.DockMode ||
            next.DockControl != _candidate.DockControl || next.DockIndex != _candidate.DockIndex ||
            next.Contextual != _candidate.Contextual || next.SelectionStatus != _candidate.SelectionStatus ||
            next.SelectionRevision != _candidate.SelectionRevision || next.MenuRevision != _candidate.MenuRevision ||
            next.ViewportWidth != _candidate.ViewportWidth || next.ViewportHeight != _candidate.ViewportHeight ||
            next.Scale != _candidate.Scale || Vector2.DistanceSquared(pointer, _anchor) > 9 * layout.Scale * layout.Scale;
        if (changed)
        {
            _anchor = pointer;
            _stable = TimeSpan.Zero;
        }
        else if (elapsed > TimeSpan.Zero && elapsed <= TimeSpan.FromMilliseconds(250)) _stable += elapsed;
        else if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromMilliseconds(250)) _stable = TimeSpan.Zero;
        _candidate = next with { Ready = next.SelectionStatus || _stable >= DefaultDelay };
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
    public static HudRect BesideRing(in ObjectActionRingView ring, in GameplayHudLayout layout,
        ReadOnlySpan<HudRect> published, float width, float height)
    {
        if(published.Length>8) throw new ArgumentException("Tooltip supports the eight published HUD obstacles.",nameof(published));
        var safe=layout.SafeArea;
        if(safe.IsEmpty || width<=0 || height<=0 || width>safe.Width || height>safe.Height || !ring.Geometry.IsValid) return default;
        Span<HudRect> occupied=stackalloc HudRect[16];int count=0;
        foreach(var rectangle in published) if(!rectangle.IsEmpty) occupied[count++]=rectangle;
        for(int slot=0;slot<ring.Geometry.Count;slot++) if(ring.Slots[slot].Present)
            occupied[count++]=ring.Geometry.SectorBounds(slot);
        float gap=MathF.Min(layout.Gap,8);
        Span<float> xs=stackalloc float[40];Span<float> ys=stackalloc float[40];
        var center=ring.Geometry.Center;
        xs[0]=center.X+ring.Geometry.OuterRadius+gap;xs[1]=center.X-ring.Geometry.OuterRadius-gap-width;
        xs[2]=center.X-width/2;xs[3]=safe.X;xs[4]=safe.Right-width;
        ys[0]=center.Y-height/2;ys[1]=center.Y+ring.Geometry.OuterRadius+gap;
        ys[2]=center.Y-ring.Geometry.OuterRadius-gap-height;ys[3]=safe.Y;ys[4]=safe.Bottom-height;
        int xCount=5,yCount=5;
        for(int i=0;i<count;i++) {
            xs[xCount++]=occupied[i].Right+gap;xs[xCount++]=occupied[i].X-gap-width;
            ys[yCount++]=occupied[i].Bottom+gap;ys[yCount++]=occupied[i].Y-gap-height;
        }
        HudRect best=default;float score=float.PositiveInfinity;
        for(int x=0;x<xCount;x++) for(int y=0;y<yCount;y++) {
            var candidate=new HudRect(Math.Clamp(xs[x],safe.X,safe.Right-width),Math.Clamp(ys[y],safe.Y,safe.Bottom-height),width,height);
            float distance=Vector2.DistanceSquared(new(candidate.X+width/2,candidate.Y+height/2),center);
            if(distance>=score) continue;
            bool clear=true;
            for(int i=0;i<count;i++) {
                var obstacle=occupied[i];
                if(candidate.Intersects(new(obstacle.X-gap,obstacle.Y-gap,obstacle.Width+gap*2,obstacle.Height+gap*2))) {clear=false;break;}
            }
            if (clear)
            {
                // The card belongs to the free area on the ring's right whenever feasible.
                if (x == 0) return candidate;
                best = candidate; score = distance;
            }
        }
        return best;
    }

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
        if (y + height > safe.Bottom)
            y = pointer.Y - gap - height;
        return new(Math.Clamp(x, safe.X, safe.Right - width), Math.Clamp(y, safe.Y, safe.Bottom - height), width, height);
    }
}
