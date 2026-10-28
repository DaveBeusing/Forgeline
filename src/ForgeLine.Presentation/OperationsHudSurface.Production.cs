using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal sealed partial class OperationsHudSurface
{
    private void RenderProduction(in GameplayHudRenderContext context, in GlobalProductionView view)
    {
        var layout = context.Layout;
        var panel = _bounds;
        var selection = context.InformationLayer.SelectedEntities;
        var data = GlobalProductionSnapshot.Resolve(context.Snapshot);
        ProductionControl(layout, view, 26, "OPS");
        ProductionControl(layout, view, 3, view.Collapsed ? "OPEN" : "FOLD");
        ProductionControl(layout, view, 0, "CLOSE");
        Span<char> text = stackalloc char[192];
        var b = new OperationsTextBuilder(text);
        int factories = 0, orders = 0;
        if (data is not null)
            for (int i = 0; i < data.Factories.Count; i++)
                if (GlobalProductionSnapshot.Matches(data.Factories[i], view.Filter, selection))
                { factories++; orders += data.Factories[i].QueueCount; }
        b.Append("QUEUES / "); b.Append(FilterLabel(view.Filter)); b.Append(" / "); b.Number(factories, "0");
        b.Append(" F / "); b.Number(orders,"0"); b.Append(" Q");
        if (data?.Limited == true) b.Append(" / LIMITED");
        ProductionLine(panel, 32, b.Written, true);
        if (view.Collapsed) return;
        ProductionControl(layout, view, 1, FilterLabel(view.Filter));
        ProductionControl(layout, view, 2, "PAGE");
        ProductionControl(layout, view, 4, "INFO");
        for (int i = 0; i < GlobalProductionLayout.PageSize; i++)
        {
            var factory = data is null ? null : GlobalProductionLayout.Row(data, view, i, selection);
            b = new(text);
            if (factory is not null)
            {
                b.Append(RoleLabel(factory.Capabilities)); b.Append(" #"); b.Number(factory.Entity.Index, "0"); b.Append(" / ");
                if (factory.Orders.Count > 0)
                { b.Number(factory.Progress * 100, "0"); b.Append("% / "); b.Append(ShortUnit(factory.Orders[0].Unit)); }
                else b.Append("IDLE");
                b.Append(" / Q "); b.Number(factory.QueueCount,"0");
            }
            else if (i == 0) b.Append(data is null ? "WAITING FOR OWNED QUEUES" : "NO MATCHING FACTORIES");
            ProductionControl(layout, view, i+10, b.Written, factory is not null,
                factory?.Entity == view.Factory, factory?.BlockReason is not (null or UnitProductionBlockReason.None));
        }
        var selected = data?.Find(view.Factory);
        var order = GlobalProductionLayout.Order(selected, view.Request);
        if (selected is null) ProductionLine(panel, 234, "SELECT FACTORY / DETAILS FOR INPUTS AND RALLY");
        else if (view.DetailPage > 0 && order is not null && view.DetailPage <= order.Materials.Count)
        {
            var material = order.Materials[view.DetailPage-1];
            b = new(text); b.Append(material.Name); b.Append(" / NEED "); b.Number(material.Required,"0.##");
            b.Append(order.InputsReserved ? " / HELD" : " / NOT HELD");
            ProductionLine(panel, 234, b.Written, true);
            b = new(text);
            var stock = selected.StockIndex >= 0 && selected.StockIndex < data!.Stocks.Count ? data.Stocks[selected.StockIndex] : null;
            if (stock is not { Available: true }) b.Append("FACTORY INVENTORY UNAVAILABLE");
            else
                for (int i=0;i<stock.Amounts.Count;i++)
                    if (stock.Amounts[i].Resource == material.Resource)
                    { b.Append("LOCAL FREE "); b.Number(stock.Amounts[i].Available,"0.##"); b.Append(" / ALL HELD "); b.Number(stock.Amounts[i].Reserved,"0.##"); break; }
            ProductionLine(panel, 252, b.Written);
            var sum = data!.Sum(material.Resource, view.Filter, selection);
            b = new(text); b.Append("KNOWN FREE "); b.Number(sum.Available,"0.##");
            b.Append(" HELD "); b.Number(sum.Reserved,"0.##"); 
            ProductionLine(panel, 270, b.Written);
        }
        else
        {
            b = new(text); if (order is null) b.Append(selected.Name);
            if (order is not null) { b.Append(order.Name); b.Append(" / "); b.Append(PriorityLabel(order.Priority)); }
            ProductionLine(panel, 234, b.Written, true);
            b = new(text); b.Append(selected.BlockReason == UnitProductionBlockReason.None ? StatusLabel(selected.Status) : BlockLabel(selected.BlockReason));
            if (order?.Paused == true) b.Append(" / ORDER PAUSED");
            b.Append(order?.InputsReserved == true ? " / INPUTS HELD" : " / INPUTS NOT HELD"); b.Append(" / ETA N/A");
            ProductionLine(panel, 252, b.Written);
            b = new(text);
            if (selected.RallyPoint is { } rally)
            { b.Append("RALLY "); b.Number(rally.X,"0.#"); b.Append(", "); b.Number(rally.Z,"0.#"); b.Append(" / ROUTE ON SPAWN"); }
            else b.Append("RALLY DEFAULT SPAWN / ROUTE ON SPAWN");
            ProductionLine(panel, 270, string.IsNullOrEmpty(view.Feedback) ? b.Written : view.Feedback.AsSpan());
        }
        bool visible = selected is not null && OperationsWorldOverlay.CanFocus(context.Snapshot, selected.Entity);
        ProductionControl(layout, view, 20, "FOCUS", visible);
        ProductionControl(layout, view, 21, "SELECT", visible);
        ProductionControl(layout, view, 22, "NEXT", selected?.Orders.Count > 1);
        ProductionControl(layout, view, 23, "CANCEL", order is not null);
        ProductionControl(layout, view, 24, "PRIO", order is not null);
        ProductionControl(layout, view, 25, "RALLY", visible);
        _bounds = panel;
    }

    private void ProductionControl(in GameplayHudLayout layout, in GlobalProductionView view, int index,
        ReadOnlySpan<char> text, bool enabled = true, bool selected = false, bool blocked = false) =>
        Control(layout, index, text, selected || view.Focus == index, blocked, enabled,
            bounds: GlobalProductionLayout.Control(layout, index));
    private void ProductionLine(HudRect panel, float y, ReadOnlySpan<char> text, bool primary = false)
    { var previous = _bounds; _bounds = new(panel.X, panel.Y+y*_scale, panel.Width, 22*_scale); _line=0; Line(text,primary); _bounds=previous; }
    private static string FilterLabel(GlobalProductionFilter filter) => filter switch
    { GlobalProductionFilter.Selected=>"SELECTED", GlobalProductionFilter.Infantry=>"INFANTRY", GlobalProductionFilter.Vehicles=>"VEHICLES", GlobalProductionFilter.Logistics=>"LOGISTICS", GlobalProductionFilter.Blocked=>"BLOCKED", _=>"ALL" };
    private static string ShortUnit(UnitId unit) => unit == UnitIds.MainBattleTank ? "MBT" : unit == UnitIds.ScoutVehicle ? "SCOUT" : unit == UnitIds.RifleSquad ? "RIFLE" : unit == UnitIds.CargoTruck ? "CARGO" : unit == UnitIds.SupplyTruck ? "SUPPLY" : "UNIT";
    private static string RoleLabel(UnitProductionCapability capability) => capability switch
    { UnitProductionCapability.Infantry=>"INF", UnitProductionCapability.Vehicle=>"VEH", UnitProductionCapability.Logistics=>"LOG", _=>"MIXED" };
    private static string StatusLabel(UnitProductionStatus status) => status switch
    { UnitProductionStatus.Running=>"RUNNING", UnitProductionStatus.NoInput=>"NO INPUT", UnitProductionStatus.NoPower=>"NO POWER", UnitProductionStatus.Paused=>"PAUSED", UnitProductionStatus.OutputBlocked=>"OUTPUT BLOCKED", _=>"IDLE" };
    private static string BlockLabel(UnitProductionBlockReason reason) => reason switch
    { UnitProductionBlockReason.NoInput=>"NO INPUT", UnitProductionBlockReason.NoPower=>"NO POWER", UnitProductionBlockReason.Paused=>"PAUSED", UnitProductionBlockReason.UnsupportedUnit=>"UNSUPPORTED UNIT", UnitProductionBlockReason.InvalidFacility=>"INVALID FACILITY", UnitProductionBlockReason.OutputBlocked=>"OUTPUT BLOCKED", _=>"UNKNOWN BLOCK" };
    private static string PriorityLabel(ForgeLine.Economy.ProductionPriority priority) => priority switch
    { ForgeLine.Economy.ProductionPriority.High=>"HIGH", ForgeLine.Economy.ProductionPriority.Low=>"LOW", _=>"NORMAL" };
}
