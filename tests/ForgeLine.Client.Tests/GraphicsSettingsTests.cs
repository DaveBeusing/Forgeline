using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class GraphicsSettingsTests
{
    [Theory]
    [InlineData(RenderQuality.Low)]
    [InlineData(RenderQuality.Medium)]
    [InlineData(RenderQuality.High)]
    [InlineData(RenderQuality.Ultra)]
    public void EveryPresetAndFeatureChoiceRoundTrips(RenderQuality quality)
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            var graphics = new RenderQualitySettings { Quality = quality, ResolutionScale = .75f,
                Antialiasing = SpatialAntialiasing.Disabled, Shadows = DirectionalShadowQuality.Low,
                Textures = TextureDetailQuality.Half, SurfaceDetail = false, VegetationMotion = false,
                Atmosphere = AtmosphereQuality.Distance, Particles = ParticleQuality.Low, ObjectDetail = RenderQuality.High };
            store.Save(new() { RenderQuality = graphics, SceneExposure = 1.3f });
            var adapter = new ClientSettingsFrontendAdapter(new ClientSettingsStore(root));
            var model = adapter.Load();
            Assert.Equal(graphics, model.GraphicsSettings);
            var editor = new SettingsInteractionModel();
            editor.Focus(FrontendSettingsField.ResolutionScale);
            Assert.Contains(FrontendPresentationAdapter.Settings(model, editor).DetailLines!, row => row.Label == "WORLD RESOLUTION" && row.Value == "75%");
            model.SetRenderQuality((FrontendRenderQuality)quality);
            var saved = adapter.Apply(model);
            Assert.Null(saved.RenderQuality.ResolutionScale);
            Assert.Null(saved.RenderQuality.Textures);
            Assert.Equal(quality, new ClientSettingsStore(root).Load().Settings.RenderQuality.Quality);
            Assert.Equal(1.3f, saved.SceneExposure);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void LegacyMigrationAndInvalidGraphicsPreserveDisplayAndInput()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            store.Save(new());
            File.WriteAllText(store.SettingsPath, """
                {"schemaVersion":2,"windowWidth":1920,"windowHeight":1080,"cameraPanSpeedMultiplier":1.7,
                 "particles":{"quality":"High"},"renderQuality":{"quality":"Low"}}
                """);
            var migrated = store.Load();
            Assert.False(migrated.RecoveredInvalidSettings);
            Assert.Equal(ClientUserSettings.CurrentSchemaVersion, migrated.Settings.SchemaVersion);
            Assert.Equal(NotificationPreferences.Default, migrated.Settings.Notifications);
            Assert.Equal(TextureDetailQuality.Full, migrated.Settings.RenderQuality.Textures);
            Assert.True(migrated.Settings.RenderQuality.VegetationMotion);
            Assert.Equal(ParticleQuality.High, migrated.Settings.RenderQuality.Particles);
            Assert.Equal(ParticleSettings.Default, migrated.Settings.Particles);
            File.WriteAllText(store.SettingsPath, """
                {"schemaVersion":3,"windowWidth":1920,"windowHeight":1080,"cameraPanSpeedMultiplier":1.7,
                 "renderQuality":{"resolutionScale":0.2}}
                """);
            var recovered = store.Load();
            Assert.True(recovered.RecoveredInvalidSettings);
            Assert.Equal(1920, recovered.Settings.WindowWidth);
            Assert.Equal(1.7f, recovered.Settings.CameraPanSpeedMultiplier);
            Assert.Equal(RenderQuality.Medium, recovered.Settings.RenderQuality.Quality);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(store.SettingsPath)!, "settings.json.invalid-*"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DisplayCandidateIsNotPersistedBeforeConfirmation()
    {
        string root = Path.Combine(Path.GetTempPath(), "forgeline-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ClientSettingsStore(root);
            store.Save(new());
            var adapter = new ClientSettingsFrontendAdapter(store);
            var model = adapter.Load();
            string original = File.ReadAllText(store.SettingsPath);
            model.SetDisplay(2560, 1440, true, 1);
            var candidate = adapter.CreateCandidate(model);
            Assert.Equal(2560, candidate.WindowWidth);
            Assert.Equal(original, File.ReadAllText(store.SettingsPath));
            adapter.Apply(model);
            Assert.Equal(2560, new ClientSettingsStore(root).Load().Settings.WindowWidth);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void OverridesUseOneValidatedPolicyAndLeaveSavedSettingsUnchanged()
    {
        var saved = new RenderQualitySettings();
        var options = new Dictionary<string, string> { ["FORGELINE_RENDER_QUALITY"] = "Ultra",
            ["FORGELINE_RENDER_SCALE"] = "0.5", ["FORGELINE_SHADOW_QUALITY"] = "High", ["FORGELINE_TEXTURE_QUALITY"] = "Half" };
        var requested = ClientGraphicsOverrides.Read(saved, key => options.GetValueOrDefault(key));
        Assert.Equal(RenderQuality.Ultra, requested.Quality);
        Assert.Equal(.5f, requested.ResolutionScale);
        Assert.Equal(DirectionalShadowQuality.High, requested.Shadows);
        Assert.Equal(RenderQuality.Low, requested.Resolve(true, true, 640, 360, 3).Active);
        Assert.Equal(RenderQuality.Medium, saved.Quality);
        options["FORGELINE_RENDER_SCALE"] = "NaN";
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientGraphicsOverrides.Read(saved, key => options.GetValueOrDefault(key)));
        options["FORGELINE_RENDER_SCALE"] = "1";
        options["FORGELINE_SHADOW_QUALITY"] = "255";
        Assert.Throws<ArgumentException>(() => ClientGraphicsOverrides.Read(saved, key => options.GetValueOrDefault(key)));
    }

    [Fact]
    public void NativeDisplayPreviewRestoresLiveWindowDimensions()
    {
        using var platform = new ForgeLine.Platform.Windows.WindowsPlatform();
        using var window = platform.CreateWindow(new("FORGELINE Display Preview", 640, 480));
        using var preview = new ClientDisplayPreview(window);
        preview.Begin(new("FORGELINE", 640, 480), new("FORGELINE", 800, 600));
        Assert.Equal(new WindowSize(800, 600), window.ClientSize);
        preview.Cancel();
        Assert.Equal(new WindowSize(640, 480), window.ClientSize);
        Assert.Equal(WindowMode.Windowed, window.Mode);
    }

    [Fact]
    public void PreviewExpiresCancelsAndKeepsWithoutWritingSettings()
    {
        var window = new PreviewWindow();
        double seconds = 0;
        using var preview = new ClientDisplayPreview(window, () => seconds);
        var previous = new WindowConfiguration("FORGELINE", 1600, 900);
        var candidate = new WindowConfiguration("FORGELINE", 1920, 1080, mode: WindowMode.BorderlessFullscreen);
        preview.Begin(previous, candidate);
        seconds = 14.99;
        Assert.False(preview.Expire());
        seconds = 15;
        Assert.True(preview.Expire());
        Assert.Equal(WindowMode.Windowed, window.Mode);
        Assert.Equal(new WindowSize(1600, 900), window.ClientSize);
        preview.Begin(previous, candidate);
        preview.Keep();
        seconds = 100;
        Assert.False(preview.Expire());
        Assert.Equal(WindowMode.BorderlessFullscreen, window.Mode);
        window.SetMode(WindowMode.Windowed);
        window.FailNext = true;
        Assert.Throws<InvalidOperationException>(() => preview.Begin(previous, candidate));
        Assert.False(preview.Active);
        Assert.Equal(WindowMode.Windowed, window.Mode);
    }

    private sealed class PreviewWindow : IWindow
    {
        public NativeWindowHandle NativeHandle => default;
        public WindowSize ClientSize { get; private set; } = new(1600, 900);
        public uint Dpi => 96;
        public bool IsFocused => true;
        public bool IsMinimized => false;
        public bool IsOpen => true;
        public WindowMode Mode { get; private set; }
        internal bool FailNext { get; set; }
        public void ConfigureDisplay(WindowConfiguration configuration)
        {
            if (FailNext) { FailNext = false; throw new InvalidOperationException("Rejected display mode."); }
            ClientSize = new(configuration.Width, configuration.Height);
            Mode = configuration.Mode;
        }
        public void SetMode(WindowMode mode) => Mode = mode;
        public void RequestClose() { }
        public bool TryDequeueEvent(out WindowEvent windowEvent) { windowEvent = default; return false; }
        public bool TryDequeueInputEvent(out PlatformInputEvent inputEvent) { inputEvent = default; return false; }
        public void Dispose() { }
    }
}
