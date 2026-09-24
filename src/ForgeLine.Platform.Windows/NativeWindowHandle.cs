namespace ForgeLine.Platform;

public readonly record struct NativeWindowHandle(nint Value)
{
    public bool IsValid => Value != 0;
}
