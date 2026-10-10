using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;

namespace ForgeLine.Client;

internal sealed record ClientUserSettings
{
    internal const int CurrentSchemaVersion = 2;
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

    public bool EdgeScrollEnabled { get; init; } = true;

    public float CameraPanSpeedMultiplier { get; init; } = 1.0f;

    public float SceneExposure { get; init; } =
        SceneLightingSettings.Default.Exposure;

    public RtsCameraBindings CameraBindings { get; init; } = new();

    public GameplayBindings GameplayBindings { get; init; } = new();

    [JsonIgnore]
    public RtsReferenceZoom ReferenceZoom { get; init; } = RtsReferenceZoom.NormalGameplay;

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

        ArgumentNullException.ThrowIfNull(CameraBindings);
        ValidateBindings(CameraBindings);
        _ = new GameplayBindingRegistry(GameplayBindings, CameraBindings);
    }

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

        if (keys.Any(static key => key == PlatformKey.Unknown))
        {
            throw new InvalidDataException(
                "Camera bindings cannot use the Unknown key.");
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

    internal ClientSettingsLoadResult Load()
    {
        if (!File.Exists(_path))
        {
            var defaults =
                new ClientUserSettings();
            defaults.Validate();
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
            var document = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Settings object is missing.");
            string? gameplayProperty = document.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, "gameplayBindings", StringComparison.OrdinalIgnoreCase));
            JsonNode? gameplay = gameplayProperty is null ? null : document[gameplayProperty];
            if (gameplayProperty is not null) document.Remove(gameplayProperty);
            ClientUserSettings settings =
                JsonSerializer.Deserialize<ClientUserSettings>(
                    document.ToJsonString(),
                    JsonOptions) ??
                throw new InvalidDataException(
                    "Settings file did not contain a settings object.");

            bool migrated = settings.SchemaVersion == 1;
            if (migrated) settings = settings with { SchemaVersion = ClientUserSettings.CurrentSchemaVersion };
            settings.Validate();
            string? bindingRecovery = null;
            if (gameplayProperty is not null)
            {
                try
                {
                    GameplayBindings parsed = gameplay?.Deserialize<GameplayBindings>(JsonOptions) ?? throw new InvalidDataException("Gameplay bindings are missing.");
                    _ = new GameplayBindingRegistry(parsed, settings.CameraBindings);
                    settings = settings with { GameplayBindings = parsed };
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
                {
                    bindingRecovery = $"Gameplay bindings reset: {exception.Message} Saved camera/display settings were preserved.";
                    _ = TryQuarantineInvalidSettings();
                }
            }
            if (migrated || bindingRecovery is not null) Save(settings);

            return new ClientSettingsLoadResult(
                settings,
                _path,
                CreatedDefaults: false,
                RecoveredInvalidSettings: bindingRecovery is not null,
                RecoveryMessage: bindingRecovery);
        }
        catch (Exception exception)
            when (exception is JsonException or
                  InvalidDataException or
                  IOException or
                  UnauthorizedAccessException)
        {
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
                WriteIndented = true
            };
        options.Converters.Add(
            new JsonStringEnumConverter());
        return options;
    }
}
