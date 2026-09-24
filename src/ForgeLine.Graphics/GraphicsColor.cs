namespace ForgeLine.Graphics;

public readonly record struct GraphicsColor(float Red, float Green, float Blue, float Alpha)
{
    public static GraphicsColor ForgeLineClear => new(0.015f, 0.025f, 0.055f, 1.0f);
}
