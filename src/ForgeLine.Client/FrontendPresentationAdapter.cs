using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal static class FrontendPresentationAdapter
{
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

        return FrontendSurfaceView.MainMenu(entries);
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
                        System.Globalization.CultureInfo.InvariantCulture))
            ],
            "ENTER  START MATCH     ESC  BACK");

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

        FrontendDetailLineView[] lines =
            model.Entries
                .Select(
                    entry =>
                        new FrontendDetailLineView(
                            ReferenceEquals(entry, model.FocusedEntry)
                                ? $"> {entry.DisplayName}"
                                : entry.DisplayName,
                            entry.CanLoad
                                ? $"TICK {entry.SavedTick}"
                                : entry.State.ToString().ToUpperInvariant(),
                            !entry.CanLoad))
                .ToArray();

        return FrontendSurfaceView.Detail(
            "LOAD GAME",
            lines,
            "UP/DOWN  SELECT SAVE     ENTER  LOAD     ESC  BACK");
    }

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
                    focused == FrontendSettingsField.BorderlessFullscreen ? "> BORDERLESS" : "BORDERLESS",
                    settings.BorderlessFullscreen ? "ON" : "OFF"),
                new FrontendDetailLineView(
                    focused == FrontendSettingsField.UiScale ? "> UI SCALE" : "UI SCALE",
                    settings.UiScale.ToString(
                        "0.00",
                        System.Globalization.CultureInfo.InvariantCulture)),
                new FrontendDetailLineView(
                    focused == FrontendSettingsField.EdgeScroll ? "> EDGE SCROLL" : "EDGE SCROLL",
                    settings.EdgeScrollEnabled ? "ON" : "OFF"),
                new FrontendDetailLineView(
                    focused == FrontendSettingsField.CameraSpeed ? "> CAMERA SPEED" : "CAMERA SPEED",
                    settings.CameraPanSpeedMultiplier.ToString(
                        "0.00",
                        System.Globalization.CultureInfo.InvariantCulture)),
                new FrontendDetailLineView(
                    focused == FrontendSettingsField.Onboarding ? "> ONBOARDING" : "ONBOARDING",
                    settings.ShowOnboarding ? "ON" : "OFF")
            ],
            "UP/DOWN  SELECT     LEFT/RIGHT  CHANGE     ENTER  APPLY     ESC  BACK");
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
