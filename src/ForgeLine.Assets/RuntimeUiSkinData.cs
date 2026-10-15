using System.Text;

namespace ForgeLine.Assets;

public enum UiSkinGroup { Core, Icons, Portraits }
public enum UiSkinState { Normal, Hover, Pressed, Focus, Selected, Disabled, Warning, Error, Pending, Accepted }
public readonly record struct UiSkinInsets(int Left, int Top, int Right, int Bottom);
public sealed record UiSkinPage(string Id, int Width, int Height, UiSkinGroup Group);
public sealed record UiSkinSprite(string Id, int Page, int X, int Y, int Width, int Height,
    float LogicalWidth, float LogicalHeight, int Padding, UiSkinInsets Insets,
    UiSkinInsets ContentPadding, bool NineSlice, int HotspotX = -1, int HotspotY = -1);

/// <summary>Display-referred, straight-alpha RGBA8, linear sampling, no mipmaps.
/// Page color bytes are already display encoded and bypass world tone mapping.</summary>
public sealed class RuntimeUiSkinData
{
    public const int PayloadVersion = 1;
    public const int MaximumPages = 16;
    public const int MaximumSprites = 2048;
    public const long MaximumPageBytes = 32 * 1024 * 1024;
    public const int MaximumPayloadBytes = 1024 * 1024;
    private readonly UiSkinPage[] _pages;
    private readonly UiSkinSprite[] _sprites;
    public IReadOnlyList<UiSkinPage> Pages { get; }
    public IReadOnlyList<UiSkinSprite> Sprites { get; }
    public long PageBytes { get; }

    public RuntimeUiSkinData(IEnumerable<UiSkinPage> pages, IEnumerable<UiSkinSprite> sprites)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(sprites);
        _pages = pages.Take(MaximumPages + 1).ToArray();
        _sprites = sprites.Take(MaximumSprites + 1).ToArray();
        Validate();
        Pages = Array.AsReadOnly(_pages);
        Sprites = Array.AsReadOnly(_sprites);
        PageBytes = _pages.Sum(p => (long)p.Width * p.Height * 4);
    }

    private void Validate()
    {
        if (_pages.Length is < 1 or > MaximumPages || _sprites.Length is < 1 or > MaximumSprites)
            throw new InvalidDataException("UI skin page/sprite count exceeds its bounded contract.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var p in _pages)
        {
            if (p is null || string.IsNullOrEmpty(p.Id) || p.Id.Length > 256 || !AssetId.TryParse(p.Id, out _) || !ids.Add(p.Id) ||
                p.Width is < 1 or > 2048 || p.Height is < 1 or > 2048 || !Enum.IsDefined(p.Group))
                throw new InvalidDataException("Invalid or duplicate UI page identity/dimensions/group.");
            bytes += (long)p.Width * p.Height * 4;
        }
        if (bytes > MaximumPageBytes) throw new InvalidDataException("UI page residency exceeds 32 MiB.");
        ids.Clear();
        foreach (var s in _sprites)
        {
            if (s is null || string.IsNullOrEmpty(s.Id) || s.Id.Length > 256 || !AssetId.TryParse(s.Id, out _) || !ids.Add(s.Id) || s.Page < 0 || s.Page >= _pages.Length ||
                s.Width <= 0 || s.Height <= 0 || s.Padding is < 1 or > 8 || s.X < s.Padding || s.Y < s.Padding ||
                (long)s.X + s.Width + s.Padding > _pages[s.Page].Width || (long)s.Y + s.Height + s.Padding > _pages[s.Page].Height ||
                !float.IsFinite(s.LogicalWidth) || !float.IsFinite(s.LogicalHeight) || s.LogicalWidth <= 0 || s.LogicalHeight <= 0 ||
                s.LogicalWidth > 4096 || s.LogicalHeight > 4096 || !Valid(s.Insets, s.Width, s.Height) ||
                !Valid(s.ContentPadding, s.Width, s.Height) ||
                !((s.HotspotX == -1 && s.HotspotY == -1) || (s.HotspotX >= 0 && s.HotspotX < s.Width && s.HotspotY >= 0 && s.HotspotY < s.Height)))
                throw new InvalidDataException("Invalid UI sprite identity, region, logical size, insets or cursor hotspot.");
        }
        for (int i = 0; i < _sprites.Length; i++)
            for (int j = i + 1; j < _sprites.Length; j++)
            {
                var a = _sprites[i]; var b = _sprites[j];
                if (a.Page == b.Page && a.X - a.Padding < b.X + b.Width + b.Padding &&
                    b.X - b.Padding < a.X + a.Width + a.Padding && a.Y - a.Padding < b.Y + b.Height + b.Padding &&
                    b.Y - b.Padding < a.Y + a.Height + a.Padding)
                    throw new InvalidDataException("UI sprite padded regions overlap.");
            }
    }
    private static bool Valid(UiSkinInsets i, int w, int h) => i.Left >= 0 && i.Top >= 0 && i.Right >= 0 && i.Bottom >= 0 &&
        (long)i.Left + i.Right <= w && (long)i.Top + i.Bottom <= h;

    public static void ValidatePage(UiSkinPage page, RuntimeTextureData texture)
    {
        if (texture.Width != page.Width || texture.Height != page.Height || texture.Mips.Count != 1 ||
            texture.Format != RuntimeTextureFormat.Rgba8Unorm || texture.ColorSpace != RuntimeTextureColorSpace.Linear || texture.Usage != RuntimeTextureUsage.DisplayImage)
            throw new InvalidDataException($"UI page '{page.Id}' requires exact dimensions, display RGBA8/linear, one mip and straight alpha.");
    }

    public byte[] ToPayload()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8, true);
        w.Write(PayloadVersion); w.Write(_pages.Length); w.Write(_sprites.Length);
        foreach (var p in _pages) { w.Write(p.Id); w.Write(p.Width); w.Write(p.Height); w.Write((int)p.Group); }
        foreach (var s in _sprites)
        {
            w.Write(s.Id); w.Write(s.Page); w.Write(s.X); w.Write(s.Y); w.Write(s.Width); w.Write(s.Height);
            w.Write(s.LogicalWidth); w.Write(s.LogicalHeight); w.Write(s.Padding);
            Write(w, s.Insets); Write(w, s.ContentPadding); w.Write(s.NineSlice); w.Write(s.HotspotX); w.Write(s.HotspotY);
        }
        return stream.ToArray();
    }
    private static void Write(BinaryWriter w, UiSkinInsets i) { w.Write(i.Left); w.Write(i.Top); w.Write(i.Right); w.Write(i.Bottom); }
    private static UiSkinInsets ReadInsets(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
    public static RuntimeUiSkinData FromPayload(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length > MaximumPayloadBytes) throw new InvalidDataException("UI skin payload too large.");
        try
        {
            using var stream = new MemoryStream(payload, false);
            using var r = new BinaryReader(stream, Encoding.UTF8);
            if (r.ReadInt32() != PayloadVersion) throw new InvalidDataException("Unsupported UI skin version.");
            int pc = r.ReadInt32(), sc = r.ReadInt32();
            if (pc is < 1 or > MaximumPages || sc is < 1 or > MaximumSprites) throw new InvalidDataException("Invalid UI skin counts.");
            var pages = new UiSkinPage[pc]; var sprites = new UiSkinSprite[sc];
            for (int i = 0; i < pc; i++) pages[i] = new(r.ReadString(), r.ReadInt32(), r.ReadInt32(), (UiSkinGroup)r.ReadInt32());
            for (int i = 0; i < sc; i++) sprites[i] = new(r.ReadString(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadSingle(), r.ReadSingle(), r.ReadInt32(), ReadInsets(r), ReadInsets(r), r.ReadBoolean(), r.ReadInt32(), r.ReadInt32());
            if (stream.Position != stream.Length) throw new InvalidDataException("Trailing UI skin payload.");
            return new(pages, sprites);
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("Truncated UI skin payload.", e); }
    }
}
