namespace ForgeLine.Platform.Windows;

internal readonly record struct WindowBounds(
    int Left,
    int Top,
    int Width,
    int Height)
{
    internal int Right => Left + Width;

    internal int Bottom => Top + Height;

    internal bool HasPositiveArea =>
        Width > 0 &&
        Height > 0;
}

internal static class WindowPlacement
{
    internal static WindowBounds ConstrainToWorkArea(
        in WindowBounds requested,
        in WindowBounds workArea,
        int fallbackWidth,
        int fallbackHeight)
    {
        if (!workArea.HasPositiveArea)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workArea),
                workArea,
                "Monitor work area must have positive dimensions.");
        }

        int width =
            requested.Width > 0
                ? Math.Min(
                    requested.Width,
                    workArea.Width)
                : Math.Min(
                    Math.Max(1, fallbackWidth),
                    workArea.Width);
        int height =
            requested.Height > 0
                ? Math.Min(
                    requested.Height,
                    workArea.Height)
                : Math.Min(
                    Math.Max(1, fallbackHeight),
                    workArea.Height);

        bool intersectsWorkArea =
            requested.HasPositiveArea &&
            requested.Right > workArea.Left &&
            requested.Left < workArea.Right &&
            requested.Bottom > workArea.Top &&
            requested.Top < workArea.Bottom;

        if (!intersectsWorkArea)
        {
            return new WindowBounds(
                workArea.Left +
                (workArea.Width - width) / 2,
                workArea.Top +
                (workArea.Height - height) / 2,
                width,
                height);
        }

        int left =
            Math.Clamp(
                requested.Left,
                workArea.Left,
                workArea.Right - width);
        int top =
            Math.Clamp(
                requested.Top,
                workArea.Top,
                workArea.Bottom - height);

        return new WindowBounds(
            left,
            top,
            width,
            height);
    }
}
