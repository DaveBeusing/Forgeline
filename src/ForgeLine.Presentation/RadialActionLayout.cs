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

    /// <summary>One obstacle contract for gameplay, fixture preparation and native recording.</summary>
    public static void ResolveObstacles(Span<HudRect> occupied, in GameplayHudLayout layout,
        OnboardingPresentation? onboarding = null, OperationsView operations = default)
    {
        if (occupied.Length != 8) throw new ArgumentException("Ring obstacle storage must contain eight rectangles.",nameof(occupied));
        occupied[0]=layout.TopStatusBar;
        occupied[1]=default;
        occupied[2]=layout.Minimap;
        occupied[3]=default;
        occupied[4]=layout.NotificationBounds(false);
        occupied[5]=onboarding?.VisiblePanel ?? default;
        occupied[6]=onboarding?.Tab ?? default;
        occupied[7]=operations.Open ? OperationsLayout.Panel(layout) : OperationsLayout.Entry(layout);
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
