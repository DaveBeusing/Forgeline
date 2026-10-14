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
            state.Progress);

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
            "LEFT/RIGHT  CHANGE SEED     ENTER  START MATCH     ESC  BACK",
            "START");

    internal static FrontendSurfaceView LoadGame(
        LoadGameModel model)
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
                "ESC  BACK");
        }

        LoadGameEntry[] visibleEntries =
            ResolveVisibleSaves(model);

        FrontendDetailLineView[] lines =
            visibleEntries
                .Select(
                    entry =>
                        new FrontendDetailLineView(
                            FrontendDesign.FitText(entry.DisplayName, 320f, UiTypography.Style(UiTextRole.MenuItem).Size),
                            entry.CanLoad
                                ? $"TICK {entry.SavedTick}"
                                : entry.State.ToString().ToUpperInvariant(),
                            !entry.CanLoad,
                            IsFocused:
                                model.FocusedEntry is LoadGameEntry selected &&
                                string.Equals(entry.Id, selected.Id, StringComparison.Ordinal)))
                .ToArray();

        return FrontendSurfaceView.Detail(
            "LOAD GAME",
            lines,
            "UP/DOWN  SELECT SAVE     ENTER  LOAD     ESC  BACK",
            model.TryGetFocusedLoadTarget(out _) ? "LOAD" : string.Empty);
    }

    private static LoadGameEntry[] ResolveVisibleSaves(
        LoadGameModel model) =>
        model.Entries
            .Skip(
                model.VisibleStartIndex(
                    FrontendDesign.MaximumVisibleDetailRows))
            .Take(
                FrontendDesign.MaximumVisibleDetailRows)
            .ToArray();

    internal static FrontendSurfaceView Settings(
        SettingsModel model,
        SettingsInteractionModel? interaction = null, string? displayFeedback = null, bool displayPreviewActive = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        FrontendSettingsSnapshot settings =
            model.Settings;

        FrontendSettingsField? focused = interaction?.FocusedField;
        var graphics = model.GraphicsSettings;
        var effective = graphics.Resolve(true, false, settings.WindowWidth, settings.WindowHeight, 3);
        string Choice<T>(T? requested, T active) where T : struct => requested?.ToString()?.ToUpperInvariant() ?? GraphicsSettingsText.Get("Profile") + " / " + active.ToString()?.ToUpperInvariant();
        string Toggle(bool? requested, bool active) => requested.HasValue ? GraphicsSettingsText.Get(requested.Value ? "On" : "Off") : GraphicsSettingsText.Get("Profile") + " / " + GraphicsSettingsText.Get(active ? "On" : "Off");
        GameplayAction action = interaction?.BindingAction ?? GameplayAction.Build;
        var registry = model.Bindings;

        return FrontendSurfaceView.Detail(
            "SETTINGS",
            new FrontendDetailLineView[]
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
                new FrontendDetailLineView(GraphicsSettingsText.Get("BindingKey"), interaction?.BindingCandidate is { } candidate ?
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
                new(GraphicsSettingsText.Get("Particles"), Choice(graphics.Particles, effective.Particles.Quality), IsFocused: focused == FrontendSettingsField.Particles, CanDecrease: true, CanIncrease: true)
            }.Skip(interaction?.VisibleStart ?? 0).Take(7).ToArray(),
            displayFeedback ?? (string.IsNullOrEmpty(interaction?.BindingFeedback) ?
                focused is { } field && (field == FrontendSettingsField.OutputResolution || field >= FrontendSettingsField.RenderQuality) ? GraphicsSettingsText.Get(field + "Help") : GraphicsSettingsText.Get("Controls") : interaction.BindingFeedback),
            interaction?.CanApply == false ? string.Empty : displayPreviewActive ? GraphicsSettingsText.Get("KeepDisplay") : "APPLY");
    }

    internal static FrontendSurfaceView Controls(
        RtsCameraBindings bindings,
        string footer = "ESC  BACK",
        bool edgeScrollEnabled = true, GameplayBindingRegistry? gameplay = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        gameplay ??= GameplayBindingRegistry.Default;

        string primaryPan =
            $"{bindings.PanForward.ToString().ToUpperInvariant()}/" +
            $"{bindings.PanLeft.ToString().ToUpperInvariant()}/" +
            $"{bindings.PanBackward.ToString().ToUpperInvariant()}/" +
            bindings.PanRight.ToString().ToUpperInvariant();
        string alternatePan =
            $"{bindings.PanForwardAlternate.ToString().ToUpperInvariant()}/" +
            $"{bindings.PanLeftAlternate.ToString().ToUpperInvariant()}/" +
            $"{bindings.PanBackwardAlternate.ToString().ToUpperInvariant()}/" +
            bindings.PanRightAlternate.ToString().ToUpperInvariant();

        return FrontendSurfaceView.Detail(
            "CONTROLS",
            [
                new FrontendDetailLineView(
                    "CAMERA",
                    $"{primaryPan} OR {alternatePan} PAN  " +
                    $"{bindings.RotateLeft.ToString().ToUpperInvariant()}/" +
                    $"{bindings.RotateRight.ToString().ToUpperInvariant()} ROTATE  " +
                    $"{bindings.PitchUp.ToString().ToUpperInvariant()}/" +
                    $"{bindings.PitchDown.ToString().ToUpperInvariant()} PITCH"),
                new FrontendDetailLineView(
                    "CAMERA MOUSE",
                    $"WHEEL ZOOM  {bindings.DragPanButton.ToString().ToUpperInvariant()} DRAG  " +
                    (edgeScrollEnabled ? "EDGE PAN" : "EDGE PAN DISABLED") +
                    (RtsCameraFocusController.IsHomeCameraBinding(bindings) ? "  HOME USES CAMERA BINDING" : string.Empty) +
                    $"  {gameplay.Prompt(GameplayAction.FocusBase)} BASE FOCUS"),
                new FrontendDetailLineView(
                    "SELECT / MOVE",
                    "LEFT SELECT  DOUBLE LEFT SAME-TYPE VISIBLE UNITS  SHIFT+LEFT MULTI  RIGHT MOVE"),
                new FrontendDetailLineView(
                    "COMMAND PANELS",
                    gameplay.PanelsHelpPrompt + "; " + gameplay.DockNavigationPrompt),
                new FrontendDetailLineView(
                    "HELP / WORLD / FORMATION",
                    "F1 HELP  F2 WORLD DEBUG  " + gameplay.Prompt(GameplayAction.Formation) + " FORMATION"),
                new FrontendDetailLineView(
                    "PLACEMENT",
                    gameplay.Prompt(GameplayAction.CorePlacement) + " COMMAND CORE  " + gameplay.Prompt(GameplayAction.PowerPlacement) + " POWER PLANT  " + gameplay.Prompt(GameplayAction.ExtractorPlacement) + " EXTRACTOR"),
                new FrontendDetailLineView(
                    "PLACEMENT / ROTATE",
                    gameplay.Prompt(GameplayAction.StoragePlacement) + " STORAGE DEPOT  " + gameplay.Prompt(GameplayAction.SmelterPlacement) + " SMELTER  " + gameplay.Prompt(GameplayAction.RotatePlacement) + " ROTATE BUILDING  SHIFT+CLICK REPEAT AFTER ACCEPTANCE"),
                new FrontendDetailLineView(
                    "DEVELOPER METRICS",
                    "SHIFT+F1 TOGGLE PERFORMANCE METRICS"),
                new FrontendDetailLineView(
                    "CONTROL GROUPS",
                    "CTRL+DIGIT ASSIGN  DIGIT RECALL  DOUBLE DIGIT FOCUS  CTRL+SHIFT+DIGIT CLEAR"),
                new FrontendDetailLineView(
                    "OBJECTIVE",
                    "DESTROY THE ENEMY COMMAND CORE"),
                new FrontendDetailLineView(
                    "OPTIONAL GUIDE",
                    "SHIFT+F12 SHOW / HIDE  ONBOARDING SETTING CONTROLS DEFAULT"),
                new FrontendDetailLineView(
                    "MAP / HELP / PAUSE",
                    gameplay.Prompt(GameplayAction.Overlay) + " STRATEGIC OVERLAY  " + gameplay.Prompt(GameplayAction.Minimap) + " MINIMAP  F12 HELP  ESC/" + gameplay.Prompt(GameplayAction.Pause) + " PAUSE")
            ],
            footer) with { Kind = FrontendSurfaceKind.Controls };
    }

    internal static FrontendSurfaceView Credits()
    {
        FrontendDetailLineView[] lines =
            CreditsModel.Sections
                .SelectMany(
                    section =>
                        section.Lines.Select(
                            line =>
                                new FrontendDetailLineView(
                                    section.Heading,
                                    line)))
                .ToArray();

        return FrontendSurfaceView.Detail(
            "CREDITS",
            lines,
            "ESC  BACK");
    }
}
