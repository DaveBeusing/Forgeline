using System.Numerics;
using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

public readonly record struct SceneAtmosphereSettings(GraphicsAtmosphereSettings Graphics)
{
    public static SceneAtmosphereSettings Default => new(GraphicsAtmosphereSettings.Default);
    public void Validate() => Graphics.Validate();

    public static GraphicsAtmosphereView CreateView(RtsCamera camera, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (!Matrix4x4.Invert(camera.GetMatrices(width, height).ViewProjection, out Matrix4x4 inverse))
            throw new ArgumentException("The atmospheric view requires an invertible camera projection.", nameof(camera));
        return new(inverse, camera.Position, camera.Distance);
    }
}
