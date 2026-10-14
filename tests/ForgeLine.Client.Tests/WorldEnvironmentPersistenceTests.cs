using System.Numerics;
using ForgeLine.Graphics;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class WorldEnvironmentPersistenceTests
{
    [Fact]
    public void AppearanceResidencyAndLocalHazeRoundTripAndSurvivePresetSelection()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-world-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var quality = new RenderQualitySettings
            {
                Environment = new() { Daylight = .4f, CloudCover = .6f, Wetness = .7f },
                Vegetation = new() { Density = .5f, MaximumDistanceMeters = 1200 }, TerrainResidency = new(true, 64)
            };
            var haze = new GraphicsLocalHazeSettings(new(400, 15, 500), 350, .001f);
            var store = new ClientSettingsStore(root);
            store.Save(new() { RenderQuality = quality, SceneAtmosphere = new() { LocalHaze = haze } });
            var adapter = new ClientSettingsFrontendAdapter(new ClientSettingsStore(root));
            var model = adapter.Load();
            Assert.Equal(quality, model.GraphicsSettings);
            model.SetRenderQuality(FrontendRenderQuality.Ultra);
            var saved = adapter.Apply(model);
            Assert.Equal(quality.Environment, saved.RenderQuality.Environment);
            Assert.Equal(quality.Vegetation, saved.RenderQuality.Vegetation);
            Assert.Equal(quality.TerrainResidency, saved.RenderQuality.TerrainResidency);
            Assert.Equal(haze, new ClientSettingsStore(root).Load().Settings.SceneAtmosphere.LocalHaze);
            var options = new Dictionary<string, string> { ["FORGELINE_WORLD_WETNESS"] = "0.25", ["FORGELINE_VEGETATION_DENSITY"] = "0.4" };
            var requested = ClientGraphicsOverrides.Read(quality, key => options.GetValueOrDefault(key));
            Assert.Equal(.25f, requested.Environment.Wetness);
            Assert.Equal(.4f, requested.Vegetation.Density);
            Assert.Equal(.7f, quality.Environment.Wetness);
            options["FORGELINE_WORLD_WETNESS"] = "NaN";
            Assert.Throws<ArgumentOutOfRangeException>(() => ClientGraphicsOverrides.Read(quality, key => options.GetValueOrDefault(key)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
