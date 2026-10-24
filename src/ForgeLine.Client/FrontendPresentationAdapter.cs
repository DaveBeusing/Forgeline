using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal static class FrontendPresentationAdapter
{
    private static readonly string s_productVersion =
        ClientBuildIdentity.DisplayVersion;

    internal static FrontendSurfaceView Loading(
        FrontendLoadingState state) =>
        FrontendSurfaceView.Loading(
            state.Status,
            state.HasDeterminateProgress,
            state.Progress) with { IsError = state.HasFailed };

    internal static FrontendSurfaceView MainMenu(
        MainMenuModel menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        FrontendMenuEntryView[] entries =
            menu.Items
                .Select(
                    item =>
                        new FrontendMenuEntryView(
                            item.Id,
                            item.Label,
                            item.IsEnabled,
                            string.Equals(
                                item.Id,
                                menu.FocusedId,
                                StringComparison.Ordinal)))
                .ToArray();

        return FrontendSurfaceView.MainMenu(
            entries,
            s_productVersion);
    }

    internal static FrontendSurfaceView PauseMenu(
        PauseMenuModel menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        FrontendMenuEntryView[] entries =
            menu.Items
                .Select(
                    item =>
                        new FrontendMenuEntryView(
                            item.Id,
                            item.Label,
                            item.IsEnabled,
                            string.Equals(
                                item.Id,
                                menu.FocusedId,
                                StringComparison.Ordinal)))
                .ToArray();

        return FrontendSurfaceView.PauseMenu(entries);
    }

    internal static FrontendSurfaceView NewGame(
        NewGameModel model) =>
        FrontendSurfaceView.Detail(
            "NEW GAME",
            [
                new FrontendDetailLineView(
                    "BATTLEFIELD",
                    model.Configuration.MapName),
                new FrontendDetailLineView(
                    "FACTION",
                    model.Configuration.FactionName),
                new FrontendDetailLineView(
                    "SEED",
                    model.Configuration.Seed.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    IsFocused: true,
                    CanDecrease: model.Configuration.Seed > 0,
                    CanIncrease: model.Configuration.Seed < ulong.MaxValue)
            ],
            model.CanStartSelectedScenario
                ? "UP/DOWN  SCENARIO     LEFT/RIGHT  SEED     ENTER  START     ESC  BACK"
                : model.SelectedScenario.Error ?? "SCENARIO UNAVAILABLE",
            model.CanStartSelectedScenario ? "START" : string.Empty)
        with { DisabledPrimaryAction = model.CanStartSelectedScenario ? string.Empty : "START" };

    internal static FrontendSurfaceView LoadGame(
        LoadGameModel model, int visibleRows = FrontendDesign.MaximumVisibleDetailRows)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!model.HasSaves)
        {
            return FrontendSurfaceView.Detail(
                "LOAD GAME",
                [
                    new FrontendDetailLineView(
                        "STATUS",
                        "NO SAVED MATCHES")
                ],
                "ESC  BACK") with { DisabledPrimaryAction = "LOAD" };
        }

        LoadGameEntry[] visibleEntries =
            ResolveVisibleSaves(model, visibleRows);

        FrontendDetailLineView[] lines =
            visibleEntries
                .Select(
                    entry =>
                        new FrontendDetailLineView(
                            entry.DisplayName,
                            entry.CanLoad
                                ? $"TICK {entry.SavedTick}"
                                : entry.State.ToString().ToUpperInvariant(),
                            !entry.CanLoad,
                            IsFocused:
                                model.FocusedEntry is LoadGameEntry selected &&
                                string.Equals(entry.Id, selected.Id, StringComparison.Ordinal), IsSelectable: true))
                .ToArray();

        return FrontendSurfaceView.Detail(
            "LOAD GAME",
            lines,
            "UP/DOWN  SELECT SAVE     ENTER  LOAD     ESC  BACK",
            model.TryGetFocusedLoadTarget(out _) ? "LOAD" : string.Empty) with { DisabledPrimaryAction = model.TryGetFocusedLoadTarget(out _) ? "" : "LOAD", TotalDetailRows = model.Entries.Count, DetailRowOffset = model.VisibleStartIndex(visibleRows) };
    }

    private static LoadGameEntry[] ResolveVisibleSaves(
        LoadGameModel model, int visibleRows) =>
        model.Entries
            .Skip(
                model.VisibleStartIndex(
                    visibleRows))
            .Take(
                visibleRows)
            .ToArray();

    internal static FrontendSurfaceView Settings(
        SettingsModel model,
        SettingsInteractionModel? interaction = null, string? displayFeedback = null, bool displayPreviewActive = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        FrontendSettingsSnapshot settings =
            model.Settings;

        interaction ??= new SettingsInteractionModel();
        FrontendSettingsField? focused = interaction.FocusedField;
        var graphics = model.GraphicsSettings;
        var effective = graphics.Resolve(true, false, settings.WindowWidth, settings.WindowHeight, 3);
        string Choice<T>(T? requested, T active) where T : struct => requested?.ToString()?.ToUpperInvariant() ?? GraphicsSettingsText.Get("Profile") + " / " + active.ToString()?.ToUpperInvariant();
        string Toggle(bool? requested, bool active) => requested.HasValue ? GraphicsSettingsText.Get(requested.Value ? "On" : "Off") : GraphicsSettingsText.Get("Profile") + " / " + GraphicsSettingsText.Get(active ? "On" : "Off");
        GameplayAction action = interaction.BindingAction;
        var registry = model.Bindings;

        var lines = new FrontendDetailLineView[]
            {
                new FrontendDetailLineView(
                    GraphicsSettingsText.Get("OutputResolution"),
                    $"{settings.WindowWidth} X {settings.WindowHeight}",
                    IsFocused: focused == FrontendSettingsField.OutputResolution, CanDecrease: true, CanIncrease: true),
                new FrontendDetailLineView(
                    GraphicsSettingsText.Get("BorderlessFullscreen"),
                    settings.BorderlessFullscreen ? "ON" : "OFF",
                    IsFocused: focused == FrontendSettingsField.BorderlessFullscreen,
                    CanDecrease: true,
                    CanIncrease: true),
                new FrontendDetailLineView(
                    GraphicsSettingsText.Get("UiScale"),
                    settings.UiScale.ToString(
                        "0.00",
                        System.Globalization.CultureInfo.InvariantCulture),
                    IsFocused: focused == FrontendSettingsField.UiScale,
                    CanDecrease: settings.UiScale > FrontendDesign.MinimumScale,
                    CanIncrease: settings.UiScale < FrontendDesign.MaximumScale),
                new FrontendDetailLineView(
                    GraphicsSettingsText.Get("EdgeScroll"),
                    settings.EdgeScrollEnabled ? "ON" : "OFF",
                    IsFocused: focused == FrontendSettingsField.EdgeScroll,
                    CanDecrease: true,
                    CanIncrease: true),
                new FrontendDetailLineView(
                    GraphicsSettingsText.Get("CameraSpeed"),
                    settings.CameraPanSpeedMultiplier.ToString(
                        "0.00",
                        System.Globalization.CultureInfo.InvariantCulture),
                    IsFocused: focused == FrontendSettingsField.CameraSpeed,
                    CanDecrease: settings.CameraPanSpeedMultiplier > .5f,
                    CanIncrease: settings.CameraPanSpeedMultiplier < 2.5f),
                new FrontendDetailLineView(
                    GraphicsSettingsText.Get("Onboarding"),
                    settings.ShowOnboarding ? "ON" : "OFF",
                    IsFocused: focused == FrontendSettingsField.Onboarding,
                    CanDecrease: true,
                    CanIncrease: true)
                , new FrontendDetailLineView(GraphicsSettingsText.Get("BindingAction"), GameplayBindingRegistry.Definitions[(int)action].Label,
                    IsFocused: focused == FrontendSettingsField.BindingAction, CanDecrease: true, CanIncrease: true),
                new FrontendDetailLineView(GraphicsSettingsText.Get("BindingKey"), interaction.BindingCandidate is { } candidate ?
                    GameplayBindingRegistry.KeyLabel(candidate) : registry.Prompt(action),
                    IsFocused: focused == FrontendSettingsField.BindingKey, CanDecrease: true, CanIncrease: true),
                new FrontendDetailLineView(GraphicsSettingsText.Get("RenderQuality"), settings.RenderQuality.ToString().ToUpperInvariant(),
                    IsFocused: focused == FrontendSettingsField.RenderQuality, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("ResolutionScale"), graphics.ResolutionScale.HasValue ? $"{graphics.ResolutionScale.Value * 100:0}%" : GraphicsSettingsText.Get("Profile") + " / 100%", IsFocused: focused == FrontendSettingsField.ResolutionScale, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("Antialiasing"), Choice(graphics.Antialiasing, effective.PostProcessing.Antialiasing), IsFocused: focused == FrontendSettingsField.Antialiasing, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("Shadows"), Choice(graphics.Shadows, effective.Shadows.Quality), IsFocused: focused == FrontendSettingsField.Shadows, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("Textures"), Choice(graphics.Textures, effective.Textures), IsFocused: focused == FrontendSettingsField.Textures, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("ObjectDetail"), Choice(graphics.ObjectDetail, graphics.Quality), IsFocused: focused == FrontendSettingsField.ObjectDetail, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("SurfaceDetail"), Toggle(graphics.SurfaceDetail, effective.TerrainDetail.Enabled), IsFocused: focused == FrontendSettingsField.SurfaceDetail, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("VegetationMotion"), Toggle(graphics.VegetationMotion, effective.VegetationMotion), IsFocused: focused == FrontendSettingsField.VegetationMotion, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("Atmosphere"), Choice(graphics.Atmosphere, effective.Atmosphere), IsFocused: focused == FrontendSettingsField.Atmosphere, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("Particles"), Choice(graphics.Particles, effective.Particles.Quality), IsFocused: focused == FrontendSettingsField.Particles, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("ReducedUiMotion"), settings.FeedbackSettings.ReducedMotion ? "ON" : "OFF", IsFocused: focused == FrontendSettingsField.ReducedUiMotion, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("UiSoundEnabled"), settings.FeedbackSettings.SoundEnabled ? "ON" : "OFF", IsFocused: focused == FrontendSettingsField.UiSoundEnabled, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("UiSoundVolume"), $"{settings.FeedbackSettings.SoundVolume * 100:0}%", IsFocused: focused == FrontendSettingsField.UiSoundVolume, CanDecrease: settings.FeedbackSettings.SoundVolume > 0, CanIncrease: settings.FeedbackSettings.SoundVolume < 1),
                new(GraphicsSettingsText.Get("WorldSoundEnabled"), settings.WorldAudio.Enabled ? "ON" : "OFF", IsFocused: focused == FrontendSettingsField.WorldSoundEnabled, CanDecrease: true, CanIncrease: true),
                new(GraphicsSettingsText.Get("MasterSoundVolume"), $"{settings.WorldAudio.Master * 100:0}%", IsFocused: focused == FrontendSettingsField.MasterSoundVolume, CanDecrease: settings.WorldAudio.Master > 0, CanIncrease: settings.WorldAudio.Master < 1),
                new(GraphicsSettingsText.Get("WorldSoundVolume"), $"{settings.WorldAudio.World * 100:0}%", IsFocused: focused == FrontendSettingsField.WorldSoundVolume, CanDecrease: settings.WorldAudio.World > 0, CanIncrease: settings.WorldAudio.World < 1),
                new(GraphicsSettingsText.Get("AlertSoundVolume"), $"{settings.WorldAudio.Alerts * 100:0}%", IsFocused: focused == FrontendSettingsField.AlertSoundVolume, CanDecrease: settings.WorldAudio.Alerts > 0, CanIncrease: settings.WorldAudio.Alerts < 1),
                new(GraphicsSettingsText.Get("AmbienceSoundVolume"), $"{settings.WorldAudio.Ambience * 100:0}%", IsFocused: focused == FrontendSettingsField.AmbienceSoundVolume, CanDecrease: settings.WorldAudio.Ambience > 0, CanIncrease: settings.WorldAudio.Ambience < 1)
            };
        interaction ??= new SettingsInteractionModel();
        foreach (var settingField in Enum.GetValues<FrontendSettingsField>())
        {
            var kind = settingField switch
            {
                FrontendSettingsField.BorderlessFullscreen or FrontendSettingsField.EdgeScroll or FrontendSettingsField.Onboarding or
                FrontendSettingsField.WorldSoundEnabled or FrontendSettingsField.ReducedUiMotion or FrontendSettingsField.UiSoundEnabled or FrontendSettingsField.SurfaceDetail or
                FrontendSettingsField.VegetationMotion => FrontendValueKind.Toggle,
                FrontendSettingsField.UiScale or FrontendSettingsField.CameraSpeed or FrontendSettingsField.UiSoundVolume or
                FrontendSettingsField.MasterSoundVolume or FrontendSettingsField.WorldSoundVolume or FrontendSettingsField.AlertSoundVolume or FrontendSettingsField.AmbienceSoundVolume or FrontendSettingsField.ResolutionScale => FrontendValueKind.Slider,
                FrontendSettingsField.BindingKey => FrontendValueKind.Binding,
                _ => FrontendValueKind.Choice
            };
            bool on = settingField switch
            {
                FrontendSettingsField.BorderlessFullscreen => settings.BorderlessFullscreen,
                FrontendSettingsField.EdgeScroll => settings.EdgeScrollEnabled,
                FrontendSettingsField.Onboarding => settings.ShowOnboarding,
                FrontendSettingsField.ReducedUiMotion => settings.FeedbackSettings.ReducedMotion,
                FrontendSettingsField.UiSoundEnabled => settings.FeedbackSettings.SoundEnabled,
                FrontendSettingsField.WorldSoundEnabled => settings.WorldAudio.Enabled,
                FrontendSettingsField.SurfaceDetail => graphics.SurfaceDetail ?? effective.TerrainDetail.Enabled,
                FrontendSettingsField.VegetationMotion => graphics.VegetationMotion ?? effective.VegetationMotion,
                _ => false
            };
            float normalized = settingField switch
            {
                FrontendSettingsField.UiScale => (settings.UiScale - FrontendDesign.MinimumScale) / (FrontendDesign.MaximumScale - FrontendDesign.MinimumScale),
                FrontendSettingsField.CameraSpeed => (settings.CameraPanSpeedMultiplier - .5f) / 2,
                FrontendSettingsField.UiSoundVolume => settings.FeedbackSettings.SoundVolume,
                FrontendSettingsField.MasterSoundVolume => settings.WorldAudio.Master,
                FrontendSettingsField.WorldSoundVolume => settings.WorldAudio.World,
                FrontendSettingsField.AlertSoundVolume => settings.WorldAudio.Alerts,
                FrontendSettingsField.AmbienceSoundVolume => settings.WorldAudio.Ambience,
                FrontendSettingsField.ResolutionScale => ((graphics.ResolutionScale ?? 1) - .5f) * 2,
                _ => 0
            };
            lines[(int)settingField] = lines[(int)settingField] with { ValueKind = kind, IsOn = on, NormalizedValue = Math.Clamp(normalized, 0, 1) };
        }
        var visible = interaction.VisibleFields.Select(field => lines[(int)field]).ToArray();
        return FrontendSurfaceView.Detail("SETTINGS", visible,
            displayFeedback ?? (string.IsNullOrEmpty(interaction.BindingFeedback) ?
                focused is { } field && (field == FrontendSettingsField.OutputResolution || field >= FrontendSettingsField.RenderQuality) ? GraphicsSettingsText.Get(field + "Help") : GraphicsSettingsText.Get("Controls") : interaction.BindingFeedback),
            interaction.CanApply == false ? string.Empty : displayPreviewActive ? GraphicsSettingsText.Get("KeepDisplay") : "APPLY") with
        {
            TotalDetailRows = interaction.SectionFieldCount, DetailRowOffset = interaction.VisibleStart,
            IsError = !interaction.CanApply,
            DisabledPrimaryAction = interaction.CanApply ? "" : "APPLY",
            SettingsTabs = Enum.GetValues<FrontendSettingsSection>().Select(section => new FrontendMenuEntryView(
                section.ToString(), section.ToString().ToUpperInvariant(), true, section == interaction.Section)).ToArray()
        };
    }

    internal static FrontendSurfaceView Controls(
        RtsCameraBindings bindings,
        string footer = "ESC  BACK",
        bool edgeScrollEnabled = true, GameplayBindingRegistry? gameplay = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        gameplay ??= GameplayBindingRegistry.Default;

        string Key(ForgeLine.Platform.PlatformKey key) => GameplayBindingRegistry.KeyLabel(key);
        var lines = new List<FrontendDetailLineView>
        {
            new("PAN FORWARD", $"{Key(bindings.PanForward)} / {Key(bindings.PanForwardAlternate)}"),
            new("PAN LEFT", $"{Key(bindings.PanLeft)} / {Key(bindings.PanLeftAlternate)}"),
            new("PAN BACKWARD", $"{Key(bindings.PanBackward)} / {Key(bindings.PanBackwardAlternate)}"),
            new("PAN RIGHT", $"{Key(bindings.PanRight)} / {Key(bindings.PanRightAlternate)}"),
            new("ROTATE LEFT", Key(bindings.RotateLeft)),
            new("ROTATE RIGHT", Key(bindings.RotateRight)),
            new("PLACE TURN LEFT", "Q (WHILE PLACING)"),
            new("PLACE TURN RIGHT", "E / " + gameplay.Prompt(GameplayAction.RotatePlacement) + " (WHILE PLACING)"),
            new("RING PAGE PREV", "Q (PAGED SUBMENU)"),
            new("RING PAGE NEXT", "E (PAGED SUBMENU)"),
            new("PITCH UP", Key(bindings.PitchUp)),
            new("PITCH DOWN", Key(bindings.PitchDown)),
            new("ZOOM", "MOUSE WHEEL"),
            new("DRAG CAMERA", bindings.DragPanButton.ToString().ToUpperInvariant() + " MOUSE DRAG"),
            new("EDGE SCROLL", edgeScrollEnabled ? "POINTER AT SCREEN EDGE" : "DISABLED"),
            new("SELECT", "LEFT CLICK"),
            new("SELECT VISIBLE SAME TYPE", "DOUBLE LEFT CLICK"),
            new("EXTEND SELECTION", "SHIFT + LEFT CLICK"),
            new("MOVE / TARGET", "RIGHT CLICK"),
            new("CANCEL / PAUSE", "ESC"),
            new("REPEAT BUILDING", "SHIFT + WORLD CLICK"),
            new("ASSIGN CONTROL GROUP", "CTRL + DIGIT"),
            new("RECALL CONTROL GROUP", "DIGIT"),
            new("FOCUS CONTROL GROUP", "DOUBLE DIGIT"),
            new("CLEAR CONTROL GROUP", "CTRL + SHIFT + DIGIT"),
            new("DETAILED STATS", "SHIFT + F1"),
            new("OPTIONAL GUIDE", "SHIFT + F12"),
            new("WORLD DEBUG", "F2")
        };
        lines.AddRange(GameplayBindingRegistry.Definitions.Select(definition =>
            new FrontendDetailLineView(definition.Label, gameplay.Prompt(definition.Action))));
        if (RtsCameraFocusController.IsHomeCameraBinding(bindings))
            lines.Add(new("HOME", "CAMERA BINDING"));
        return FrontendSurfaceView.Detail("CONTROLS", lines,
            "UP/DOWN / WHEEL SCROLL   " + footer) with { Kind = FrontendSurfaceKind.Controls };
    }

    internal static FrontendSurfaceView Credits()
    {
        return FrontendSurfaceView.Detail(
            "CREDITS",
            [
                new("STUDIO", CreditsModel.Studio),
                new("LEAD DEVELOPER", CreditsModel.LeadDeveloper),
                new("FORGELINE VERSION", ClientBuildIdentity.ProductVersion),
                new("ENGINE", CreditsModel.Engine)
            ],
            "ESC  BACK") with { Kind = FrontendSurfaceKind.Credits };
    }
}
