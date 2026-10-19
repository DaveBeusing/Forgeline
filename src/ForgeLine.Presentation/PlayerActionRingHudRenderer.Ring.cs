using System.Numerics;
using ForgeLine.Input;

namespace ForgeLine.Presentation;

internal sealed partial class PlayerActionRingHudRenderer
{
    private void EmitObjectRing(in PlayerActionPanelView panel, PlayerActionSnapshot? actions,
        in GameplayHudLayout layout, int width, int height)
    {
        var ring = panel.Ring;
        var geometry = ring.Geometry;
        if (!ring.Resolved || !geometry.IsValid || ring.Opacity <= 0) return;
        float scale = (geometry.OuterRadius - geometry.InnerRadius) / 96, opacity = Math.Clamp(ring.Opacity, 0, 1);
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
        Span<char> valueBuffer = stackalloc char[96];
        for (int i = 0; i < 8; i++)
        {
            var slot = ring.Slots[i];
            if (!slot.Present) continue;
            var target = slot.Target;
            bool navigation = slot.Navigation != ActionRingNavigation.None;
            bool mode = target.Kind == PlayerActionDockControlKind.Mode;
            bool item = target.Kind == PlayerActionDockControlKind.Item;
            bool enabled = slot.Enabled && (slot.Navigation == ActionRingNavigation.GroupControl ? ring.Group?.Participants(target.ItemIndex) > 0 : navigation || mode || PlayerActionDockHudModel.CanUseControl(item ? PlayerActionDockControlKind.Activate : target.Kind,
                panel.Mode, target.ItemIndex, panel, actions));
            bool hovered = ring.Open && ring.HoveredSlot == i;
            bool pending = panel.ContextualPending && item && panel.SelectedIndex == target.ItemIndex;
            Vector4 fill = RadialActionVisuals.Background(enabled, hovered, ring.Pressed && hovered, pending, opacity);
            RingSegment(geometry, i, fill, width, height);
            Vector2 center = geometry.LabelCenter(i);
            float iconSize = 20 * scale, halfWidth = geometry.LabelHalfWidth;
            var icon = mode ? PlayerActionDockHudModel.ResolveModeIcon(target.Mode) :
                item ? PlayerActionDockHudModel.ResolveItemIcon(panel.Mode, target.ItemIndex, actions) :
                PlayerActionDockHudModel.ResolveModeIcon(panel.Mode);
            _skin.Icon(icon, center.X - iconSize / 2, center.Y - 28 * scale, iconSize,
                new Vector4(enabled ? 1 : .55f, enabled ? 1 : .55f, enabled ? 1 : .55f, opacity));
            bool unavailableGlobal = ring.Global && slot.Key.Kind == 255;
            string label = unavailableGlobal ? "No target" : slot.Navigation switch
            {
                ActionRingNavigation.Group => "Group", ActionRingNavigation.GroupControl => GroupActionRingContext.Label(target.ItemIndex),
                ActionRingNavigation.Back => "Back", ActionRingNavigation.Close => "Close",
                ActionRingNavigation.Next => "Next", ActionRingNavigation.Previous => "Prev",
                ActionRingNavigation.Parameters => "Params", ActionRingNavigation.NextItem => "Next item",
                _ => mode ? PlayerActionDockHudModel.ResolveModeLabel(target.Mode) :
                    item ? PlayerActionDockHudModel.ResolveRingItemTitle(panel.Mode, target.ItemIndex, actions, geometry.LabelHalfWidth < 38 * scale) :
                    target.Kind == PlayerActionDockControlKind.Activate ? PlayerActionDockHudModel.ResolvePrimaryActionLabel(panel.Mode, target.ItemIndex, actions) :
                    PlayerActionDockHudModel.ResolveFooterLabel(target.Kind, panel.Mode)
            };
            Vector4 text = enabled ? TextColor : MutedTextColor; text.W *= opacity;
            if (slot.Navigation == ActionRingNavigation.Group && ring.Group is { } identity)
            {
                var heading = new HudTextBuilder(valueBuffer); heading.Append("Group "); heading.Append(identity.Count);
                _text.AddCentered(heading.Written, new(center.X - halfWidth, center.Y - 7 * scale, halfWidth * 2, 20 * scale),
                    14 * scale, text, width, height, true);
            }
            else _text.AddCentered(label, new(center.X - halfWidth, center.Y - 7 * scale, halfWidth * 2, 20 * scale),
                14 * scale, text, width, height, true);
            var value = new HudTextBuilder(valueBuffer);
            if (slot.Navigation == ActionRingNavigation.GroupControl && ring.Group is { } group)
            { if (group.HasOperationalFacts && ring.GroupFactsCurrent) value.Append(group.Participants(target.ItemIndex)); else value.Append("Updating"); value.Append(" / "); value.Append(group.SelectedCount); }
            else if (slot.Navigation == ActionRingNavigation.Group && ring.Group is { } summary)
                value.Append(ring.VisibleMembers < summary.Count ? "Part visible" : "Selection tools");
            else if (unavailableGlobal) value.Append(
                panel.Mode is PlayerActionPanelMode.Tactical or PlayerActionPanelMode.Supply
                    ? "SELECT UNIT" : "SELECT BUILDING");
            else if (item && panel.Mode == PlayerActionPanelMode.Tactical && ring.Group is { } participants && actions?.Tactical is { } tactical)
            {
                if (!participants.MatchesSelection(tactical.SelectedEntities)) value.Append("Updating");
                else if (!PlayerActionDockHudModel.ResolveItemState(panel.Mode,target.ItemIndex,actions,false).CanActivate) value.Append("Blocked");
                else { value.Append(target.ItemIndex is 5 or 6 ? tactical.Artillery.Count : tactical.CombatEligibleCount); value.Append("/");
                    value.Append(participants.SelectedCount); value.Append(geometry.LabelHalfWidth < 38 * scale ? " elig" : " eligible"); }
            }
            else if (!navigation && !mode && !item && panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics is { } requirements && panel.SelectedIndex >= requirements.Policies.Count &&
                target.Kind is PlayerActionDockControlKind.Decrease or PlayerActionDockControlKind.Increase)
            { value.Append("STEPS "); value.Append(panel.LogisticsSteps); }
            else if (!navigation && !mode && !item) AppendRingParameter(ref value, target.Kind, panel);
            else if (mode) value.Append(PlayerActionDockHudModel.ResolveModeShortcut(target.Mode, panel.Bindings));
            else if (item)
            {
                var state = PlayerActionDockHudModel.ResolveItemState(panel.Mode, target.ItemIndex, actions,
                    panel.ContextualPending && panel.ContextualActivationTick == actions?.Tick);
                value.Append(!state.CanActivate ? state.DisabledReason :
                    PlayerActionDockHudModel.ResolvePrimaryActionLabel(panel.Mode, target.ItemIndex, actions));
            }
            _text.AddCentered(value.Written, new(center.X - halfWidth, center.Y + 12 * scale, halfWidth * 2, 16 * scale),
                12 * scale, text, width, height);
        }
        if (ring.Level == ActionRingLevel.Parameters)
        {
            // Keep the inner hole visually and interactively free. Parameter values sit on their controls.
            Span<char> buffer = stackalloc char[192];
            var text = new HudTextBuilder(buffer);
            text.Append(PlayerActionDockHudModel.ResolveItemTitle(panel.Mode, ring.Slots[0].Target.ItemIndex, actions));
            var selected = geometry.LabelCenter(6);
            _text.AddCentered(text.Written, new(selected.X - geometry.LabelHalfWidth, selected.Y + 12 * scale, geometry.LabelHalfWidth * 2, 16 * scale),
                12 * scale, MutedTextColor with { W = MutedTextColor.W * opacity }, width, height);
        }
    }

    private static void AppendRingParameter(ref HudTextBuilder text, PlayerActionDockControlKind kind, in PlayerActionPanelView panel)
    {
        if (kind == PlayerActionDockControlKind.CyclePrimary)
            text.Append(panel.Mode == PlayerActionPanelMode.Supply ? PlayerActionDockHudModel.ResolveSupplyPriorityLabel(panel.SupplyPriority) :
                panel.Mode == PlayerActionPanelMode.Logistics ? PlayerActionDockHudModel.ResolveLogisticsPriorityLabel(panel.LogisticsPriority) :
                PlayerActionDockHudModel.ResolvePriorityLabel(panel.Priority));
        else if (kind == PlayerActionDockControlKind.CycleSecondary)
        {
            if (panel.Mode == PlayerActionPanelMode.Production) text.Append(PlayerActionDockHudModel.ResolveProductionModeLabel(panel.ProductionMode));
            else if (panel.Mode == PlayerActionPanelMode.Supply) text.Append(panel.AutomaticResupplyEnabled ? "AUTO ON" : "AUTO OFF");
            else text.Append(panel.StockThresholdField switch { PlayerStockThresholdField.Minimum => "MINIMUM", PlayerStockThresholdField.Maximum => "MAXIMUM", _ => "TARGET" });
        }
        else if (kind is PlayerActionDockControlKind.Decrease or PlayerActionDockControlKind.Increase)
        {
            if (panel.Mode == PlayerActionPanelMode.Supply)
            { text.Append((panel.SelectedIndex == 0 ? panel.AutomaticFuelThreshold : panel.AutomaticAmmunitionThreshold) * 100, "F0"); text.Append("%"); }
            else text.Append(panel.Mode == PlayerActionPanelMode.Logistics ? panel.StockThresholdField switch
                { PlayerStockThresholdField.Minimum => panel.StockMinimum, PlayerStockThresholdField.Maximum => panel.StockMaximum, _ => panel.StockTarget } :
                panel.DesiredStockQuantity, "F0");
        }
        var binding = (panel.Bindings ?? GameplayBindingRegistry.Default).Prompt(kind switch
        { PlayerActionDockControlKind.Activate => GameplayAction.Activate, PlayerActionDockControlKind.Cancel => GameplayAction.CancelJob,
            PlayerActionDockControlKind.CyclePrimary => GameplayAction.PrimarySetting, PlayerActionDockControlKind.CycleSecondary => GameplayAction.SecondarySetting,
            PlayerActionDockControlKind.Decrease => GameplayAction.Decrease, _ => GameplayAction.Increase });
        text.Append(" "); text.Append(binding);
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
