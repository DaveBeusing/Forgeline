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
            bool orderItem = item && panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics is { } orderList && target.ItemIndex >= orderList.OrderStartIndex &&
                target.ItemIndex < orderList.ItemCount;
            bool orderDetail = ring.Level == ActionRingLevel.Parameters && panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics is { } detailList && panel.SelectedIndex >= detailList.OrderStartIndex &&
                panel.SelectedIndex < detailList.ItemCount && i < 6;
            bool coreStock = ring.Level == ActionRingLevel.Parameters && panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics is { IsCommandCore: true } core && panel.SelectedIndex >= 0 && panel.SelectedIndex < core.Policies.Count;
            bool bandControl = target.Kind is PlayerActionDockControlKind.DecreaseMinimum or PlayerActionDockControlKind.IncreaseMinimum or
                PlayerActionDockControlKind.DecreaseMaximum or PlayerActionDockControlKind.IncreaseMaximum;
            bool activeStockField = coreStock && bandControl &&
                (target.Kind is PlayerActionDockControlKind.DecreaseMinimum or PlayerActionDockControlKind.IncreaseMinimum
                    ? panel.StockThresholdField == PlayerStockThresholdField.Minimum
                    : panel.StockThresholdField == PlayerStockThresholdField.Maximum);
            bool enabled = slot.Enabled && (slot.Navigation == ActionRingNavigation.GroupControl ? ring.Group?.Participants(target.ItemIndex) > 0 : navigation || mode || PlayerActionDockHudModel.CanUseControl(item ? PlayerActionDockControlKind.Activate : target.Kind,
                panel.Mode, target.ItemIndex, panel, actions));
            if (orderItem || orderDetail) enabled = true;
            bool hovered = ring.Open && ring.HoveredSlot == i;
            bool pending = panel.ContextualPending && item && panel.SelectedIndex == target.ItemIndex;
            Vector4 fill = RadialActionVisuals.Background(enabled, hovered, ring.Pressed && hovered, pending, opacity);
            RingSegment(geometry, i, fill, width, height);
            if (activeStockField && !hovered)
                RingHoverArc(geometry, i, new Vector4(.3f, .8f, 1, opacity),
                    RadialActionVisuals.HoverArcWidth(scale), width, height);
            if (hovered)
                RingHoverArc(geometry, i, RadialActionVisuals.HoverArc(enabled, ring.Pressed, pending, opacity),
                    RadialActionVisuals.HoverArcWidth(scale), width, height);
            Vector2 center = geometry.LabelCenter(i);
            float iconSize = 20 * scale, halfWidth = geometry.LabelHalfWidth;
            var icon = mode ? PlayerActionDockHudModel.ResolveModeIcon(target.Mode) :
                item ? PlayerActionDockHudModel.ResolveItemIcon(panel.Mode, target.ItemIndex, actions) :
                PlayerActionDockHudModel.ResolveModeIcon(panel.Mode);
            _skin.Icon(icon, center.X - iconSize / 2, center.Y - 28 * scale, iconSize,
                new Vector4(enabled ? 1 : .55f, enabled ? 1 : .55f, enabled ? 1 : .55f, opacity));
            bool unavailableGlobal = ring.Global && slot.Key.Kind == 255;
            string label = coreStock && target.Kind == PlayerActionDockControlKind.Activate ? "APPLY STOCK" :
                coreStock && target.Kind == PlayerActionDockControlKind.Cancel ? "REMOVE RULE" : orderDetail ? i switch
            {
                0 => "ORDER ID", 1 => "STATUS", 2 => "RESOURCE", 3 => "QUANTITY",
                4 => "SOURCE", _ => "DESTINATION"
            } : orderItem ? "ORDER" : unavailableGlobal ? "No target" : slot.Navigation switch
            {
                ActionRingNavigation.Group => "Group",
                ActionRingNavigation.GroupControl =>
                    target.ItemIndex == 11 && ring.Group?.CanCreateCargoConvoy == true
                        ? "Create Convoy" : GroupActionRingContext.Label(target.ItemIndex),
                ActionRingNavigation.Back => "Back", ActionRingNavigation.Close => "Close",
                ActionRingNavigation.Next => "Next", ActionRingNavigation.Previous => "Prev",
                ActionRingNavigation.Parameters => "Params", ActionRingNavigation.NextItem => "Next item",
                _ => mode ? PlayerActionDockHudModel.ResolveModeLabel(target.Mode) :
                    item ? PlayerActionDockHudModel.ResolveRingItemTitle(panel.Mode, target.ItemIndex, actions, geometry.LabelHalfWidth < 38 * scale) :
                    target.Kind == PlayerActionDockControlKind.Cancel &&
                    panel.Mode == PlayerActionPanelMode.Logistics &&
                    actions?.Logistics?.SelectedConvoy is not null ? "DISBAND" :
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
                else { value.Append(target.ItemIndex >= 8 ? tactical.FirePolicyEligibleCount : target.ItemIndex is 5 or 6 ? tactical.Artillery.Count : tactical.CombatEligibleCount); value.Append("/");
                    value.Append(participants.SelectedCount); value.Append(geometry.LabelHalfWidth < 38 * scale ? " elig" : " eligible"); }
            }
            else if (orderDetail && actions?.Logistics is { } details)
                AppendOrderDetail(ref value, details.Orders[panel.SelectedIndex - details.OrderStartIndex], i);
            else if (orderItem && actions?.Logistics is { } itemOrders)
            {
                var order = itemOrders.Orders[target.ItemIndex - itemOrders.OrderStartIndex];
                value.Append("#"); value.Append(order.RequestId.Value);
                value.Append(" "); value.Append(OrderState(order.State));
            }
            else if (coreStock && bandControl && actions?.Logistics is { } bandStock)
            {
                if (activeStockField) value.Append("EDIT ");
                if (!enabled) value.Append(PlayerActionDockHudModel.DecideBandAdjustment(panel, bandStock, target.Kind).Reason);
                else value.Append(target.Kind is PlayerActionDockControlKind.DecreaseMinimum or PlayerActionDockControlKind.IncreaseMinimum
                    ? panel.StockMinimum : panel.StockMaximum, "F0");
            }
            else if (coreStock && target.Kind == PlayerActionDockControlKind.Activate)
            {
                value.Append(panel.StockConflict ? "REVIEW / MIN " : "MIN "); value.Append(panel.StockMinimum, "F0");
                value.Append(" / MAX "); value.Append(panel.StockMaximum, "F0");
            }
            else if (coreStock && target.Kind == PlayerActionDockControlKind.Cancel)
                value.Append("AUTO REFILL");
            else if (!navigation && !mode && !item && panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics is { } requirements && panel.SelectedIndex >= requirements.Policies.Count &&
                target.Kind is PlayerActionDockControlKind.Decrease or PlayerActionDockControlKind.Increase)
            { value.Append("STEPS "); value.Append(panel.LogisticsSteps); }
            else if (!navigation && !mode && !item && !enabled &&
                panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics is { } stock &&
                panel.SelectedIndex < stock.Policies.Count &&
                target.Kind is PlayerActionDockControlKind.Decrease or PlayerActionDockControlKind.Increase)
            {
                var reason = PlayerActionDockHudModel.DecideStockAdjustment(panel, stock,
                    target.Kind == PlayerActionDockControlKind.Increase ? 1 : -1);
                value.Append(reason.Reason);
            }
            else if (!navigation && !mode && !item) AppendRingParameter(ref value, target.Kind, panel);
            else if (mode && target.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics?.SelectedConvoy is { } convoy)
            {
                value.Append("CVY ");
                value.Append(convoy.TruckCount);
                value.Append(" ");
                value.Append(PlayerActionDockHudModel.ResolveCargoConvoyStatusLabel(convoy.Status));
            }
            else if (mode && target.Mode == PlayerActionPanelMode.Logistics && actions?.Logistics is { } logisticsSummary)
            { value.Append("ORDERS "); value.Append(logisticsSummary.TotalOrderCount); }
            else if (mode) value.Append(PlayerActionDockHudModel.ResolveModeShortcut(target.Mode, panel.Bindings));
            else if (item && panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.BridgeRepairs.Count > 0 && target.ItemIndex < actions.BridgeRepairs.Count)
            {
                var repair = actions.BridgeRepairs[target.ItemIndex];
                value.Append("HP "); value.Append((int)repair.Health); value.Append("/");
                value.Append((int)repair.MaximumHealth); value.Append(" / ");
                value.Append(repair.Paid ? "KIT PAID" : "120 ST / 20 EL");
            }
            else if (item && panel.Mode == PlayerActionPanelMode.Logistics &&
                actions?.Logistics is { } stockRows &&
                target.ItemIndex >= 0 && target.ItemIndex < stockRows.Policies.Count)
            {
                // The entry describes accepted simulation state, not the
                // separately editable PRIOR value on the parameter page.
                var stockPolicy = stockRows.Policies[target.ItemIndex];
                if (stockRows.IsCommandCore)
                {
                    value.Append("HAVE "); value.Append(stockPolicy.CurrentQuantity, "F0");
                    if (!stockPolicy.HasPolicy) value.Append(" / SET MIN MAX");
                    else if (!stockPolicy.Enabled) value.Append(" / OFF");
                    else if (stockPolicy.FailureReason != ForgeLine.Game.LogisticsTransportRequestFailureReason.None)
                    { value.Append(" / "); value.Append(PlayerActionDockHudModel.ResolveTransportFailureLabel(stockPolicy.FailureReason)); }
                    else { value.Append(" / "); value.Append(PlayerActionDockHudModel.ResolveDistributionStateLabel(stockPolicy.DistributionState)); }
                    _text.AddCentered(value.Written, new(center.X - halfWidth, center.Y + 12 * scale, halfWidth * 2, 16 * scale),
                        12 * scale, text, width, height);
                    continue;
                }
                value.Append(PlayerActionDockHudModel.ResolveLogisticsPriorityLabel(stockPolicy.Priority));
                if (stockPolicy.DispatchPriority != stockPolicy.Priority &&
                    stockPolicy.Enabled)
                {
                    value.Append(">");
                    value.Append(PlayerActionDockHudModel.ResolveLogisticsPriorityLabel(stockPolicy.DispatchPriority));
                }
                if (!stockPolicy.Enabled) value.Append(" / OFF");
                else if (stockPolicy.FailureReason != ForgeLine.Game.LogisticsTransportRequestFailureReason.None)
                {
                    value.Append(" / ");
                    value.Append(PlayerActionDockHudModel.ResolveTransportFailureLabel(stockPolicy.FailureReason));
                }
                else if (stockPolicy.DistributionState != PlayerDistributionActionState.None)
                {
                    value.Append(" / ");
                    value.Append(PlayerActionDockHudModel.ResolveDistributionStateLabel(stockPolicy.DistributionState));
                }
                else value.Append(" / IDLE");
            }
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

    private static string OrderState(ForgeLine.Game.LogisticsTransportRequestState state) => state switch
    {
        ForgeLine.Game.LogisticsTransportRequestState.Pending => "PENDING",
        ForgeLine.Game.LogisticsTransportRequestState.Assigned => "ASSIGNED",
        ForgeLine.Game.LogisticsTransportRequestState.InTransit => "IN TRANSIT",
        ForgeLine.Game.LogisticsTransportRequestState.RetryPending => "RETRY",
        ForgeLine.Game.LogisticsTransportRequestState.Failed => "FAILED",
        _ => "UNKNOWN"
    };

    private static void AppendOrderDetail(ref HudTextBuilder value, in PlayerLogisticsOrderReadModel order, int slot)
    {
        switch (slot)
        {
            case 0:
                value.Append("#"); value.Append(order.RequestId.Value);
                break;
            case 1:
                value.Append(OrderState(order.State));
                if (order.FailureReason != ForgeLine.Game.LogisticsTransportRequestFailureReason.None)
                {
                    value.Append(" ");
                    value.Append(PlayerActionDockHudModel.ResolveTransportFailureLabel(order.FailureReason));
                }
                break;
            case 2:
                value.Append(order.ResourceName);
                break;
            case 3:
                value.Append(order.RequestedQuantity, "F0");
                value.Append(" / LOAD "); value.Append(order.LoadedQuantity, "F0");
                if (order.AssignedTruck.IsValid)
                { value.Append(" TRK "); value.Append(order.AssignedTruck.Index); }
                break;
            case 4:
                value.Append(order.OriginName);
                break;
            case 5:
                value.Append(order.DestinationName);
                break;
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
    private void RingHoverArc(in RadialActionLayout geometry, int slot, Vector4 color, float thickness, int width, int height)
    {
        const int steps = 20;
        float outer = geometry.OuterRadius - 1;
        float inner = MathF.Max(geometry.InnerRadius + 1, outer - thickness);
        float start = geometry.Start(slot), end = geometry.End(slot);
        for (int i = 0; i < steps; i++)
        {
            float a = start + (end - start) * i / steps;
            float b = start + (end - start) * (i + 1) / steps;
            Vector2 da = RadialActionLayout.Direction(a), db = RadialActionLayout.Direction(b), c = geometry.Center;
            RingQuad(c + da * inner, c + da * outer, c + db * outer, c + db * inner,
                color, width, height);
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
