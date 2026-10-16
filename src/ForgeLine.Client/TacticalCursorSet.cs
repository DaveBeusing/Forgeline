using ForgeLine.Assets;
using ForgeLine.Platform;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

/// <summary>Prepared runtime atlas pixels; no authoring catalog or frame-time asset reads.</summary>
internal sealed class TacticalCursorSet : IDisposable
{
    private static readonly string[] Roles = ["default", "select", "move", "attack", "attack_move", "build", "repair", "supply", "invalid", "pan", "drag_select"];
    private static readonly int[] Sizes = [24, 32, 48, 64];
    private readonly WindowCursorImage[,] _images = new WindowCursorImage[11, 4];
    private readonly IWindow _window;
    internal TacticalCursorSet(IWindow window, RuntimeAssetCatalog catalog)
    {
        _window = window;
        var data = RuntimeUiSkinData.FromPayload(catalog.Read(AssetId.Parse(UiSkinResources.AssetName)).Payload);
        var pages = new Dictionary<int, RuntimeTextureData>();
        for (int role = 0; role < Roles.Length; role++) for (int size = 0; size < Sizes.Length; size++)
        {
            string id = $"ui.artwork.directorate.cursor.{Roles[role]}.p{Sizes[size]}";
            var sprite = data.Sprites.Single(s => s.Id == id);
            if (sprite.Width != Sizes[size] || sprite.Height != Sizes[size] || sprite.HotspotX < 0 || sprite.HotspotY < 0)
                throw new InvalidDataException($"Required native cursor '{id}' has invalid dimensions or hotspot.");
            if (!pages.TryGetValue(sprite.Page, out var texture))
            {
                texture = RuntimeTextureData.FromPayload(catalog.Read(AssetId.Parse(data.Pages[sprite.Page].Id)).Payload);
                RuntimeUiSkinData.ValidatePage(data.Pages[sprite.Page], texture);
                pages.Add(sprite.Page, texture);
            }
            byte[] pixels = new byte[sprite.Width * sprite.Height * 4];
            var mip = texture.Mips[0];
            for (int row = 0; row < sprite.Height; row++)
                mip.Pixels.AsSpan((sprite.Y + row) * mip.RowPitch + sprite.X * 4, sprite.Width * 4).CopyTo(pixels.AsSpan(row * sprite.Width * 4));
            _images[role, size] = new(sprite.Width, sprite.HotspotX, sprite.HotspotY, pixels);
        }
    }
    internal static int SizeIndex(uint dpi, float scale) => Math.Clamp(dpi * (float.IsFinite(scale) ? scale : 1), 72, 192) switch
    { <= 72 => 0, <= 96 => 1, <= 144 => 2, _ => 3 };
    internal void Update(RtsCursorKind kind, uint dpi, float scale, bool active) =>
        _window.SetCursorImage(active ? _images[(int)kind, SizeIndex(dpi, scale)] : null);
    public void Dispose() => _window.ReleaseCursorImages();
}
