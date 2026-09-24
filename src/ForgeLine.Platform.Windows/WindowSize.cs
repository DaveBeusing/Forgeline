namespace ForgeLine.Platform;

public readonly record struct WindowSize(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
