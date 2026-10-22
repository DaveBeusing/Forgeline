using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Bounded presentation-only world annotations. No gameplay authority.</summary>
public sealed class SelectedUnitOrderOverlay
{
    public const float LineAlpha = 0.22f;
    public const int MaximumTargets = 1024;
    private readonly HashSet<Vector3> _targets = [];
    private readonly List<Vector3> _orderedTargets = [];

    public int LastConnectionCount { get; private set; }
    public int LastUniqueTargetCount { get; private set; }

    public void Draw(
        DebugDraw draw,
        RenderWorld world,
        IReadOnlyCollection<EntityId>? currentSelection,
        SimulationSessionId session,
        RtsCamera camera,
        float interpolation,
        int viewportHeight,
        float uiScale,
        TimeSpan presentationTime,
        bool reducedMotion)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(camera);
        LastConnectionCount = LastUniqueTargetCount = 0;
        _targets.Clear();
        _orderedTargets.Clear();
        draw.Clear();

        SelectedUnitOrderSnapshot? orders = world.CurrentSnapshot?.SelectedOrders;
        if (orders is null || world.CurrentSnapshot?.SessionId != session ||
            !orders.MatchesSelection(session, currentSelection) ||
            viewportHeight <= 0)
            return;

        draw.Enabled = true;
        float displayScale = float.IsFinite(uiScale)
            ? Math.Clamp(uiScale, 0.5f, 4.0f) : 1.0f;
        float metersPerPixel = 2.0f * camera.Distance *
            MathF.Tan(camera.Settings.VerticalFieldOfViewRadians * 0.5f) /
            viewportHeight * displayScale;
        if (!float.IsFinite(metersPerPixel) || metersPerPixel <= 0)
            return;

        Vector4 stroke = RtsSelectionVisualStyle.Selected with { W = LineAlpha };
        foreach (ref readonly SelectedUnitOrderView order in orders.Orders)
        {
            if (!order.HasTarget || !Finite(order.Target))
                continue;
            if (!world.TryGetInterpolatedInstance(order.Entity, interpolation, out RenderInstance instance))
                continue;
            Vector3 start = instance.Transform.Position;
            if (!Finite(start))
                continue;

            draw.Line(start, order.Target, stroke);
            LastConnectionCount++;
            // Several formation members may share one destination. Blend a
            // given pulse once, not once per member.
            if (_targets.Count < MaximumTargets && _targets.Add(order.Target))
            {
                _orderedTargets.Add(order.Target);
                LastUniqueTargetCount++;
            }
        }

        float pulse = reducedMotion ? 0.0f :
            MathF.Sin((float)(presentationTime.TotalSeconds * (MathF.Tau / 1.2)));
        float radius = metersPerPixel * (3.0f + pulse * 0.45f);
        Vector4 dot = stroke with { W = reducedMotion ? 0.65f : 0.60f + 0.10f * pulse };
        _orderedTargets.Sort(static (a, b) =>
        {
            int first = a.X.CompareTo(b.X);
            if (first != 0) return first;
            int second = a.Z.CompareTo(b.Z);
            return second != 0 ? second : a.Y.CompareTo(b.Y);
        });
        foreach (Vector3 target in _orderedTargets)
        {
            // Seven closely spaced horizontal chords form a compact,
            // approximately filled target disc without a per-target draw.
            for (int row = -3; row <= 3; row++)
            {
                float normal = row / 3.5f;
                float half = radius * MathF.Sqrt(MathF.Max(0, 1 - normal * normal));
                Vector3 center = target + new Vector3(0, 0, normal * radius);
                draw.Line(center - new Vector3(half, 0, 0),
                    center + new Vector3(half, 0, 0), dot);
            }
        }
    }

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
