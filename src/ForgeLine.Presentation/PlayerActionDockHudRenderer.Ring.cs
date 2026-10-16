using System.Numerics;

namespace ForgeLine.Presentation;

internal sealed partial class PlayerActionDockHudRenderer
{
    private void EmitObjectRing(in PlayerActionPanelView panel, PlayerActionSnapshot? actions,
        in GameplayHudLayout layout, int width, int height)
    {
        var ring = panel.Ring;
        var geometry = ring.Geometry;
        if (!ring.Resolved || !geometry.IsValid || ring.Opacity <= 0) return;
        float scale = MathF.Min(layout.Scale, 1.75f), opacity = Math.Clamp(ring.Opacity, 0, 1);
        _scale = scale;
        // Tether is decoration only. Its width is one physical pixel and never owns input.
        Vector2 delta = geometry.Anchor - geometry.Center;
        if (delta.LengthSquared() > 9)
        {
            Vector2 end = geometry.Center + Vector2.Normalize(delta) * geometry.InnerRadius;
            Vector2 normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * .65f;
            RingQuad(geometry.Anchor - normal, geometry.Anchor + normal, end + normal, end - normal,
                new(.55f, .68f, .77f, .6f * opacity), width, height);
        }
        for (int i = 0; i < 8; i++)
        {
            var slot = ring.Slots[i];
            if (!slot.Present) continue;
            var target = slot.Target;
            bool navigation = slot.Navigation != ActionRingNavigation.None;
            bool mode = target.Kind == PlayerActionDockControlKind.Mode;
            bool item = target.Kind == PlayerActionDockControlKind.Item;
            bool enabled = navigation || mode || PlayerActionDockHudModel.CanUseControl(item ? PlayerActionDockControlKind.Activate : target.Kind,
                panel.Mode, target.ItemIndex, panel, actions);
            bool hovered = ring.Open && ring.HoveredSlot == i;
            Vector4 fill = enabled ? new(.045f, .075f, .10f, .82f) : new(.04f, .045f, .055f, .82f);
            if (hovered) fill = ring.Pressed ? new(.15f, .30f, .34f, .94f) : new(.10f, .22f, .27f, .90f);
            fill.W *= opacity;
            RingSegment(geometry, i, fill, width, height);
            Vector2 center = geometry.LabelCenter(i);
            float iconSize = 26 * scale;
            var icon = mode ? PlayerActionDockHudModel.ResolveModeIcon(target.Mode) :
                item ? PlayerActionDockHudModel.ResolveItemIcon(panel.Mode, target.ItemIndex, actions) :
                PlayerActionDockHudModel.ResolveModeIcon(panel.Mode);
            _skin.Icon(icon, center.X - iconSize / 2, center.Y - 30 * scale, iconSize,
                new Vector4(enabled ? 1 : .55f, enabled ? 1 : .55f, enabled ? 1 : .55f, opacity));
            string label = slot.Navigation switch
            {
                ActionRingNavigation.Back => "Back", ActionRingNavigation.Close => "Close",
                ActionRingNavigation.Next => "Next page", ActionRingNavigation.Previous => "Prev page",
                ActionRingNavigation.Parameters => "Parameters", ActionRingNavigation.NextItem => "Next item",
                _ => mode ? PlayerActionDockHudModel.ResolveModeLabel(target.Mode) :
                    item ? PlayerActionDockHudModel.ResolveItemTitle(panel.Mode, target.ItemIndex, actions) :
                    target.Kind == PlayerActionDockControlKind.Activate ? PlayerActionDockHudModel.ResolvePrimaryActionLabel(panel.Mode, target.ItemIndex, actions) :
                    PlayerActionDockHudModel.ResolveFooterLabel(target.Kind, panel.Mode)
            };
            Vector4 text = enabled ? TextColor : MutedTextColor; text.W *= opacity;
            _text.AddCentered(label, new(center.X - 48 * scale, center.Y, 96 * scale, 24 * scale),
                14 * scale, text, width, height, true);
        }
        if (ring.Level == ActionRingLevel.Parameters)
        {
            // Keep the inner hole visually and interactively free. Parameter values sit on their controls.
            Span<char> buffer = stackalloc char[192];
            var text = new HudTextBuilder(buffer);
            text.Append(PlayerActionDockHudModel.ResolveItemTitle(panel.Mode, panel.SelectedIndex, actions));
            var selected = geometry.LabelCenter(6);
            _text.AddCentered(text.Written, new(selected.X - 48 * scale, selected.Y + 22 * scale, 96 * scale, 20 * scale),
                12 * scale, MutedTextColor with { W = MutedTextColor.W * opacity }, width, height);
        }
    }

    private void RingSegment(in RadialActionLayout geometry, int slot, Vector4 color, int width, int height)
    {
        const int steps = 20;
        float start = geometry.Start(slot), end = geometry.End(slot);
        for (int j = 0; j < steps; j++)
        {
            float a = start + (end - start) * j / steps, b = start + (end - start) * (j + 1) / steps;
            Vector2 da = RadialActionLayout.Direction(a), db = RadialActionLayout.Direction(b), c = geometry.Center;
            float inner = geometry.InnerRadius, outer = geometry.OuterRadius;
            RingQuad(c + da * inner, c + da * outer, c + db * outer, c + db * inner, color, width, height);
            Vector4 clear = color with { W = 0 };
            RingQuadGradient(c + da * (inner - 1), c + da * inner, c + db * inner, c + db * (inner - 1),
                clear, color, color, clear, width, height);
            RingQuadGradient(c + da * outer, c + da * (outer + 1), c + db * (outer + 1), c + db * outer,
                color, clear, clear, color, width, height);
        }
    }
    private void RingQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 color, int width, int height) =>
        RingQuadGradient(a, b, c, d, color, color, color, color, width, height);
    private void RingQuadGradient(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd, int width, int height)
    {
        if (_vertexCount > MaxVertices - 6) return;
        _vertices[_vertexCount++] = new(RingNdc(a, width, height), ca);
        _vertices[_vertexCount++] = new(RingNdc(b, width, height), cb);
        _vertices[_vertexCount++] = new(RingNdc(c, width, height), cc);
        _vertices[_vertexCount++] = new(RingNdc(a, width, height), ca);
        _vertices[_vertexCount++] = new(RingNdc(c, width, height), cc);
        _vertices[_vertexCount++] = new(RingNdc(d, width, height), cd);
    }
    private static Vector2 RingNdc(Vector2 point, int width, int height) => new(point.X / width * 2 - 1, 1 - point.Y / height * 2);
}
