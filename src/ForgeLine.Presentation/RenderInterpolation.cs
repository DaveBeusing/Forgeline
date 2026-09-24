namespace ForgeLine.Presentation;

public static class RenderInterpolation
{
    public static float CalculateAlpha(
        TimeSpan accumulatedRenderTime,
        TimeSpan simulationTickDuration)
    {
        if (simulationTickDuration <= TimeSpan.Zero)
        {
            return 1.0f;
        }

        double ratio =
            accumulatedRenderTime.TotalSeconds /
            simulationTickDuration.TotalSeconds;

        return (float)Math.Clamp(ratio, 0.0, 1.0);
    }
}
