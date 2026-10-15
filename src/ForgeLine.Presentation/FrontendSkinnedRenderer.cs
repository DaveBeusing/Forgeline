using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public sealed partial class FrontendOverlayRenderer
{
    private static readonly Dictionary<string,string> SkinIds = new[] {
        "panel.primary", "panel.dialog", "panel.inset", "panel.banner", "panel.compact",
        "control.button", "control.tab", "control.row_compact", "control.row", "control.input", "control.dropdown",
        "control.toggle_track", "control.toggle_thumb", "control.slider_track", "control.slider_thumb", "control.button_compact",
        "control.scrollbar_track", "control.scrollbar_thumb", "control.separator", "progress.progress_track", "progress.progress_fill",
        "overlay.hover", "overlay.pressed", "overlay.focus", "overlay.selected", "overlay.disabled", "overlay.warning", "overlay.error"
    }.ToDictionary(role => role, role => "ui.skin.directorate." + role, StringComparer.Ordinal);
    private string? _skinFailure;
    public bool SkinReady => _skin is not null && _text.Ready;
    public string? SkinFailure => _skinFailure ?? _text.FailureReason;
    public int LastSkinnedControlCount { get; private set; }
    public string SkinState => SkinReady ? "Ready" : SkinFailure is not null ? "Failed" : "Bootstrap";

    public void PrepareSkin(RuntimeAssetCatalog catalog)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_graphics.SupportsUiText || _skin is not null || _skinFailure is not null) return;
        UiSkinResources? resources = null;
        try
        {
            resources = new(_graphics, catalog);
            // Required frontend roles fail at preparation, before any partial frame can be recorded.
            foreach (var id in SkinIds.Values) _ = resources.Sprite(id);
            _skin = new(_graphics, resources);
            _skinResources = resources;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or KeyNotFoundException or ArgumentException)
        {
            resources?.Dispose();
            _skinFailure = "Required UI skin unavailable. Reinstall the complete player. " + error.Message;
            Console.Error.WriteLine("[ui:skin:failed] " + _skinFailure);
        }
        catch { resources?.Dispose(); throw; }
    }

    private void RenderSkinned(IGraphicsCommandContext context, in FrontendSurfaceView view, FrontendGeometry layout)
    {
        var skin = _skin!;
        int width = context.Width, height = context.Height;
        float scale = layout.Scale;
        _vertexCount = 0; LastSkinnedControlCount = 0;
        _vertexLimit = MaxVertices;
        skin.Begin(width, height, context.FrameIndex);
        bool paused = view.Kind == FrontendSurfaceKind.PauseMenu || view.PauseContext;
        bool fullImage = !paused && view.Kind == FrontendSurfaceKind.MainMenu && _background?.Ready == true;
        if (fullImage) _background!.Render(context, layout.Background);

        // An opaque backing blocks underlying HUD text. Its skin and live content follow it.
        if (paused)
        {
            var plate = view.Kind == FrontendSurfaceKind.PauseMenu ? layout.PauseBackdrop : layout.SafeArea with { Width = MathF.Min(layout.SafeArea.Width, layout.Details.Right - layout.SafeArea.X + 20 * scale) };
            EmitPanel(new(plate.X, plate.Y, plate.Width, plate.Height),
                new(ForgeLineUiPalette.Surface.X, ForgeLineUiPalette.Surface.Y, ForgeLineUiPalette.Surface.Z, 1), width, height);
            EmitPanel(new(layout.Footer.X, layout.Footer.Y, layout.Footer.Width, layout.Footer.Height),
                new(ForgeLineUiPalette.Surface.X, ForgeLineUiPalette.Surface.Y, ForgeLineUiPalette.Surface.Z, 1), width, height);
            if (!string.IsNullOrEmpty(view.Feedback)) EmitPanel(new(layout.Feedback.X, layout.Feedback.Y, layout.Feedback.Width, layout.Feedback.Height),
                new(ForgeLineUiPalette.Surface.X, ForgeLineUiPalette.Surface.Y, ForgeLineUiPalette.Surface.Z, 1), width, height);
        }
        int backingVertices = _vertexCount;
        EmitBrandMark(layout.Header, MathF.Min(480 * scale, layout.Navigation.Width - 24 * scale), width, height);

        if (!paused) Panel(layout.Background, "panel.primary", fullImage ? .20f : 1);
        if (view.Kind is FrontendSurfaceKind.MainMenu or FrontendSurfaceKind.PauseMenu)
            Panel(layout.PauseBackdrop, "panel.primary");
        else Panel(paused ? layout.SafeArea with { Width = MathF.Min(layout.SafeArea.Width, layout.Details.Right - layout.SafeArea.X + 20 * scale) } : layout.SafeArea,
            view.Kind == FrontendSurfaceKind.Loading ? "panel.dialog" : "panel.primary");
        if (paused) Panel(layout.Footer, "panel.compact");

        if (view.Kind != FrontendSurfaceKind.Credits)
            Text("BUILD. SUPPLY. CONQUER.", layout.Header with { Y = layout.Header.Y + 72 * scale }, ForgeLineUiPalette.TextSecondary, .75f);
        Text(view.Title, layout.Title, ForgeLineUiPalette.Accent);
        if (view.Kind is FrontendSurfaceKind.MainMenu or FrontendSurfaceKind.PauseMenu)
        {
            for (int i = 0; i < Math.Min(7, view.MenuEntries.Count); i++)
            {
                var entry = view.MenuEntries[i]; var row = layout.MenuRow(i);
                Control(row, "control.button", entry.IsEnabled, entry.IsHovered, entry.IsPressed, entry.IsFocused);
                Text(s_menuNumbers[i], row with { X = row.X + 22 * scale, Y = row.Y + 15 * scale, Width = 40 * scale }, ForgeLineUiPalette.TextMuted, .76f);
                Text(entry.Label, row with { X = row.X + 76 * scale, Y = row.Y + 12 * scale, Width = row.Width - 220 * scale },
                    !entry.IsEnabled ? ForgeLineUiPalette.TextDisabled : entry.IsFocused ? ForgeLineUiPalette.TextPrimary : ForgeLineUiPalette.TextSecondary);
                if (entry.IsFocused || !entry.IsEnabled)
                    Text(!entry.IsEnabled ? "N/A" : "SELECT", row with { X = row.Right - 128 * scale, Y = row.Y + 15 * scale, Width = 110 * scale },
                        entry.IsEnabled ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.TextDisabled, .68f);
            }
        }
        else if (view.Kind == FrontendSurfaceKind.Loading)
        {
            Text("INITIALIZING COMMAND SYSTEMS", layout.Title, ForgeLineUiPalette.TextSecondary);
            var status = layout.Details;
            Panel(status, "panel.inset");
            if (view.IsError) Overlay(UiSkinState.Error, status);
            var lines = Wrap(view.Status, MathF.Max(1, (status.Width - 40 * scale) / scale), 22);
            for (int i = 0; i < Math.Min(4, lines.Length); i++)
                Text(lines[i], status with { X = status.X + 20 * scale, Y = status.Y + (20 + i * 28) * scale, Width = status.Width - 40 * scale },
                    view.IsError ? ForgeLineUiPalette.Warning : ForgeLineUiPalette.TextPrimary);
            if (view.HasProgress)
            {
                var track = new FrontendRegion(status.X, layout.PrimaryAction.Y, status.Width, 14 * scale);
                Panel(track, "progress.progress_track");
                skin.Draw("ui.skin.directorate.progress.progress_fill", Rect(track), ForgeLineUiPalette.Accent, fill: view.Progress);
                Text($"{Math.Clamp((int)(view.Progress * 100), 0, 100)}%", layout.Feedback, ForgeLineUiPalette.TextPrimary);
            }
        }
        else
        {
            if (view.SettingsTabs is { } tabs)
                for (int i = 0; i < Math.Min(4, tabs.Count); i++)
                {
                    var tab = tabs[i]; var region = layout.SettingsTab(i);
                    Control(region, "control.tab", tab.IsEnabled, tab.IsHovered, tab.IsPressed, false, tab.IsFocused);
                    Text(tab.Label, region with { X = region.X + 12 * scale, Y = region.Y + 10 * scale, Width = region.Width - 24 * scale },
                        tab.IsFocused ? ForgeLineUiPalette.TextPrimary : ForgeLineUiPalette.TextSecondary, .85f);
                }
            if (layout.Controls)
            {
                var header = layout.DetailRow(0) with { Y = layout.Details.Y - 34 * scale, Height = 28 * scale };
                Text("ACTION", header with { X = header.X + 20 * scale, Width = header.Width * .34f - 40 * scale }, ForgeLineUiPalette.TextMuted, .75f);
                Text("INPUT", header with { X = header.X + header.Width * .36f, Width = header.Width * .62f }, ForgeLineUiPalette.TextMuted, .75f);
            }
            int start = Math.Clamp(view.DetailScroll, 0, Math.Max(0, view.DetailLines.Count - layout.VisibleDetailRows));
            int count = Math.Min(layout.VisibleDetailRows, view.DetailLines.Count - start);
            skin.SetClip(Rect(layout.Details));
            for (int i = 0; i < count; i++)
            {
                var line = view.DetailLines[start + i]; var row = layout.DetailRow(i);
                Control(row, layout.Controls ? "control.row_compact" : "control.row", true, line.IsHovered, line.IsPressed, line.IsFocused);
                if (line.IsWarning || (view.IsError && line.IsFocused)) Overlay(UiSkinState.Error, row);
                Text(line.Label, layout.DetailLabel(i), ForgeLineUiPalette.TextSecondary);
                var value = layout.DetailValue(i, line.CanDecrease || line.CanIncrease || line.ValueKind != FrontendValueKind.Text);
                var valueText = value;
                if (line.ValueKind is FrontendValueKind.Choice or FrontendValueKind.Binding)
                {
                    Panel(value with { Y = value.Y - 5 * scale, Height = 36 * scale }, line.ValueKind == FrontendValueKind.Binding ? "control.input" : "control.dropdown");
                    valueText = value with { X = value.X + 8 * scale, Width = value.Width - 16 * scale };
                }
                else if (line.ValueKind == FrontendValueKind.Toggle)
                {
                    var toggle = new FrontendRegion(value.Right - 56 * scale, value.Y, 48 * scale, 24 * scale);
                    Panel(toggle, "control.toggle_track");
                    if (line.IsOn) Overlay(UiSkinState.Selected, toggle);
                    Panel(toggle with { X = toggle.X + (line.IsOn ? 26 : 2) * scale, Y = toggle.Y + 2 * scale, Width = 20 * scale, Height = 20 * scale }, "control.toggle_thumb");
                    valueText = value with { Width = MathF.Max(1, value.Width - 68 * scale) };
                }
                else if (line.ValueKind == FrontendValueKind.Slider && value.Width >= 140 * scale)
                {
                    var track = new FrontendRegion(value.X + 90 * scale, value.Y + 9 * scale, value.Width - 100 * scale, 8 * scale);
                    Panel(track, "control.slider_track");
                    skin.Draw("ui.skin.directorate.progress.progress_fill", Rect(track), ForgeLineUiPalette.Accent, fill: line.NormalizedValue);
                    Panel(new(track.X + Math.Clamp(line.NormalizedValue, 0, 1) * MathF.Max(0, track.Width - 12 * scale), track.Y - 5 * scale, 12 * scale, 18 * scale), "control.slider_thumb");
                    valueText = value with { Width = 80 * scale };
                }
                Text(line.Value, valueText, line.IsWarning ? ForgeLineUiPalette.Warning : line.IsFocused ? ForgeLineUiPalette.Accent : ForgeLineUiPalette.TextPrimary, layout.Controls ? .9f : 1);
                if (layout.Controls) Panel(row with { X = row.X + row.Width * .35f, Width = scale }, "control.separator");
                if (line.CanDecrease || line.CanIncrease || line.ValueKind != FrontendValueKind.Text)
                {
                    Step("-", layout.Decrease(i), line.CanDecrease, -1, line);
                    Step("+", layout.Increase(i), line.CanIncrease, 1, line);
                }
            }
            skin.SetClip(new(0, 0, width, height));
            if (view.DetailLines.Count > count)
            {
                var track = layout.Details with { X = layout.Details.Right + 4 * scale, Width = 8 * scale };
                Panel(track, "control.scrollbar_track");
                float thumb = MathF.Max(20 * scale, track.Height * count / view.DetailLines.Count);
                Panel(track with { Y = track.Y + (track.Height - thumb) * start / Math.Max(1, view.DetailLines.Count - count), Height = thumb }, "control.scrollbar_thumb");
                Text($"{start + 1}-{start + count} / {view.DetailLines.Count}  UP/DOWN / WHEEL", layout.Title with { X = layout.Title.X + layout.Title.Width * .42f, Width = layout.Title.Width * .58f }, ForgeLineUiPalette.TextMuted, .65f);
            }
            if (!string.IsNullOrEmpty(view.DisabledPrimaryAction))
            {
                Control(layout.PrimaryAction, "control.button", false, false, false, false);
                Text(view.DisabledPrimaryAction, layout.PrimaryAction with { X = layout.PrimaryAction.X + 24 * scale, Y = layout.PrimaryAction.Y + 18 * scale, Width = layout.PrimaryAction.Width - 48 * scale }, ForgeLineUiPalette.TextDisabled);
            }
            Action(view.PrimaryAction, layout.PrimaryAction, true, view.PrimaryHovered, view.PrimaryPressed);
            Action(view.SecondaryAction, layout.SecondaryAction, false, view.SecondaryHovered, view.SecondaryPressed);
        }
        var motion = view.Motion ?? UiMotionView.Settled;
        if (motion.Reveal < 1) Panel(layout.Title with { Y = layout.Title.Bottom - scale, Height = scale, Width = layout.Title.Width * Math.Clamp(motion.Reveal, 0, 1) }, "control.separator");
        var footer = Wrap(view.Footer, layout.Footer.Width / scale, 22 * .78f);
        for (int i = 0; i < Math.Min(2, footer.Length); i++) Text(footer[i], layout.Footer with { Y = layout.Footer.Y + i * 24 * scale }, ForgeLineUiPalette.TextMuted, .78f);
        if (!string.IsNullOrEmpty(view.Feedback))
        {
            Panel(layout.Feedback, "panel.banner"); Overlay(UiSkinState.Warning, layout.Feedback);
            Text(view.Feedback, layout.Feedback with { X = layout.Feedback.X + 8 * scale, Width = layout.Feedback.Width - 16 * scale }, ForgeLineUiPalette.Warning, .8f);
        }
        if (!string.IsNullOrEmpty(view.ProductVersion)) Text($"VERSION {view.ProductVersion}", layout.Footer with { Y = height - 40 * scale }, ForgeLineUiPalette.TextMuted, .65f);

        // Upload colored backing/branding once, before binding it; preserve the native write guard.
        IGraphicsBuffer? buffer = null;
        if (_vertexCount > 0) { buffer = GetFrameVertexBuffer(context.FrameIndex); buffer.SetData<OverlayVertex>(_vertices.AsSpan(0, _vertexCount)); }
        if (backingVertices > 0) Colored(0, backingVertices);
        skin.Flush(context);
        if (_vertexCount > backingVertices) Colored(backingVertices, _vertexCount - backingVertices);
        _text.Render(context);
        LastRenderedVertexCount = _vertexCount + skin.Diagnostics.Vertices + _text.VertexCount + (fullImage ? 6 : 0);

        void Colored(int start, int count) { context.SetPipeline(_pipeline); context.SetVertexBuffer(buffer!, VertexStride); context.Draw(count, start); }
        void Text(string text, FrontendRegion region, Vector4 color, float factor = 1) =>
            EmitText(text, region.X, region.Y, color, width, height, scale * factor, MathF.Max(1, region.Width / (scale * factor)));
        void Panel(FrontendRegion region, string role, float opacity = 1) => skin.Draw(SkinIds[role], Rect(region), new(1, 1, 1, opacity), true);
        void Overlay(UiSkinState state, FrontendRegion region) => skin.Overlay(state, Rect(region), Vector4.One);
        void Control(FrontendRegion region, string role, bool enabled, bool hover, bool pressed, bool focus, bool selected = false)
        {
            Panel(region, role); LastSkinnedControlCount++;
            if (!enabled) { Overlay(UiSkinState.Disabled, region); return; }
            if (selected) Overlay(UiSkinState.Selected, region);
            if (hover) Overlay(UiSkinState.Hover, region);
            if (pressed) Overlay(UiSkinState.Pressed, region);
            if (focus) Overlay(UiSkinState.Focus, region);
        }
        void Step(string label, FrontendRegion region, bool enabled, int direction, FrontendDetailLineView line)
        {
            Control(region, "control.button_compact", enabled, line.AdjustHover == direction, line.AdjustPress == direction, false);
            Text(label, region with { X = region.X + 27 * scale, Y = region.Y + 10 * scale, Width = region.Width - 30 * scale }, enabled ? ForgeLineUiPalette.TextPrimary : ForgeLineUiPalette.TextDisabled);
        }
        void Action(string label, FrontendRegion region, bool selected, bool hover, bool pressed)
        {
            if (string.IsNullOrEmpty(label)) return;
            Control(region, "control.button", true, hover, pressed, selected);
            Text(label, region with { X = region.X + 24 * scale, Y = region.Y + 18 * scale, Width = region.Width - 48 * scale }, ForgeLineUiPalette.TextPrimary);
        }
    }
    private static UiSkinRect Rect(FrontendRegion region) => new(region.X, region.Y, region.Width, region.Height);
}
