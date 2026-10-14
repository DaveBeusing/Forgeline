namespace ForgeLine.Presentation;

/// <summary>Physical client-pixel geometry. Regions never include letterbox space.</summary>
public readonly record struct FrontendRegion(float X, float Y, float Width, float Height, bool Interactive = false)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public bool Contains(float x, float y) => Interactive && Width > 0 && Height > 0 &&
        x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>Passive layout contract shared by frontend composition and input; HUD layout is independent.</summary>
public readonly record struct FrontendGeometry
{
    public float Width { get; init; }
    public float Height { get; init; }
    public float Scale { get; init; }
    public float Dpi { get; init; }
    public float UserScale { get; init; }
    public FrontendRegion SafeArea { get; init; }
    public FrontendRegion Header { get; init; }
    public FrontendRegion Title { get; init; }
    public FrontendRegion Navigation { get; init; }
    public FrontendRegion Details { get; init; }
    public FrontendRegion PrimaryAction { get; init; }
    public FrontendRegion SecondaryAction { get; init; }
    public FrontendRegion Footer { get; init; }
    public FrontendRegion Feedback { get; init; }
    public FrontendRegion Decorative { get; init; }
    public FrontendRegion MenuArtwork { get; init; }
    public float MenuStride { get; init; }
    public float DetailStride { get; init; }
    public bool Controls { get; init; }
    public int VisibleDetailRows { get; init; }
    public bool IsEmpty => Scale <= 0;
    public FrontendRegion PauseBackdrop
    {
        get
        {
            float top = MathF.Min(Header.Y, Title.Y);
            return new(Navigation.X, top, Navigation.Width, MathF.Max(0, Footer.Bottom - top));
        }
    }

    private static FrontendRegion Rect(float x, float y, float width, float height, bool interactive = false) =>
        new(MathF.Round(x), MathF.Round(y), MathF.Max(0, MathF.Round(width)), MathF.Max(0, MathF.Round(height)), interactive);

    public FrontendRegion MenuRow(int index) => index is >= 0 and < 7 && !IsEmpty
        ? Rect(Navigation.X, Navigation.Y + index * MenuStride * Scale, Navigation.Width, 52 * Scale, true) : default;
    public FrontendRegion DetailRow(int index) => index >= 0 && index < VisibleDetailRows && !IsEmpty
        ? Rect(Details.X, Details.Y + index * DetailStride * Scale, Details.Width, (DetailStride - 10) * Scale, true) : default;
    public FrontendRegion DetailLabel(int index) { var r = DetailRow(index); return Rect(r.X + 20 * Scale, r.Y + 13 * Scale, (Controls ? r.Width : r.Width * .34f) - 40 * Scale, 28 * Scale); }
    public FrontendRegion DetailValue(int index, bool adjustable)
    {
        var r = DetailRow(index); float left = Controls ? r.X + 20 * Scale : r.X + r.Width * .36f;
        return Rect(left, r.Y + (Controls ? 43 : 13) * Scale, (adjustable ? Decrease(index).X : r.Right - 20 * Scale) - left - 12 * Scale, 28 * Scale);
    }
    public FrontendRegion Decrease(int index) { var r = DetailRow(index); return r.Width > 0 ? Rect(r.Right - 242 * Scale, r.Y, 72 * Scale, 42 * Scale, true) : default; }
    public FrontendRegion Increase(int index) { var r = DetailRow(index); return r.Width > 0 ? Rect(r.Right - 156 * Scale, r.Y, 72 * Scale, 42 * Scale, true) : default; }

    public static FrontendGeometry Resolve(float width, float height, float userScale = 1, float dpi = 96, bool controls = false)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return default;
        if (!float.IsFinite(userScale) || userScale <= 0) throw new ArgumentOutOfRangeException(nameof(userScale));
        if (!float.IsFinite(dpi) || dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        float viewport = MathF.Min(width / 1920, height / 1080);
        // Retain a readable minimum at supported sizes; compact header/art before shrinking commands.
        float scale = MathF.Min(MathF.Max(viewport * userScale * dpi / 96, MathF.Min(.75f, viewport)),
            MathF.Min(3, MathF.Min(height / 850, width / 1100)));
        float canvas = MathF.Min(width, 1920 * viewport);
        float left = (width - canvas) / 2 + 82 * viewport;
        float right = (width + canvas) / 2 - 82 * viewport;
        float menuStride = height / scale >= 1000 ? 72 : 64;
        float menuHeight = (6 * menuStride + 52) * scale;
        float menuY = MathF.Min(338 * scale, height - 190 * scale - menuHeight);
        float detailY = MathF.Min(377 * scale, height * .35f);
        float actionY = height - 242 * scale;
        float detailWidth = controls ? right - left - 10 * scale : MathF.Min(right - left - 10 * scale, MathF.Max(980 * viewport, 950 * scale));
        float stride = controls ? 150 : 58;
        int rows = Math.Clamp((int)((actionY - detailY - 6 * scale) / (stride * scale)), 1, 7);
        var result = new FrontendGeometry { Width = width, Height = height, Scale = scale, Dpi = dpi, UserScale = userScale };
        return result with
        {
            SafeArea = Rect(left, 24 * scale, right - left, height - 48 * scale),
            Header = Rect(left + 12 * scale, MathF.Min(80 * scale, menuY - 170 * scale), right - left, 90 * scale),
            Title = Rect(left + 12 * scale, MathF.Min(menuY - 52 * scale, detailY - 91 * scale), detailWidth - 24 * scale, 35 * scale),
            Navigation = Rect(left, menuY, MathF.Min(560 * scale, right - left), menuHeight),
            Details = Rect(left + 10 * scale, detailY, detailWidth, rows * stride * scale - 10 * scale),
            PrimaryAction = Rect(left + 10 * scale + detailWidth - 290 * scale, actionY, 290 * scale, 58 * scale, true),
            SecondaryAction = Rect(left + 10 * scale, actionY, 220 * scale, 58 * scale, true),
            Footer = Rect(left + 12 * scale, height - 150 * scale, right - left - 24 * scale, 70 * scale, true),
            Feedback = Rect(left + 12 * scale, height - 175 * scale, right - left - 24 * scale, 28 * scale),
            Decorative = Rect(left + detailWidth + 50 * scale, detailY, MathF.Max(0, right - left - detailWidth - 50 * scale), actionY - detailY),
            MenuArtwork = Rect(left + MathF.Min(560 * scale, right - left) + 40 * scale, MathF.Max(190 * scale, menuY - 90 * scale),
                MathF.Max(0, right - left - MathF.Min(560 * scale, right - left) - 40 * scale), height - 210 * scale - MathF.Max(190 * scale, menuY - 90 * scale)),
            MenuStride = menuStride, DetailStride = stride, Controls = controls, VisibleDetailRows = rows
        };
    }
}
