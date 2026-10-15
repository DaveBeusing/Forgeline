using System.Numerics;
using System.Security.Cryptography;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal enum UiReferenceScenario
{
    World, Menu, MenuFocus, MenuDisabled, MenuReveal, Loading, LoadingError,
    NewGame, LoadGame, LoadEmpty, LoadBroken, SettingsLong, ControlsLong, Pause, Credits,
    ResourcePressure, LowPower, SupplyCritical, MixedSelection, Targeting, QueueBlocked,
    Technology, Contacts, DockFocus, DockHover, DockPressed, DockPending, DockAccepted,
    DockRejected, MinimapStrategic, Onboarding, SettingsAudio, SettingsGraphics, Controls
}

/// <summary>Named authored read models for the existing reference capture lane; never dispatches input.</summary>
internal static class UiReferenceStates
{
    private static readonly (UiReferenceScenario Left, UiReferenceScenario Right)[] s_distinct =
    [
        (UiReferenceScenario.Menu, UiReferenceScenario.MenuFocus),
        (UiReferenceScenario.Menu, UiReferenceScenario.MenuDisabled),
        (UiReferenceScenario.Menu, UiReferenceScenario.MenuReveal),
        (UiReferenceScenario.Contacts, UiReferenceScenario.DockHover),
        (UiReferenceScenario.DockHover, UiReferenceScenario.DockPressed),
        (UiReferenceScenario.DockPending, UiReferenceScenario.DockAccepted),
        (UiReferenceScenario.DockAccepted, UiReferenceScenario.DockRejected)
    ];

    internal static void ConfirmDistinct(Dictionary<string, string> hashes, string scope, UiReferenceScenario state, byte[] rgba)
    {
        string hash = Convert.ToHexString(SHA256.HashData(rgba));
        hashes.Add(scope + state, hash);
        foreach (var (left, right) in s_distinct)
        {
            if (state != left && state != right) continue;
            var other = state == left ? right : left;
            if (hashes.TryGetValue(scope + other, out string? previous) && previous == hash)
                throw new InvalidDataException($"UI states {state} and {other} have identical native pixels in {scope}.");
        }
    }

    internal static bool IsFrontend(UiReferenceScenario state) => state is >= UiReferenceScenario.Menu and <= UiReferenceScenario.Credits or
        UiReferenceScenario.SettingsAudio or UiReferenceScenario.SettingsGraphics or UiReferenceScenario.Controls;

    internal static FrontendSurfaceView Frontend(UiReferenceScenario state, int width, int height, uint dpi, float scale)
    {
        var menu = FrontendPresentationAdapter.MainMenu(new MainMenuModel(state != UiReferenceScenario.MenuDisabled));
        var view = state switch
        {
            UiReferenceScenario.Menu or UiReferenceScenario.MenuDisabled => menu,
            UiReferenceScenario.MenuFocus => menu with { MenuEntries = menu.MenuEntries.Select((x, i) => x with { IsFocused = i == 2 }).ToArray() },
            UiReferenceScenario.MenuReveal => menu with { Motion = new UiMotionView(.5f, .5f, 0) },
            UiReferenceScenario.Loading => FrontendSurfaceView.Loading("LOADING SAVED MATCH / 75%", true, .75f),
            UiReferenceScenario.LoadingError => FrontendSurfaceView.Loading("ERROR / SAVED MATCH COULD NOT BE READ / ESC BACK", false, 0),
            UiReferenceScenario.NewGame => FrontendPresentationAdapter.NewGame(new NewGameModel()),
            UiReferenceScenario.LoadEmpty => FrontendPresentationAdapter.LoadGame(new LoadGameModel([])),
            UiReferenceScenario.LoadGame or UiReferenceScenario.LoadBroken => FrontendPresentationAdapter.LoadGame(new LoadGameModel(
                [new("reference", "SAVE / ÄÖÜ äöü ß / " + new string('W', 96), "reference.json", 1234,
                    state == UiReferenceScenario.LoadBroken ? LoadGameEntryState.Corrupt : LoadGameEntryState.Available)])),
            UiReferenceScenario.SettingsLong => FrontendPresentationAdapter.Settings(new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings()))) with
            { DetailLines = [new("AUFLÖSUNG / ÄÖÜ / " + new string('W', 72), "3840 X 2160 / " + new string('W', 120), IsFocused: true, CanDecrease: true, CanIncrease: true)] },
            UiReferenceScenario.ControlsLong => FrontendPresentationAdapter.Controls(new RtsCameraBindings()) with
            { DetailLines = [new("TASTATUR / ÄÖÜ / " + new string('W', 120), "CTRL + SHIFT / " + new string('W', 160))] },
            UiReferenceScenario.Pause => FrontendPresentationAdapter.PauseMenu(new PauseMenuModel()),
            UiReferenceScenario.Credits => FrontendPresentationAdapter.Credits(),
            UiReferenceScenario.Controls => FrontendPresentationAdapter.Controls(new RtsCameraBindings()),
            UiReferenceScenario.SettingsAudio => Settings(FrontendSettingsSection.Audio),
            UiReferenceScenario.SettingsGraphics => Settings(FrontendSettingsSection.Graphics),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        return view with { Layout = FrontendGeometry.Resolve(width, height, scale, dpi, view.Kind == FrontendSurfaceKind.Controls) };
    }

    private static FrontendSurfaceView Settings(FrontendSettingsSection section)
    {
        var interaction = new SettingsInteractionModel();
        interaction.SelectSection(section);
        return FrontendPresentationAdapter.Settings(
            new SettingsModel(ClientSettingsFrontendAdapter.ToSnapshot(new ClientUserSettings())), interaction);
    }

    internal static (PresentationSnapshot Snapshot, PlayerActionPanelView Panel) Hud(PresentationSnapshot source,
        UiReferenceScenario state, int width, int height, uint dpi, float scale)
    {
        if (state == UiReferenceScenario.World) return (source, default);
        if (IsFrontend(state)) return (source, default);
        string dockState = state switch
        {
            UiReferenceScenario.QueueBlocked => "Blocked", UiReferenceScenario.DockPending => "Pending",
            UiReferenceScenario.DockAccepted => "Accepted", UiReferenceScenario.DockRejected => "Rejected",
            UiReferenceScenario.Technology => "Queue", _ => "Ready"
        };
        var mode = state == UiReferenceScenario.Technology ? PlayerActionPanelMode.Technology :
            state == UiReferenceScenario.Targeting ? PlayerActionPanelMode.Tactical :
            state is UiReferenceScenario.Contacts or UiReferenceScenario.DockFocus or UiReferenceScenario.DockHover or UiReferenceScenario.DockPressed
                ? PlayerActionPanelMode.Construction : PlayerActionPanelMode.UnitProduction;
        var (snapshot, panel) = CommandDockCapture.Fixture(source, mode, dockState, width, height, dpi, scale);
        panel = panel with { SelectedIndex = state == UiReferenceScenario.DockFocus ? 1 : 0,
            HoveredIndex = state is UiReferenceScenario.DockHover or UiReferenceScenario.DockPressed ? 1 : -1,
            PointerPressed = state == UiReferenceScenario.DockPressed };
        if (state is UiReferenceScenario.ResourcePressure or UiReferenceScenario.LowPower or UiReferenceScenario.SupplyCritical or UiReferenceScenario.MixedSelection)
        {
            snapshot = BattlefieldHudCapture.State(snapshot, state == UiReferenceScenario.SupplyCritical ? "Supply" :
                state == UiReferenceScenario.MixedSelection ? "Mixed" : "Production");
            if (state == UiReferenceScenario.ResourcePressure)
                snapshot = CopyExperience(snapshot, snapshot.PlayerExperience!.Value with { Resources = new(0, 1, 0, 2, 0, 0, 0) });
            if (state == UiReferenceScenario.MixedSelection)
                snapshot = CopyExperience(snapshot, snapshot.PlayerExperience!.Value with
                { Selection = PlayerSelectionSummary.Empty with { Count = 3, Kind = PlayerSelectionKind.Mixed, DisplayName = "Mixed selection" } });
        }
        if (state == UiReferenceScenario.Onboarding)
            snapshot = new(snapshot.Tick, snapshot.TickDuration, snapshot.SimulationEntityCount, snapshot.Instances,
                snapshot.Intelligence, snapshot.SessionId, snapshot.PlayerExperience, playerActions: snapshot.PlayerActions,
                guidance: new PlayerGuidanceSummary(snapshot.SessionId, snapshot.Tick, snapshot.PlayerExperience!.Value.Player, default));
        return (snapshot, panel);
    }

    private static PresentationSnapshot CopyExperience(PresentationSnapshot s, PlayerExperienceSnapshot experience) =>
        new(s.Tick, s.TickDuration, s.SimulationEntityCount, s.Instances, s.Intelligence, s.SessionId, experience,
            playerActions: s.PlayerActions, vfxMetrics: s.VfxMetrics, alerts: s.Alerts, operations: s.Operations);

    internal static object[] Regions(byte[] rgba, int width, int height, FrontendGeometry? frontend, GameplayHudLayout hud)
    {
        var regions = new List<(string Name, float X, float Y, float Width, float Height)>();
        if (frontend is { } f)
        {
            Add("title", f.Title); Add("navigation", f.Navigation); Add("details", f.Details);
            Add("primary-action", f.PrimaryAction); Add("secondary-action", f.SecondaryAction); Add("feedback", f.Feedback);
            void Add(string name, FrontendRegion r) => regions.Add((name, r.X, r.Y, r.Width, r.Height));
        }
        else
        {
            Add("resources", hud.TopStatusBar); Add("selection", hud.SelectionInspector);
            Add("action-dock", hud.ActionDock); Add("alerts", hud.AlertStack); Add("minimap", hud.Minimap);
            void Add(string name, HudRect r) => regions.Add((name, r.X, r.Y, r.Width, r.Height));
        }
        return regions.Select(r =>
        {
            int x = (int)MathF.Floor(r.X), y = (int)MathF.Floor(r.Y);
            int right = (int)MathF.Ceiling(r.X + r.Width), bottom = (int)MathF.Ceiling(r.Y + r.Height);
            if (x < 0 || y < 0 || right > width || bottom > height || right <= x || bottom <= y)
                throw new InvalidDataException($"UI region {r.Name} escapes native output.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            bool nonuniform = false; var first = rgba.AsSpan((y * width + x) * 4, 4).ToArray();
            for (int row = y; row < bottom; row++)
            {
                var pixels = rgba.AsSpan((row * width + x) * 4, (right - x) * 4); hash.AppendData(pixels);
                for (int pixel = 0; pixel < pixels.Length; pixel += 4) nonuniform |= !pixels.Slice(pixel, 4).SequenceEqual(first);
            }
            return (object)new { name = r.Name, x, y, width = right - x, height = bottom - y,
                pixelSha256 = Convert.ToHexString(hash.GetHashAndReset()), nonuniform };
        }).ToArray();
    }
}
