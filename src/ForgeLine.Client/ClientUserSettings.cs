using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using ForgeLine.Audio;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Graphics;

namespace ForgeLine.Client;

internal sealed record ClientUserSettings
{
    internal const int CurrentSchemaVersion = 4;
    private const int MinimumWindowWidth = 1_024;
    private const int MinimumWindowHeight = 720;
    private const int MaximumWindowWidth = 7_680;
    private const int MaximumWindowHeight = 4_320;
    private const float MinimumUiScale = 0.75f;
    private const float MaximumUiScale = 2.0f;
    private const float MinimumCameraPanSpeedMultiplier = 0.5f;
    private const float MaximumCameraPanSpeedMultiplier = 2.5f;
    private const float MinimumSceneExposure = 0.25f;
    private const float MaximumSceneExposure = 4.0f;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public int WindowWidth { get; init; } = 1_600;

    public int WindowHeight { get; init; } = 900;

    public bool BorderlessFullscreen { get; init; }

    public float UiScale { get; init; } = 1.0f;

    public bool ShowOnboarding { get; init; } = true;

    public bool ShowStudioSplash { get; init; } = true;

    public bool ReducedUiMotion { get; init; }
    public bool UiSoundEnabled { get; init; }
    public float UiSoundVolume { get; init; } = .35f;
    public UiFeedbackSettings CreateUiFeedbackSettings() => new(ReducedUiMotion, UiSoundEnabled, UiSoundVolume);

    public WorldAudioSettings WorldAudio { get; init; } = WorldAudioSettings.Default;

    public HoverTooltipSettings Tooltips { get; init; } = HoverTooltipSettings.Default;

    public bool EdgeScrollEnabled { get; init; } = true;

    public float CameraPanSpeedMultiplier { get; init; } = 1.0f;

    public float SceneExposure { get; init; } =
        SceneLightingSettings.Default.Exposure;

    public RtsCameraBindings CameraBindings { get; init; } = new();
    public SceneAtmosphereSettings SceneAtmosphere { get; init; } = SceneAtmosphereSettings.Default;
    public ParticleSettings Particles { get; init; } = ParticleSettings.Default;
    public RenderQualitySettings RenderQuality { get; init; } = new();

    public GameplayBindings GameplayBindings { get; init; } = new();

    [JsonIgnore]
    public RtsReferenceZoom ReferenceZoom { get; init; } = RtsReferenceZoom.CloseTactical;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported settings schema version {SchemaVersion}. Expected {CurrentSchemaVersion}.");
        }

        if (WindowWidth < MinimumWindowWidth ||
            WindowWidth > MaximumWindowWidth)
        {
            throw new InvalidDataException(
                $"WindowWidth must be between {MinimumWindowWidth} and {MaximumWindowWidth}.");
        }

        if (WindowHeight < MinimumWindowHeight ||
            WindowHeight > MaximumWindowHeight)
        {
            throw new InvalidDataException(
                $"WindowHeight must be between {MinimumWindowHeight} and {MaximumWindowHeight}.");
        }

        RequireRange(
            UiScale,
            MinimumUiScale,
            MaximumUiScale,
            nameof(UiScale));
        RequireRange(
            CameraPanSpeedMultiplier,
            MinimumCameraPanSpeedMultiplier,
            MaximumCameraPanSpeedMultiplier,
            nameof(CameraPanSpeedMultiplier));
        RequireRange(
            SceneExposure,
            MinimumSceneExposure,
            MaximumSceneExposure,
            nameof(SceneExposure));

        CreateUiFeedbackSettings().Validate();
        WorldAudio.Validate();
        ArgumentNullException.ThrowIfNull(Tooltips);
        Tooltips.Validate();
        ArgumentNullException.ThrowIfNull(CameraBindings);
        SceneAtmosphere.Validate();
        Particles.Validate();
        RenderQuality.Validate();
        ValidateBindings(CameraBindings);
        _ = new GameplayBindingRegistry(GameplayBindings, CameraBindings);
    }

    public RenderQualitySettings CreateRenderQualitySettings() => RenderQuality with
    {
        Atmosphere = RenderQuality.Atmosphere ?? (SceneAtmosphere.Quality != AtmosphereQuality.Disabled ? SceneAtmosphere.Quality : null),
        Particles = RenderQuality.Particles ?? (Particles != ParticleSettings.Default ? Particles.Quality : null)
    };
    public WindowConfiguration CreateWindowConfiguration() =>
        new(
            "FORGELINE",
            WindowWidth,
            WindowHeight,
            resizable: true,
            BorderlessFullscreen
                ? WindowMode.BorderlessFullscreen
                : WindowMode.Windowed);

    public SceneLightingSettings CreateSceneLightingSettings() =>
        SceneLightingSettings.Default with
        {
            Exposure =
                SceneExposure
        };

    public RtsCameraSettings CreateCameraSettings(
        Vector3 initialTarget) =>
        RtsVisualReference.CreateCameraSettings(initialTarget, ReferenceZoom) with
        {
            BasePanSpeedUnitsPerSecond =
                42.0f *
                CameraPanSpeedMultiplier,
            EdgeScrollEnabled = EdgeScrollEnabled
        };

    private static void ValidateBindings(
        RtsCameraBindings bindings)
    {
        PlatformKey[] keys =
        [
            bindings.PanForward,
            bindings.PanForwardAlternate,
            bindings.PanBackward,
            bindings.PanBackwardAlternate,
            bindings.PanLeft,
            bindings.PanLeftAlternate,
            bindings.PanRight,
            bindings.PanRightAlternate,
            bindings.RotateLeft,
            bindings.RotateRight,
            bindings.PitchUp,
            bindings.PitchDown
        ];

        if (keys.Any(static key => key == PlatformKey.Unknown || GameplayBindingRegistry.IsRetiredFunctionKey(key)))
        {
            throw new InvalidDataException(
                "Camera bindings cannot use Unknown or retired F3-F9 keys.");
        }

        if (bindings.DragPanButton == PlatformMouseButton.None)
        {
            throw new InvalidDataException(
                "DragPanButton cannot use the None mouse button.");
        }

        PlatformKey[] primaryKeys =
        [
            bindings.PanForward,
            bindings.PanBackward,
            bindings.PanLeft,
            bindings.PanRight,
            bindings.RotateLeft,
            bindings.RotateRight,
            bindings.PitchUp,
            bindings.PitchDown
        ];

        if (primaryKeys.Distinct().Count() !=
            primaryKeys.Length)
        {
            throw new InvalidDataException(
                "Primary camera bindings must be unique.");
        }
    }

    private static void RequireRange(
        float value,
        float minimum,
        float maximum,
        string name)
    {
        if (!float.IsFinite(value) ||
            value < minimum ||
            value > maximum)
        {
            throw new InvalidDataException(
                $"{name} must be finite and between {minimum} and {maximum}.");
        }
    }
}

internal readonly record struct ClientSettingsLoadResult(
    ClientUserSettings Settings,
    string Path,
    bool CreatedDefaults,
    bool RecoveredInvalidSettings,
    string? RecoveryMessage);

internal sealed class ClientSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    private readonly string _path;

    internal ClientSettingsStore(
        string? settingsRoot = null)
    {
        string root =
            string.IsNullOrWhiteSpace(settingsRoot)
                ? Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData)
                : settingsRoot;

        _path =
            Path.Combine(
                root,
                "FORGELINE",
                "settings.json");
    }

    internal string SettingsPath => _path;

    internal ClientSettingsLoadResult Load() => LoadCancellable(CancellationToken.None);

    internal ClientSettingsLoadResult LoadCancellable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
        {
            var defaults =
                new ClientUserSettings();
            defaults.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            Save(defaults);

            return new ClientSettingsLoadResult(
                defaults,
                _path,
                CreatedDefaults: true,
                RecoveredInvalidSettings: false,
                RecoveryMessage: null);
        }

        try
        {
            string json =
                File.ReadAllText(_path);
            cancellationToken.ThrowIfCancellationRequested();
            var document = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Settings object is missing.");
            string? gameplayProperty = document.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, "gameplayBindings", StringComparison.OrdinalIgnoreCase));
            JsonNode? gameplay = gameplayProperty is null ? null : document[gameplayProperty];
            if (gameplayProperty is not null) document.Remove(gameplayProperty);
            string? graphicsProperty = document.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, "renderQuality", StringComparison.OrdinalIgnoreCase));
            JsonNode? graphics = graphicsProperty is null ? null : document[graphicsProperty];
            if (graphicsProperty is not null) document.Remove(graphicsProperty);
            string? tooltipProperty = document.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, "tooltips", StringComparison.OrdinalIgnoreCase));
            JsonNode? tooltip = tooltipProperty is null ? null : document[tooltipProperty];
            if (tooltipProperty is not null) document.Remove(tooltipProperty);
            ClientUserSettings settings =
                JsonSerializer.Deserialize<ClientUserSettings>(
                    document.ToJsonString(),
                    JsonOptions) ??
                throw new InvalidDataException(
                    "Settings file did not contain a settings object.");

            bool legacyGraphics = settings.SchemaVersion is 1 or 2;
            bool migrated = settings.SchemaVersion is 1 or 2 or 3;
            if (migrated) settings = settings with { SchemaVersion = ClientUserSettings.CurrentSchemaVersion };
            ArgumentNullException.ThrowIfNull(settings.CameraBindings);
            var normalizedCamera = settings.CameraBindings.WithoutRetiredFunctionKeys();
            migrated |= !ReferenceEquals(normalizedCamera, settings.CameraBindings);
            settings = settings with { CameraBindings = normalizedCamera };
            settings.Validate();
            string? graphicsRecovery = null;
            if (graphicsProperty is not null)
            {
                try
                {
                    RenderQualitySettings parsed = graphics?.Deserialize<RenderQualitySettings>(JsonOptions) ?? throw new InvalidDataException("Graphics settings are missing.");
                    parsed.Validate();
                    settings = settings with { RenderQuality = parsed };
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
                {
                    graphicsRecovery = $"Graphics settings reset: {exception.Message} Display and input settings were preserved.";
                    _ = TryQuarantineInvalidSettings();
                    settings = settings with { SceneAtmosphere = SceneAtmosphereSettings.Default, Particles = ParticleSettings.Default };
                }
            }
            if (legacyGraphics && graphicsRecovery is null)
            {
                var converted = settings.CreateRenderQualitySettings();
                settings = settings with
                {
                    RenderQuality = converted with { Textures = converted.Textures ?? TextureDetailQuality.Full, VegetationMotion = converted.VegetationMotion ?? true },
                    SceneAtmosphere = settings.SceneAtmosphere with { Quality = AtmosphereQuality.Disabled },
                    Particles = ParticleSettings.Default
                };
            }
            string? tooltipRecovery = null;
            if (tooltipProperty is not null)
            {
                try
                {
                    var parsed = tooltip?.Deserialize<HoverTooltipSettings>(JsonOptions) ?? throw new InvalidDataException("Tooltip settings are missing.");
                    parsed.Validate();
                    settings = settings with { Tooltips = parsed };
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
                {
                    tooltipRecovery = $"Tooltip settings reset: {exception.Message} Other settings were preserved.";
                    _ = TryQuarantineInvalidSettings();
                }
            }
            string? bindingRecovery = null;
            if (gameplayProperty is not null)
            {
                try
                {
                    GameplayBindings parsed = gameplay?.Deserialize<GameplayBindings>(JsonOptions) ?? throw new InvalidDataException("Gameplay bindings are missing.");
                    var normalizedBindings = parsed.WithoutRetiredFunctionKeys();
                    migrated |= !ReferenceEquals(normalizedBindings, parsed);
                    parsed = normalizedBindings;
                    _ = new GameplayBindingRegistry(parsed, settings.CameraBindings);
                    settings = settings with { GameplayBindings = parsed };
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
                {
                    bindingRecovery = $"Gameplay bindings reset: {exception.Message} Saved camera/display settings were preserved.";
                    _ = TryQuarantineInvalidSettings();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (migrated || bindingRecovery is not null || graphicsRecovery is not null || tooltipRecovery is not null) Save(settings);

            return new ClientSettingsLoadResult(
                settings,
                _path,
                CreatedDefaults: false,
                RecoveredInvalidSettings: bindingRecovery is not null || graphicsRecovery is not null || tooltipRecovery is not null,
                RecoveryMessage: graphicsRecovery is null && bindingRecovery is null && tooltipRecovery is null ? null :
                    string.Join(" ", new[] { graphicsRecovery, bindingRecovery, tooltipRecovery }.Where(message => message is not null)));
        }
        catch (Exception exception)
            when (exception is JsonException or
                  ArgumentException or
                  InvalidDataException or
                  IOException or
                  UnauthorizedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? recoveryPath =
                TryQuarantineInvalidSettings();
            var defaults =
                new ClientUserSettings();
            defaults.Validate();
            Save(defaults);

            string recoveryMessage =
                recoveryPath is null
                    ? exception.Message
                    : $"{exception.Message} Invalid settings were moved to {recoveryPath}.";

            return new ClientSettingsLoadResult(
                defaults,
                _path,
                CreatedDefaults: false,
                RecoveredInvalidSettings: true,
                recoveryMessage);
        }
    }

    internal void Save(
        ClientUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        string? directory =
            System.IO.Path.GetDirectoryName(_path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath =
            _path +
            ".tmp";
        string json =
            JsonSerializer.Serialize(
                settings,
                JsonOptions);

        try
        {
            File.WriteAllText(
                temporaryPath,
                json);
            File.Move(
                temporaryPath,
                _path,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string? TryQuarantineInvalidSettings()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            string invalidPath =
                _path +
                $".invalid-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(
                _path,
                invalidPath,
                overwrite: false);
            return invalidPath;
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                IncludeFields = true
            };
        options.Converters.Add(
            new JsonStringEnumConverter());
        return options;
    }
}
