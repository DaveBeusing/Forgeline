using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal sealed partial class OperationsHudSurface : IGameplayHudSurface
{
    internal const int MaxVertices = 262_144;
    private const int MaxLines = 24;
    private readonly IGraphicsDevice _graphics;
    private readonly UiTextBatch _text;
    private readonly IGraphicsPipeline _pipeline;
    private readonly GameplayHudSkin _skin;
    private readonly Dictionary<int, IGraphicsBuffer> _buffers = new(4);
    private readonly OperationsVertex[] _vertices = new OperationsVertex[MaxVertices];
    private readonly NotificationDetailText _notificationDetails = new();
    private readonly OperationsRuleDetailText _ruleDetails = new();
    private readonly OperationsPanelMotion _motion = new();
    private int _count, _line, _width, _height;
    private HudRect _bounds;
    private float _scale;
    private float _panelOpacity;
    private bool _disposed;
    public OperationsHudSurface(IGraphicsDevice graphics, RuntimeAssetCatalog? runtimeAssets = null)
    {
        _graphics = graphics; _text = new(graphics); _pipeline = HudOverlayPrimitives.CreatePipeline(graphics, alphaBlendEnabled: true);
        try { _skin = new(graphics, runtimeAssets, icons: true); }
        catch { _text.Dispose(); _pipeline.Dispose(); throw; }
    }
    public GameplayHudRegion Regions => GameplayHudRegion.SecondaryView | GameplayHudRegion.AlertStack;
    public UiTextDiagnostics TextDiagnostics => _text.Diagnostics;
    public GameplayHudSkinDiagnostics SkinDiagnostics => _skin.Diagnostics;
    public int LastRenderedVertexCount { get; private set; }
    internal float LastPanelTextSize { get; private set; }
    internal float LastPanelOpacity => _panelOpacity;
    private float _minimumTextScale;
    private bool _panelText;
    public void Render(in GameplayHudRenderContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastRenderedVertexCount = 0; LastPanelTextSize = 0; _panelText = false; _text.Begin();
        _minimumTextScale = MathF.Min(1, context.Layout.Scale);
        _width = context.Graphics.Width; _height = context.Graphics.Height;
        if (_width <= 0 || _height <= 0) return;
        _skin.Begin(context.Graphics);
        _count = 0; _line = 0;
        _scale = OperationsLayout.Scale(context.Layout);
        _bounds = OperationsLayout.Panel(context.Layout);
        bool panelVisible = context.Operations.Open && !context.Operations.Suppressed &&
            context.Operations.Session == context.Snapshot.SessionId;
        bool activePresentation = context.AnimateOperations && !context.PreAlphaUx.Placement.Visible;
        _panelOpacity = _motion.Update(panelVisible, context.Snapshot.SessionId,
            context.Layout, context.PresentationTime, activePresentation);
        var view = context.Operations;
        if (_panelOpacity > 0 && !panelVisible)
        {
            HudRect departing = OperationsLayout.Panel(context.Layout);
            if (!departing.IsEmpty)
            {
                if (!_skin.Panel(departing, "panel.primary", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
                    Quad(departing, GameplayHudVisualStyle.PanelBackground with
                        { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
            }
        }
        if (panelVisible)
        {
            _scale = OperationsLayout.Scale(context.Layout);
            _panelText = true;
            LastPanelTextSize = MathF.Max(14 * _minimumTextScale, 14 * _scale);
            _bounds = OperationsLayout.Panel(context.Layout); _line = 0;
            if (_scale > 0)
            {
                if (_panelOpacity > 0 &&
                    !_skin.Panel(_bounds, "panel.primary", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
                    Quad(_bounds, GameplayHudVisualStyle.PanelBackground with
                        { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
                if (view.TemplateControls is { } templates) RenderTemplates(context, templates);
                else if (view.RuleControls is { } rules) RenderRules(context, rules);
                else if (view.Notifications) RenderNotificationDetails(context);
                else
                {
                var header = new HudRect(_bounds.X, _bounds.Y, _bounds.Width, 24 * _scale);
                if (_panelOpacity > 0 &&
                    !_skin.Panel(header, "panel.raised", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
                    Quad(header, GameplayHudVisualStyle.PanelHeader with
                        { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
                Quad(new HudRect(_bounds.X, _bounds.Y, 3 * _scale, _bounds.Height), GameplayHudVisualStyle.Focus);
                Line("OPERATIONS", true);
                Control(context.Layout, 0, "CLOSE");
                Control(context.Layout, 14, "FLOW >", selected: view.MeasurementFocused);
                Control(context.Layout, 12, view.Notifications ? "FACILITIES" : "NOTIFICATIONS");
                var data = OperationsSnapshot.Resolve(context.Snapshot);
                if (data is null) Line("WAITING FOR CURRENT OWNED DATA");
                else
                {
                    Span<char> text = stackalloc char[192];
                    var b = new OperationsTextBuilder(text);
                    b.Append("FACILITIES "); b.Number(data.FacilityCount, "0"); b.Append(" LISTED "); b.Number(data.Facilities.Count, "0");
                    b.Append(" ROUTES "); b.Number(data.RouteCount, "0");
                    if (OperationsCauseSnapshot.Resolve(context.Snapshot) is { } causes)
                    { b.Append(" GROUPS "); b.Number(causes.Groups.Count, "0"); if (causes.Limited) b.Append("+"); }
                    Line(b.Written);
                    b = new(text); b.Append("INPUT WINDOW "); b.Number(ForgeLine.Game.GameplayProgressJournal.OperationsWindowTicks, "0");
                    b.Append("t / BOUNDED");
                    var observed = OperationsDiagnosisSnapshot.Resolve(context.Snapshot)?.Observation;
                    if (observed is { Limited: true }) b.Append(" / LIMITED SUBJECTS");
                    Line(b.Written);
                    if (!RenderObservation(context, data, view, text))
                    {
                    for (int i = 0; i < Math.Min(6, data.Resources.Count); i += 2)
                    {
                        b = new(text);
                        for (int j = i; j < Math.Min(i + 2, data.Resources.Count); j++)
                        { b.Append(data.Resources[j].Name); b.Append(" "); b.Number(data.Resources[j].Quantity, "0"); b.Append("   "); }
                        Line(b.Written);
                    }
                    if (data.Resources.Count > 6)
                    { b = new(text); b.Append(data.Resources[6].Name); b.Append(" "); b.Number(data.Resources[6].Quantity, "0"); Line(b.Written); }
                    }
                    _line = 0;
                    Control(context.Layout, 1, view.Filter switch
                    {
                        OperationsCategory.Production => "PRODUCTION",
                        OperationsCategory.Logistics => "LOGISTICS",
                        OperationsCategory.Supply => "SUPPLY",
                        OperationsCategory.Power => "POWER",
                        OperationsCategory.Blocked => "BLOCKED",
                        _ => "ALL FACILITIES"
                    });
                    Control(context.Layout, 2, "NEXT PAGE");
                    for (int i = 0; i < OperationsLayout.PageSize; i++)
                    {
                        b = new(text);
                        if (OperationsLayout.Row(data, view, i) is { } row)
                        { b.Append(row.Entity == view.Selected ? "> " : "  "); b.Append(row.Name); b.Append(" / "); b.Append(row.Cause.Length > 0 ? row.Cause : row.Status); b.Append(" / Q "); b.Number(row.QueueCount, "0"); }
                        else b.Append(i == 0 ? "NO MATCHING FACILITIES" : "");
                        var facility = OperationsLayout.Row(data, view, i);
                        Control(context.Layout, i + 3, b.Written,
                            facility is { } active && active.Entity == view.Selected,
                            facility is { } blocked && blocked.Cause.Length > 0,
                            facility.HasValue, facility is { } f ? CategoryIcon(f.Category) : null);
                    }
                    var panel = _bounds; _line = 0;
                    _bounds = new(panel.X, panel.Y + 284 * _scale, panel.Width, 56 * _scale);
                    if (_panelOpacity > 0)
                    {
                        if (!_skin.Panel(_bounds, "panel.inset", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
                            Quad(_bounds, GameplayHudVisualStyle.PanelBackground with
                                { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
                    }
                    OperationsFacility? selected = null;
                    for (int rowIndex = 0; rowIndex < data.Facilities.Count; rowIndex++)
                        if (data.Facilities[rowIndex].Entity == view.Selected) { selected = data.Facilities[rowIndex]; break; }
                    if (selected is { } detail)
                    {
                        var currentDiagnosis = OperationsDiagnosisSnapshot.Resolve(context.Snapshot);
                        var diagnosis = OperationsActionGuidance.ForEntity(currentDiagnosis, detail.Entity);
                        var transport = OperationsActionGuidance.TransportForEntity(currentDiagnosis, detail.Entity);
                        string nextAction = diagnosis is { } current
                            ? OperationsActionGuidance.NextAction(current.PrimaryReason) : string.Empty;
                        if (diagnosis is { } issue && nextAction.Length > 0)
                        {
                            b = new(text); b.Append("CAUSE "); b.Append(OperationsDiagnosisSnapshot.Label(issue.PrimaryReason));
                            if (issue.Resource.IsSpecified)
                                for (int resourceIndex = 0; resourceIndex < data.Resources.Count; resourceIndex++)
                                    if (data.Resources[resourceIndex].Resource == issue.Resource)
                                    { b.Append(" / "); b.Append(data.Resources[resourceIndex].Name); break; }
                            b.Append(" / NEXT "); b.Append(nextAction); Line(b.Written);
                        }
                        else Line(detail.Explanation);
                        b = new(text);
                        if (diagnosis is { Resource.IsSpecified: true, LocalQuantity: not null } local)
                        {
                            b.Append("LOCAL "); Number(ref b, local.LocalQuantity);
                            b.Append(" FREE "); Number(ref b, local.FreeQuantity);
                            b.Append(" RESERVED "); Number(ref b, local.ReservedQuantity);
                        }
                        else { b.Append("STOCK "); Number(ref b, detail.InventoryQuantity); b.Append(" / "); Number(ref b, detail.InventoryCapacity); }
                        b.Append(" POWER "); Number(ref b, detail.AllocatedPower); b.Append(" / "); Number(ref b, detail.PowerDemand);
                        if (detail.GenerationCapacity.HasValue)
                        { b.Append(" GEN "); Number(ref b, detail.GenerationCapacity); }
                        if (detail.Utilization.HasValue)
                        { b.Append(" USE "); Number(ref b, detail.Utilization, "P0"); }
                        Line(b.Written);
                        int routes = 0, unavailable = 0;
                        for (int routeIndex = 0; routeIndex < data.Routes.Count; routeIndex++)
                        {
                            var route = data.Routes[routeIndex];
                            if (route.Source == detail.Entity || route.Destination == detail.Entity)
                            { routes++; if (!route.Enabled) unavailable++; }
                        }
                        b = new(text);
                        bool hasRelated = OperationsCauseSnapshot.Resolve(context.Snapshot)?.NextRelatedMember(detail.Entity).IsValid == true;
                        if (currentDiagnosis is { Root.IsValid: true } chain)
                        {
                            b.Append(OperationsActionGuidance.NextChainEntity(chain, detail.Entity).IsValid ? "TRACE NEXT / EXTRA BRANCHES " :
                                hasRelated ? "RELATED > / EXTRA BRANCHES " : "TRACE END / EXTRA BRANCHES "); b.Number(chain.Branches, "0");
                            if (transport is { PrimaryReason: not (OperationsDiagnosisReason.None or OperationsDiagnosisReason.Unknown) } transportBlock)
                            { b.Append(" / "); b.Append(OperationsDiagnosisSnapshot.Label(transportBlock.PrimaryReason)); }
                            b.Append(" OMITTED "); b.Number(chain.OmittedSteps + chain.TotalLinks - chain.Links.Count + chain.TotalRows - chain.Rows.Count, "0");
                            if (OperationsActionGuidance.SourceForEntity(chain, detail.Entity) is { } source)
                            { b.Append(" / SOURCE FREE "); Number(ref b, source.SourceFreeQuantity); }
                            if (chain.HasCycle) b.Append(" / CYCLE");
                            if (chain.MissingObservation) b.Append(" / NOT OBSERVED");
                            Control(context.Layout, 11, b.Written,
                                enabled: OperationsActionGuidance.NextChainEntity(chain, detail.Entity).IsValid || hasRelated);
                        }
                        else if (hasRelated)
                            Control(context.Layout, 11, "SHARED CAUSE / RELATED >", enabled: true);
                        else if (transport is { } reported)
                        {
                            b.Append("STATE "); b.Append(reported.Status);
                            if (reported.OutstandingRequestQuantity.HasValue)
                            { b.Append(" REQUEST "); Number(ref b, reported.OutstandingRequestQuantity); }
                            if (reported.ConfirmedIncomingQuantity.HasValue)
                            { b.Append(" CONFIRMED "); Number(ref b, reported.ConfirmedIncomingQuantity); }
                            if (reported.LoadedCargoQuantity.HasValue)
                            { b.Append(" LOADED "); Number(ref b, reported.LoadedCargoQuantity); }
                            Line(b.Written);
                        }
                        else if (detail.Service.LoadingQueue is { } loading)
                        { b.Append(LogisticsStatusCatalog.Service(detail.Service)); b.Append(" / WAITING "); b.Number(loading.WaitingCount, "0"); Line(b.Written); }
                        else if (detail.Service.VehicleQueue is { } vehicle)
                        { b.Append(LogisticsStatusCatalog.Service(detail.Service)); b.Append(" / POSITION "); b.Number(vehicle.Position, "0"); Line(b.Written); }
                        else
                        {
                        b.Append("LISTED LINKS "); b.Number(routes, "0"); b.Append(" DISABLED "); b.Number(unavailable, "0");
                        b.Append(" NODE CAP/S "); Number(ref b, detail.TransportCapacity); b.Append(" / FOCUS NEXT"); Line(b.Written);
                        }
                    }
                    else Line("SELECT A FACILITY FOR CAUSE AND CONTROLS");
                    _bounds = panel;
                    Control(context.Layout, 13, "RULES", selected: view.RuleEntryFocused);
                    Control(context.Layout, 15, "TEMPLATES", selected: view.TemplateEntryFocused);
                    Control(context.Layout, 9, selected.HasValue ? "FOCUS FACILITY" : "SELECT FACILITY TO FOCUS", enabled: selected.HasValue);
                    bool controls = selected.HasValue && selected.Value.Controls != PlayerActionPanelMode.Closed;
                    Control(context.Layout, 10, controls ? "OPEN EXISTING CONTROLS" : "NO FACILITY CONTROLS", enabled: controls);
                }
                }
            }
        }
        if (_count == 0 && _skin.Vertices == 0 && _text.VertexCount == 0) return;
        IGraphicsBuffer? frameBuffer = null;
        if (_count > 0)
        {
            if (!_buffers.TryGetValue(context.Graphics.FrameIndex, out var buffer))
            { buffer = _graphics.CreateBuffer(new GraphicsBufferDescription(MaxVertices * 24UL, GraphicsBufferMemory.Upload)); _buffers.Add(context.Graphics.FrameIndex, buffer); }
            buffer.SetData<OperationsVertex>(_vertices.AsSpan(0, _count));
            frameBuffer = buffer;
         }
        _skin.Flush(context.Graphics);
        if (frameBuffer is not null && _count > 0)
        { context.Graphics.SetPipeline(_pipeline); context.Graphics.SetVertexBuffer(frameBuffer, 24); context.Graphics.Draw(_count); }
        _text.Render(context.Graphics);
        LastRenderedVertexCount = _count + _text.VertexCount + _skin.Vertices;
    }
    private bool RenderObservation(in GameplayHudRenderContext context, OperationsSnapshot data, in OperationsView view, Span<char> text)
    {
        if (!view.Selected.IsValid) return false;
        var diagnosis = OperationsDiagnosisSnapshot.Resolve(context.Snapshot);
        if (OperationsMeasurementDisplay.Find(diagnosis, view.Selected, view.MeasurementPage) is not { } flow)
        { Line("INPUT FLOW / WAIT / BACKLOG NOT OBSERVED"); Line("SELECT OWNED FACILITY AND KEEP OPERATIONS OPEN"); return true; }
        var b = new OperationsTextBuilder(text);
        string resource = "UNKNOWN RESOURCE";
        for (int i = 0; i < data.Resources.Count; i++)
            if (data.Resources[i].Resource == flow.Resource) { resource = data.Resources[i].Name; break; }
        b.Append(resource); b.Append(" DEL "); Number(ref b, flow.DeliveredQuantity, "0.##");
        b.Append(" / "); Number(ref b, flow.DeliveredPerSecond, "0.##"); b.Append("/s"); Line(b.Written);
        b = new(text); b.Append("COVER "); b.Number(flow.CoveredTicks, "0"); b.Append("/600t ");
        b.Number(flow.Seconds, "0.##"); b.Append("s WAIT "); Number(ref b, flow.WaitingFraction, "P0");
        b.Append(" ("); b.Number(flow.WaitObservedTicks, "0"); b.Append("t)"); Line(b.Written);
        b = new(text); b.Append("BACKLOG "); Number(ref b, flow.Backlog, "0.##");
        b.Append(" ["); Number(ref b, flow.MinimumBacklog, "0.##"); b.Append(".."); Number(ref b, flow.MaximumBacklog, "0.##");
        b.Append("] "); b.Number(flow.BacklogObservedTicks, "0"); b.Append("t"); Line(b.Written);
        var current = OperationsMeasurementDisplay.Current(diagnosis, flow);
        b = new(text); b.Append("REQ "); Number(ref b, current?.OutstandingRequestQuantity);
        b.Append(" RSV "); Number(ref b, current?.ReservedQuantity);
        b.Append(" CONF "); Number(ref b, current?.ConfirmedIncomingQuantity);
        b.Append(" LOAD "); Number(ref b, OperationsMeasurementDisplay.Loaded(diagnosis, flow));
        Line(b.Written);
        return true;
    }

    private void RenderRules(in GameplayHudRenderContext context, in OperationsRulePanelView view)
    {
        Line("RULES", true);
        var panel = _bounds;
        _bounds = panel with { Y = panel.Y + 28 * _scale, Height = 24 * _scale };
        _line = 0;
        bool compact = panel.Width < 320 * MathF.Min(1, context.Layout.Scale);
        Line(view.Data is null ? compact ? "SELECT PROCESSOR" : "SELECT ONE OWNED PROCESSOR" :
            compact ? "OWN PROCESSOR" : "SELECTED PROCESSOR / LOCAL DRAFT");
        _bounds = panel;
        for (int i = 0; i < OperationsRulePanelLayout.ControlCount; i++)
        {
            var bounds = OperationsRulePanelLayout.Control(context.Layout, i);
            bool enabled = OperationsRulePanelController.CanUse(i, view);
            bool focused = view.Focus == i;
            if (!_skin.Panel(bounds, "control.button_compact", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
                Quad(bounds, (focused ? GameplayHudVisualStyle.PanelSelected : GameplayHudVisualStyle.PanelRaised)
                    with { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
            if (focused) Quad(bounds with { Width = 2 * _scale }, GameplayHudVisualStyle.Focus);
            string label = i switch
            {
                0 => "BACK",
                1 => view.NewDraft && compact ? "NEW / NEXT RULE" : OperationsRulePanelController.SelectorLabel(view),
                2 => view.Draft.Template == ForgeLine.Game.OperationsRuleTemplate.PauseUntilOutputAvailable ? compact ? "OUTPUT FULL" : "OUTPUT RECOVERY" : compact ? "INPUT STOCK" : "INPUT STOCK POLICY",
                3 => ResourceLabel(view),
                4 => OperationsRulePanelController.FieldLabel(view),
                5 => compact ? "-" : "DECREASE",
                6 => compact ? "+" : "INCREASE",
                7 => view.Pending ? "PENDING" : "APPLY",
                8 => view.NewDraft ? view.DraftEnabled ? compact ? "ON" : "DRAFT ENABLED" : compact ? "OFF" : "DRAFT DISABLED" : view.Observed is { Enabled: true } ? "DISABLE" : "ENABLE",
                9 => "REMOVE",
                10 => view.NewDraft ? "RESET" : "RELOAD",
                _ => compact ? "DETAILS / NEXT" : "DETAILS / NEXT PAGE"
            };
            if (!_text.AddCentered(label, bounds, LastPanelTextSize,
                enabled ? GameplayHudVisualStyle.TextPrimary : GameplayHudVisualStyle.TextSecondary, _width, _height))
            { _bounds = bounds; _line = 0; Line(label, enabled); _bounds = panel; }
        }
        var detail = OperationsRulePanelLayout.Details(context.Layout);
        _ruleDetails.Update(view, MathF.Max(1, detail.Width - 16 * _scale), LastPanelTextSize);
        _bounds = detail; _line = 0;
        foreach (string line in _ruleDetails.Lines) Line(line, true);
        _bounds = panel;
    }
    private static string ResourceLabel(in OperationsRulePanelView view)
    {
        if (view.Draft.Template == ForgeLine.Game.OperationsRuleTemplate.PauseUntilOutputAvailable) return "REAL OUTPUT CAPACITY";
        if (view.Data is not null)
            foreach (var input in view.Data.Inputs) if (input.ResourceId == view.Draft.Resource) return input.DisplayName;
        return "QUEUE AN INPUT RECIPE";
    }

    private void RenderNotificationDetails(in GameplayHudRenderContext context)
    {
        LastPanelTextSize = OperationsLayout.NotificationTextSize(context.Layout);
        _scale = LastPanelTextSize / 14;
        var panel = _bounds;
        var close = OperationsLayout.NotificationControl(context.Layout, 0);
        var title = new HudRect(panel.X + 8, close.Y, MathF.Max(0, close.X - panel.X - 16), close.Height);
        _text.AddCentered("NOTIFICATIONS", title, LastPanelTextSize, GameplayHudVisualStyle.TextPrimary, _width, _height, true);
        NotificationControl(context.Layout, 0, "CLOSE");
        NotificationControl(context.Layout, 1, "FACILITIES");
        NotificationControl(context.Layout, 2, "NEXT PAGE");
        var row = OperationsLayout.NotificationControl(context.Layout, 3);
        int rows = OperationsLayout.NotificationRows(context.Layout);
        _notificationDetails.Update(context.Snapshot, MathF.Max(1, row.Width), LastPanelTextSize,
            context.PreAlphaUx.Placement.Visible ? "" : context.PreAlphaUx.InteractionHint, context.Operations.Page, rows);
        for (int i = 0; i < rows; i++)
        {
            row = OperationsLayout.NotificationControl(context.Layout, i + 3);
            _text.Add(_notificationDetails.Lines[i], row.X, row.Y + 2, LastPanelTextSize,
                GameplayHudVisualStyle.TextPrimary, _width, _height, row.Width);
        }
    }
    private void NotificationControl(in GameplayHudLayout layout, int index, string label)
    {
        var bounds = OperationsLayout.NotificationControl(layout, index);
        if (!_skin.Panel(bounds, "control.button_compact", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
            Quad(bounds, GameplayHudVisualStyle.PanelRaised with { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
        _text.AddCentered(label, bounds, LastPanelTextSize, GameplayHudVisualStyle.TextPrimary, _width, _height);
    }

    private static void Number(ref OperationsTextBuilder builder, double? value, string format = "0")
    { if (value.HasValue && double.IsFinite(value.Value)) builder.Number(value.Value, format); else builder.Append("N/A"); }
    internal static RtsUiIcon CategoryIcon(OperationsCategory category) => category switch
    {
        OperationsCategory.Production => RtsUiIcon.BuildingFactory,
        OperationsCategory.Logistics => RtsUiIcon.UnitLogistics,
        OperationsCategory.Supply => RtsUiIcon.BuildingSupply,
        OperationsCategory.Power => RtsUiIcon.BuildingPower,
        _ => RtsUiIcon.StatusAlert
    };
    private void Control(in GameplayHudLayout layout, int index, ReadOnlySpan<char> label, bool selected = false, bool blocked = false,
        bool enabled = true, RtsUiIcon? icon = null)
    {
        var previous = _bounds; int line = _line;
        _bounds = OperationsLayout.Control(layout, index); _line = 0;
        if (_skin.Ready && label.IsEmpty) { _bounds = previous; _line = line; return; }
        if (!_skin.Panel(_bounds, "control.button_compact", OperationsPanelVisuals.BackgroundOpacity * _panelOpacity))
            Quad(_bounds, (selected ? GameplayHudVisualStyle.PanelSelected : GameplayHudVisualStyle.PanelRaised)
                with { W = OperationsPanelVisuals.BackgroundOpacity * _panelOpacity });
        if (selected || blocked)
        {
            var rail = new HudRect(_bounds.X, _bounds.Y, 2 * _scale, _bounds.Height);
            Quad(rail, blocked ? GameplayHudVisualStyle.Warning : GameplayHudVisualStyle.Focus);
            if (blocked) Quad(rail with { X = rail.X + 3 * _scale }, GameplayHudVisualStyle.Warning);
        }
        if (icon.HasValue && _skin.Icon(icon.Value, _bounds.X + 5 * _scale, _bounds.Y + 3 * _scale, 14 * _scale))
            _bounds = _bounds with { X = _bounds.X + 16 * _scale, Width = _bounds.Width - 16 * _scale };
        Line(label, enabled);
        _bounds = previous; _line = line;
    }
    private void Line(ReadOnlySpan<char> text, bool primary = false)
    {
        if (text.IsEmpty || _line >= MaxLines) return;
        float x = _bounds.X + 8 * _scale;
        float size = _panelText ? LastPanelTextSize : 12 * _scale;
        float leading = _panelText ? MathF.Max(15 * _minimumTextScale, 16 * _scale) : 14 * _scale;
        float y = _bounds.Y + (_panelText ? 4 : 2) * _scale + _line++ * leading;
        if (y + size > _bounds.Bottom - (_panelText ? 2 * _scale : 0)) return;
        int characters = Math.Min(68, Math.Max(0, (int)((_bounds.Width - 16 * _scale) / (6.4f * _scale))));
        var color = primary ? GameplayHudVisualStyle.TextPrimary : GameplayHudVisualStyle.TextSecondary;
        if (_text.Add(text, x, y, size, color, _width, _height,
            MathF.Max(0, _bounds.Right - 8f * _scale - x), bold: primary)) return;
        for (int i = 0; i < Math.Min(text.Length, characters); i++)
        {
            string pattern = HudOverlayPrimitives.TextGlyphPattern(char.ToUpperInvariant(text[i]));
            if (pattern.Length != 35) continue;
            for (int pixel = 0; pixel < 35; pixel++)
                if (pattern[pixel] == '1')
                    Quad(new HudRect(x + (i * 6.4f + pixel % 5) * _scale,
                        y + pixel / 5 * _scale, _scale, _scale), color);
        }
    }

    private void Quad(HudRect rect, Vector4 color)
    {
        if (_count > MaxVertices - 6) return;
        var a = new Vector2(rect.X / _width * 2 - 1, 1 - rect.Y / _height * 2);
        var b = new Vector2(rect.Right / _width * 2 - 1, 1 - rect.Bottom / _height * 2);
        _vertices[_count++] = new(a, color);
        _vertices[_count++] = new(b, color);
        _vertices[_count++] = new(new Vector2(b.X, a.Y), color);
        _vertices[_count++] = new(a, color);
        _vertices[_count++] = new(new Vector2(a.X, b.Y), color);
        _vertices[_count++] = new(b, color);
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var buffer in _buffers.Values) buffer.Dispose();
        _buffers.Clear();
        _text.Dispose();
        _skin.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct OperationsVertex(Vector2 Position, Vector4 Color);

    private ref struct OperationsTextBuilder(Span<char> buffer)
    {
        private readonly Span<char> _buffer = buffer;
        private int _length;
        public readonly ReadOnlySpan<char> Written => _buffer[.._length];
        public void Append(string value)
        {
            int length = Math.Min(value.Length, _buffer.Length - _length);
            value.AsSpan(0, length).CopyTo(_buffer[_length..]);
            _length += length;
        }
        public void Number(double value, string format)
        {
            if (!double.IsFinite(value)) { Append("N/A"); return; }
            if (value.TryFormat(_buffer[_length..], out int written, format, CultureInfo.InvariantCulture)) _length += written;
        }
    }
}
