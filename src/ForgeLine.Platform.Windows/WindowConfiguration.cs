namespace ForgeLine.Platform;

public sealed record WindowConfiguration
{
    public WindowConfiguration(
        string title,
        int width,
        int height,
        bool resizable = true,
        WindowMode mode = WindowMode.Windowed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Title = title;
        Width = width;
        Height = height;
        Resizable = resizable;
        Mode = mode;
    }

    public string Title { get; }

    public int Width { get; }

    public int Height { get; }

    public bool Resizable { get; }

    public WindowMode Mode { get; }
}
