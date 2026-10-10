namespace ForgeLine.Graphics;

public readonly record struct GraphicsSceneOutputSettings(bool Enabled, float Exposure, bool AcesFitted)
{
    public void Validate()
    {
        if (!float.IsFinite(Exposure) || Exposure <= 0 || Exposure > 16)
            throw new ArgumentOutOfRangeException(nameof(Exposure));
    }
}
