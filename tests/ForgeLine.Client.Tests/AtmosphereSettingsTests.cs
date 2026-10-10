using ForgeLine.Graphics;
using ForgeLine.Presentation;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class AtmosphereSettingsTests
{
    [Fact]
    public void AtmosphericValuesRoundTripAndOlderFilesKeepDisabledDefaults()
    {
        string root=Path.Combine(Path.GetTempPath(),"forgeline-atmosphere-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new ClientSettingsStore(root);
            var atmosphere=SceneAtmosphereSettings.Default with { Quality=AtmosphereQuality.Height,
                ColorRed=0.2f,ColorGreen=0.3f,ColorBlue=0.4f,Density=0.002f,BaseHeight=42,
                HeightFalloff=0.04f,StartDistance=80,MaximumOpacity=0.4f,ZoomCompensation=0.25f };
            store.Save(new() { SceneAtmosphere=atmosphere });
            var loaded=store.Load();
            Assert.False(loaded.RecoveredInvalidSettings);
            Assert.Equal(atmosphere,loaded.Settings.SceneAtmosphere);
            File.WriteAllText(store.SettingsPath,"{\"schemaVersion\":2}");
            var older=store.Load();
            Assert.False(older.RecoveredInvalidSettings);
            Assert.Equal(SceneAtmosphereSettings.Default,older.Settings.SceneAtmosphere);
            Assert.Equal(AtmosphereQuality.Disabled,older.Settings.SceneAtmosphere.Quality);
        }
        finally { Directory.Delete(root,true); }
    }

    [Fact]
    public void OutOfRangeAtmosphereCannotBeSaved()
    {
        var settings=new ClientUserSettings { SceneAtmosphere=SceneAtmosphereSettings.Default with { MaximumOpacity=1 } };
        Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
    }
}
