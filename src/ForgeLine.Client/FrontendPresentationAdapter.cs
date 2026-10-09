using System.Reflection;
using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal static class FrontendPresentationAdapter
{
    private static readonly string s_productVersion =
        ResolveProductVersion();

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
                            FrontendDesign.FitText(entry.DisplayName),
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

    private static string ResolveProductVersion()
    {
        string? informationalVersion =
            typeof(FrontendPresentationAdapter)
                .Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "0.0.0";
        }

        int metadataSeparator =
            informationalVersion.IndexOf(
                '+',
                StringComparison.Ordinal);

        return metadataSeparator >= 0
            ? informationalVersion[..metadataSeparator]
            : informationalVersion;
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
        SettingsInteractionModel? interaction = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        FrontendSettingsSnapshot settings =
            model.Settings;

        FrontendSettingsField? focused = interaction?.FocusedField;

        return FrontendSurfaceView.Detail(
            "SETTINGS",
            [
                new FrontendDetailLineView(
                    "DISPLAY",
                    $"{settings.WindowWidth} X {settings.WindowHeight}"),
                new FrontendDetailLineView(
                    "BORDERLESS",
                    settings.BorderlessFullscreen ? "ON" : "OFF",
                    IsFocused: focused == FrontendSettingsField.BorderlessFullscreen,
                    CanDecrease: true,
                    CanIncrease: true),
                new FrontendDetailLineView(
                    "UI SCALE",
                    settings.UiScale.ToString(
                        "0.00",
                        System.Globalization.CultureInfo.InvariantCulture),
                    IsFocused: focused == FrontendSettingsField.UiScale,
                    CanDecrease: settings.UiScale > FrontendDesign.MinimumScale,
                    CanIncrease: settings.UiScale < FrontendDesign.MaximumScale),
                new FrontendDetailLineView(
                    "EDGE SCROLL",
                    settings.EdgeScrollEnabled ? "ON" : "OFF",
                    IsFocused: focused == FrontendSettingsField.EdgeScroll,
                    CanDecrease: true,
                    CanIncrease: true),
                new FrontendDetailLineView(
                    "CAMERA SPEED",
                    settings.CameraPanSpeedMultiplier.ToString(
                        "0.00",
                        System.Globalization.CultureInfo.InvariantCulture),
                    IsFocused: focused == FrontendSettingsField.CameraSpeed,
                    CanDecrease: settings.CameraPanSpeedMultiplier > 0.25f,
                    CanIncrease: settings.CameraPanSpeedMultiplier < 3.0f),
                new FrontendDetailLineView(
                    "ONBOARDING",
                    settings.ShowOnboarding ? "ON" : "OFF",
                    IsFocused: focused == FrontendSettingsField.Onboarding,
                    CanDecrease: true,
                    CanIncrease: true)
            ],
            "UP/DOWN  SELECT     LEFT/RIGHT  CHANGE     ENTER  APPLY     ESC  BACK",
            "APPLY");
    }

    internal static FrontendSurfaceView Controls(
        RtsCameraBindings bindings,
        string footer = "ESC  BACK",
        bool edgeScrollEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(bindings);

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
                    (edgeScrollEnabled ? "EDGE PAN" : "EDGE PAN DISABLED")),
                new FrontendDetailLineView(
                    "SELECT / MOVE",
                    "LEFT SELECT  SHIFT+LEFT MULTI  RIGHT MOVE"),
                new FrontendDetailLineView(
                    "COMMAND PANELS",
                    "B BUILD  P PROCESS  U UNITS  L LOGISTICS  Y SUPPLY  K COMBAT  H TECHNOLOGY"),
                new FrontendDetailLineView(
                    "F1 / F2 / F3",
                    "HELP  WORLD DEBUG  FORMATION"),
                new FrontendDetailLineView(
                    "F4 / F5 / F6",
                    "COMMAND CORE  POWER PLANT  EXTRACTOR"),
                new FrontendDetailLineView(
                    "F7 / F8 / F9",
                    "STORAGE DEPOT  SMELTER  ROTATE BUILDING"),
                new FrontendDetailLineView(
                    "DEVELOPER METRICS",
                    "SHIFT+F1 TOGGLE PERFORMANCE METRICS"),
                new FrontendDetailLineView(
                    "F10 / F11 / F12",
                    "STRATEGIC OVERLAY  MINIMAP  HELP  ESC/SPACE PAUSE")
            ],
            footer);
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
