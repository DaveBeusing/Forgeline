using System.Numerics;

namespace ForgeLine.Presentation;

/// <summary>Physical screen coordinates. Rendering and input share these exact angular boundaries.</summary>
public readonly record struct RadialActionLayout(Vector2 Anchor, Vector2 Center, float InnerRadius, float OuterRadius, int Count)
{
    public const float GapRadians = .045f;
    public bool IsValid => Count is >= 1 and <= 8 && float.IsFinite(Center.X) && float.IsFinite(Center.Y) &&
        float.IsFinite(Anchor.X) && float.IsFinite(Anchor.Y) && float.IsFinite(InnerRadius) &&
        float.IsFinite(OuterRadius) && InnerRadius > 0 && OuterRadius > InnerRadius;
    public float LabelHalfWidth => InnerRadius < 80 * ((OuterRadius - InnerRadius) / 96) ? 30 * ((OuterRadius - InnerRadius) / 96) : 38 * ((OuterRadius - InnerRadius) / 96);
    public float Step => MathF.Tau / Count;
    public float Start(int slot) => -MathF.PI / 2 - Step / 2 + slot * Step + GapRadians / 2;
    public float End(int slot) => Start(slot) + Step - GapRadians;
    public Vector2 LabelCenter(int slot) => Center + Direction(-MathF.PI / 2 + slot * Step) * ((InnerRadius + OuterRadius) / 2);
    public static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    public int Hit(Vector2 point)
    {
        if (!IsValid || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return -1;
        Vector2 delta = point - Center;
        float radius = delta.Length();
        if (radius < InnerRadius || radius > OuterRadius) return -1;
        float angle = (MathF.Atan2(delta.Y, delta.X) + MathF.PI / 2 + Step / 2 + MathF.Tau) % MathF.Tau;
        int slot = Math.Min(Count - 1, (int)(angle / Step));
        float local = angle - slot * Step;
        return local >= GapRadians / 2 && local <= Step - GapRadians / 2 ? slot : -1;
    }

    /// <summary>Conservative physical bounds of a painted sector, including its one-pixel soft edges.</summary>
    public HudRect SectorBounds(int slot)
    {
        if(!IsValid || slot<0 || slot>=Count) return default;
        float start=Start(slot),end=End(slot);
        Span<float> angles=stackalloc float[11];angles[0]=start;angles[1]=end;int count=2;
        for(int cardinal=-4;cardinal<=4;cardinal++) {
            float angle=cardinal*MathF.PI/2;
            if(angle>=start && angle<=end) angles[count++]=angle;
        }
        var minimum=new Vector2(float.PositiveInfinity);var maximum=new Vector2(float.NegativeInfinity);
        for(int i=0;i<count;i++) for(int edge=0;edge<2;edge++) {
            var point=Center+Direction(angles[i])*(edge==0 ? MathF.Max(0,InnerRadius-1) : OuterRadius+1);
            minimum=Vector2.Min(minimum,point);maximum=Vector2.Max(maximum,point);
        }
        return new(minimum.X,minimum.Y,maximum.X-minimum.X,maximum.Y-minimum.Y);
    }

    /// <summary>One obstacle contract for gameplay, fixture preparation and native recording.</summary>
    public static void ResolveObstacles(Span<HudRect> occupied, in GameplayHudLayout layout,
        OnboardingPresentation? onboarding = null, OperationsView operations = default, CombatGroupOverviewView? groups = null, NotificationStackLayout? notifications = null)
    {
        if (occupied.Length != 8) throw new ArgumentException("Ring obstacle storage must contain eight rectangles.",nameof(occupied));
        occupied[0]=layout.TopStatusBar;
        occupied[1]=layout.GroupOverviewBounds(groups);
        occupied[2]=layout.Minimap;
        occupied[3]=default;
        occupied[4]=notifications?.PaintedBounds ?? layout.NotificationBounds();
        occupied[5]=onboarding?.VisiblePanel ?? default;
        occupied[6]=onboarding?.Tab ?? default;
        occupied[7]=operations.Open && !operations.Suppressed ? OperationsLayout.Panel(layout) : default;
    }

    public static RadialActionLayout Fit(Vector2 anchor, float footprintRadius, in GameplayHudLayout hud,
        ReadOnlySpan<HudRect> occupied, int count = 8)
    {
        if (!float.IsFinite(anchor.X) || !float.IsFinite(anchor.Y) || !float.IsFinite(footprintRadius) ||
            footprintRadius < 0 || !float.IsFinite(hud.Scale) || hud.Scale <= 0 || count is < 1 or > 8 ||
            !hud.SafeArea.Contains(anchor)) return default;
        float preferred = Math.Clamp(hud.Scale, 1, 1.75f);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            float scale = preferred + (1 - preferred) * attempt / 4;
            float inner = Math.Clamp(footprintRadius + 10 * scale, 80 * scale, (100 - attempt * 5) * scale);
            var fit = TryFit(anchor, inner, inner + 96 * scale, hud.SafeArea, occupied, count);
            if (fit.IsValid) return fit;
        }
        // Preserve fourteen-pixel type and the full control band; a narrower label box
        // permits the compact inner hole when expanded hints occupy the left corner.
        return TryFit(anchor, 60, 156, hud.SafeArea, occupied, count);
    }

    /// <summary>Favor space for a noninteractive status card to the right of an owned ring.</summary>
    public static RadialActionLayout FitWithRightInspector(Vector2 anchor, float footprintRadius,
        in GameplayHudLayout hud, ReadOnlySpan<HudRect> occupied, float inspectorWidth)
    {
        var ordinary = Fit(anchor, footprintRadius, hud, occupied);
        if (!ordinary.IsValid || !float.IsFinite(inspectorWidth) || inspectorWidth <= 0) return ordinary;
        float gap = MathF.Max(10, hud.Gap + 2);
        if (ordinary.Center.X + ordinary.OuterRadius + gap + inspectorWidth <= hud.SafeArea.Right)
            return ordinary;
        var restricted = hud.SafeArea with
        {
            Width = MathF.Max(0, hud.SafeArea.Width - inspectorWidth - gap)
        };
        float preferred = Math.Clamp(hud.Scale, 1, 1.75f);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            float scale = preferred + (1 - preferred) * attempt / 4;
            float inner = Math.Clamp(footprintRadius + 10 * scale, 80 * scale, (100 - attempt * 5) * scale);
            var fit = TryFit(anchor, inner, inner + 96 * scale, restricted, occupied, 8);
            if (fit.IsValid) return fit;
        }
        var compact = TryFit(anchor, 60, 156, restricted, occupied, 8);
        return compact.IsValid ? compact : ordinary;
    }

    private static RadialActionLayout TryFit(Vector2 anchor, float inner, float outer, in HudRect safe,
        ReadOnlySpan<HudRect> occupied, int count)
    {
        // Bounded fallback stops at 14 physical-pixel labels; no unreadable shrinking.
        if (safe.Width < outer * 2 || safe.Height < outer * 2) return default;
        Vector2 best = default;
        float bestDistance = float.PositiveInfinity;
        // Deterministic obstacle edge candidates include the minimum screen-boundary translation.
        Span<float> xs = stackalloc float[34]; Span<float> ys = stackalloc float[34];
        int nx = 1, ny = 1;
        xs[0] = Math.Clamp(anchor.X, safe.X + outer, safe.Right - outer);
        ys[0] = Math.Clamp(anchor.Y, safe.Y + outer, safe.Bottom - outer);
        foreach (var rect in occupied)
        {
            if (rect.IsEmpty || nx + 2 > xs.Length) continue;
            xs[nx++] = Math.Clamp(rect.X - outer - 2, safe.X + outer, safe.Right - outer);
            xs[nx++] = Math.Clamp(rect.Right + outer + 2, safe.X + outer, safe.Right - outer);
            ys[ny++] = Math.Clamp(rect.Y - outer - 2, safe.Y + outer, safe.Bottom - outer);
            ys[ny++] = Math.Clamp(rect.Bottom + outer + 2, safe.Y + outer, safe.Bottom - outer);
        }
        for (int x = 0; x < nx; x++) for (int y = 0; y < ny; y++)
        {
            Vector2 center = new(xs[x], ys[y]);
            bool blocked = false;
            foreach (var rect in occupied)
            {
                if (rect.IsEmpty) continue;
                Vector2 nearest = new(Math.Clamp(center.X, rect.X, rect.Right), Math.Clamp(center.Y, rect.Y, rect.Bottom));
                if (Vector2.DistanceSquared(nearest, center) < outer * outer) { blocked = true; break; }
            }
            float distance = Vector2.DistanceSquared(center, anchor);
            if (!blocked && distance < bestDistance) { bestDistance = distance; best = center; }
        }
        return float.IsFinite(bestDistance) ? new(anchor, best, inner, outer, count) : default;
    }
}
