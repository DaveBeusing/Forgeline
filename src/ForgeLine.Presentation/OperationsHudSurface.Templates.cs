namespace ForgeLine.Presentation;

internal sealed partial class OperationsHudSurface
{
    private readonly PolicyTemplateDetailText _templateDetails = new();

    private void RenderTemplates(in GameplayHudRenderContext context, PolicyTemplatePanelView view)
    {
        Line("POLICY TEMPLATES", true);
        var panel = _bounds;
        _bounds = panel with { Y = panel.Y + 28 * _scale, Height = 24 * _scale };
        _line = 0;
        Line(view.Dirty ? "LOCAL DRAFT / SAVE BEFORE PREVIEW" : view.Saved
            ? "SAVED LOCALLY / APPLY IS EXPLICIT" : "EMPTY LOCAL SLOT");
        _bounds = panel;
        var detail = OperationsRulePanelLayout.Details(context.Layout);
        _templateDetails.Update(view, MathF.Max(1, detail.Width - 16 * _scale), LastPanelTextSize);
        for (int i = 0; i < OperationsRulePanelLayout.ControlCount; i++)
        {
            var bounds = OperationsRulePanelLayout.Control(context.Layout, i);
            bool enabled = PolicyTemplatePanelController.CanUse(i, view), focused = view.Focus == i;
            if (!_skin.Panel(bounds, "control.button_compact", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
                Quad(bounds, (focused ? GameplayHudVisualStyle.PanelSelected : GameplayHudVisualStyle.PanelRaised)
                    with { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
            if (focused) Quad(bounds with { Width = 2 * _scale }, GameplayHudVisualStyle.Focus);
            string label = i switch
            {
                0 => "BACK",
                1 => $"SLOT {view.Slot + 1}/4 / NEXT",
                2 => view.Draft.Kind == PolicyTemplateKind.Stock ? "STOCK POLICY" : "PRODUCTION",
                3 => "COPY / NEXT SOURCE",
                4 => PolicyTemplatePanelController.FieldLabel(view),
                5 => "DECREASE", 6 => "INCREASE",
                7 => "SAVE SLOT", 8 => "PREVIEW",
                9 => view.Pending ? "PENDING" : view.Applying ? "APPLYING" : "APPLY PREVIEW",
                10 => "CLEAR SLOT", _ => $"DETAILS {_templateDetails.Page + 1}/{_templateDetails.PageCount} / NEXT"
            };
            if (!_text.AddCentered(label, bounds, LastPanelTextSize,
                enabled ? GameplayHudVisualStyle.TextPrimary : GameplayHudVisualStyle.TextSecondary, _width, _height))
            { _bounds = bounds; _line = 0; Line(label, enabled); _bounds = panel; }
        }
        _bounds = detail; _line = 0;
        foreach (string line in _templateDetails.Lines) Line(line, true);
        _bounds = panel;
    }
}
