namespace ForgeLine.Graphics;

public readonly record struct GraphicsSceneOutputSettings(bool Enabled, float Exposure, bool AcesFitted, float ResolutionScale = 1)
{
    public void Validate()
    {
        if (!float.IsFinite(Exposure) || Exposure <= 0 || Exposure > 16)
            throw new ArgumentOutOfRangeException(nameof(Exposure));
        if (!float.IsFinite(ResolutionScale) || ResolutionScale is < .5f or > 1 || (!Enabled && ResolutionScale != 1))
            throw new ArgumentOutOfRangeException(nameof(ResolutionScale));
    }
}
