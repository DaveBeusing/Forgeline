using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

/// <summary>Shared atlas residency (deduplicated by group) and bounded per-owner recording costs.</summary>
public readonly record struct GameplayHudSkinDiagnostics(long CoreBytes, long IconBytes, long PortraitBytes,
    int Batches, int Buffers, int Draws, int Vertices, long VertexUploadBytes, int MissingAssets, int DroppedQuads)
{
    public long ResidentPageBytes => CoreBytes + IconBytes + PortraitBytes;
    public bool Prepared => CoreBytes > 0;
    internal GameplayHudSkinDiagnostics Combine(GameplayHudSkinDiagnostics other) => new(
        Math.Max(CoreBytes, other.CoreBytes), Math.Max(IconBytes, other.IconBytes), Math.Max(PortraitBytes, other.PortraitBytes),
        Batches + other.Batches, Buffers + other.Buffers, Draws + other.Draws, Vertices + other.Vertices,
        VertexUploadBytes + other.VertexUploadBytes, MissingAssets + other.MissingAssets, DroppedQuads + other.DroppedQuads);
}

/// <summary>Prepared render-thread resources. Skin precedes the owner's dynamic geometry and live text.
/// Bootstrap/test devices remain independent of assets; a required production asset failure is explicit.</summary>
internal sealed class GameplayHudSkin : IDisposable
{
    private static readonly Dictionary<string, string> PanelIds = new[] { "panel.primary", "panel.resource", "panel.raised", "panel.compact", "panel.inset", "panel.portrait", "panel.tooltip", "control.button_compact", "control.tab" }
        .ToDictionary(static role => role, static role => "ui.skin.directorate." + role, StringComparer.Ordinal);
    private readonly UiSkinResources? _core, _icons, _portraits;
    private readonly UiSkinBatch? _panels, _symbols, _images;
    internal GameplayHudSkinDiagnostics Diagnostics
    {
        get
        {
            var core = _panels?.Diagnostics ?? default; var icons = _symbols?.Diagnostics ?? default; var portraits = _images?.Diagnostics ?? default;
            return new(core.PageBytes, icons.PageBytes, portraits.PageBytes, (Ready ? 1 : 0) + (_symbols is null ? 0 : 1) + (_images is null ? 0 : 1),
                core.BufferCount + icons.BufferCount + portraits.BufferCount, core.Draws + icons.Draws + portraits.Draws,
                core.Vertices + icons.Vertices + portraits.Vertices, core.VertexUploadBytes + icons.VertexUploadBytes + portraits.VertexUploadBytes,
                core.MissingAssets + icons.MissingAssets + portraits.MissingAssets, core.DroppedQuads + icons.DroppedQuads + portraits.DroppedQuads);
        }
    }
    internal bool Ready => _panels is not null;
    internal int Vertices => (_panels?.Diagnostics.Vertices ?? 0) + (_symbols?.Diagnostics.Vertices ?? 0) + (_images?.Diagnostics.Vertices ?? 0);
    internal GameplayHudSkin(IGraphicsDevice graphics, RuntimeAssetCatalog? catalog, bool icons = false, bool portraits = false)
    {
        if (catalog is null || !graphics.SupportsUiText) return;
        try
        {
            _core = new(graphics, catalog);
            foreach (string role in new[] { "panel.primary", "panel.resource", "panel.raised", "panel.compact", "panel.inset", "panel.portrait", "panel.tooltip",
                "control.button_compact", "control.tab", "progress.progress_track", "progress.progress_fill",
                "overlay.hover", "overlay.pressed", "overlay.focus", "overlay.selected", "overlay.disabled", "overlay.warning", "overlay.error", "overlay.pending", "overlay.accepted" })
                _ = _core.Sprite("ui.skin.directorate." + role);
            _panels = new(graphics, _core, maximumQuads: 1024);
            if (icons)
            {
                _icons = new(graphics, catalog, UiSkinGroup.Icons);
                foreach (string id in GameplayHudArtwork.RequiredIcons) _ = _icons.Sprite(id);
                _symbols = new(graphics, _icons, maximumQuads: 256);
            }
            if (portraits)
            {
                _portraits = new(graphics, catalog, UiSkinGroup.Portraits);
                foreach (string id in GameplayHudArtwork.RequiredPortraits) _ = _portraits.Sprite(id);
                _images = new(graphics, _portraits, maximumQuads: 64);
            }
        }
        catch
        {
            Dispose();
            throw; // Never advertise a partially prepared production HUD as ready.
        }
    }
    internal void Begin(IGraphicsCommandContext context)
    {
        _panels?.Begin(context.Width, context.Height, context.FrameIndex);
        _symbols?.Begin(context.Width, context.Height, context.FrameIndex);
        _images?.Begin(context.Width, context.Height, context.FrameIndex);
    }
    internal bool Panel(HudRect r, string role = "panel.primary")
    {
        if (_panels is null) return false;
        _panels.Draw(PanelIds[role], Rect(r), Vector4.One, true);
        return true;
    }
    internal bool Control(HudRect r, bool enabled = true, bool selected = false, bool hover = false, bool pressed = false, string role = "control.button_compact")
    {
        if (!Panel(r, role)) return false;
        if (!enabled) State(r, UiSkinState.Disabled);
        else
        {
            if (selected) { State(r, UiSkinState.Selected); State(r, UiSkinState.Focus); }
            if (hover) State(r, UiSkinState.Hover);
            if (pressed) State(r, UiSkinState.Pressed);
        }
        return true;
    }
    internal void State(HudRect r, UiSkinState state) => _panels?.Overlay(state, Rect(r), Vector4.One);
    internal bool Icon(RtsUiIcon icon, float x, float y, float size)
    {
        if (_symbols is null) return false;
        string? id = GameplayHudArtwork.Icon(icon, size);
        if (id is null) return false;
        _symbols.Draw(id, new(x,y,size,size), Vector4.One);
        return true;
    }
    internal bool Portrait(in PlayerSelectionSummary selection, HudRect r)
    {
        if (_images is null) return false;
        string? id = GameplayHudArtwork.Portrait(selection, r.Width);
        if (id is null) return false;
        Panel(r, "panel.portrait");
        _images.Draw(id, Rect(r with { X = r.X + 2, Y = r.Y + 2, Width = r.Width - 4, Height = r.Height - 4 }), Vector4.One);
        return true;
    }
    internal bool Portrait(PlayerSelectionKind kind, uint identity, HudRect r)
    {
        if (_images is null) return false;
        string? id = GameplayHudArtwork.Portrait(kind, identity, r.Width);
        if (id is null) return false;
        Panel(r, "panel.portrait");
        _images.Draw(id, Rect(r with { X = r.X + 1, Y = r.Y + 1, Width = r.Width - 2, Height = r.Height - 2 }), Vector4.One);
        return true;
    }
    internal bool Bar(HudRect r, double fraction, Vector4 tint)
    {
        if (_panels is null) return false;
        // Resample the complete sprite below its authored height: compressed borders would remove the centre.
        bool sliced = r.Height >= 12;
        _panels.Draw("ui.skin.directorate.progress.progress_track", Rect(r), Vector4.One, sliced);
        if (double.IsFinite(fraction))
            _panels.Draw("ui.skin.directorate.progress.progress_fill", Rect(r), tint, sliced, (float)Math.Clamp(fraction,0,1));
        return true;
    }
    internal void Flush(IGraphicsCommandContext context) { _panels?.Flush(context); _symbols?.Flush(context); _images?.Flush(context); }
    private static UiSkinRect Rect(HudRect r) => new(r.X,r.Y,r.Width,r.Height);
    public void Dispose()
    {
        _images?.Dispose(); _symbols?.Dispose(); _panels?.Dispose();
        _portraits?.Dispose(); _icons?.Dispose(); _core?.Dispose();
    }
}
