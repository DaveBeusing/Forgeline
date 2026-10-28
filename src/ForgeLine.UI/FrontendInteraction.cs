using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Presentation;
using ForgeLine.Graphics;

namespace ForgeLine.UI;

public enum FrontendSettingsField : byte
{
    OutputResolution = 0,
    BorderlessFullscreen = 1,
    UiScale = 2,
    EdgeScroll = 3,
    CameraSpeed = 4,
    Onboarding = 5,
    BindingAction = 6,
    BindingKey = 7,
    RenderQuality = 8,
    ResolutionScale = 9, Antialiasing = 10, Shadows = 11, Textures = 12, ObjectDetail = 13,
    SurfaceDetail = 14, VegetationMotion = 15, Atmosphere = 16, Particles = 17, ReducedUiMotion = 18, UiSoundEnabled = 19, UiSoundVolume = 20,
    WorldSoundEnabled = 21, MasterSoundVolume = 22, WorldSoundVolume = 23, AlertSoundVolume = 24, AmbienceSoundVolume = 25, TooltipDelay = 26, TooltipTextSize = 27,
    DisplayPreset = 28, HighContrast = 29, TeamPalette = 30, ResetAccessibility = 31, NotificationMode = 32, InformationalAlerts = 33
}

public enum FrontendSettingsSection : byte { General, Graphics, Audio, Bindings }

public sealed class SettingsInteractionModel
{
    private static readonly FrontendSettingsField[][] s_sections =
    [
        [FrontendSettingsField.OutputResolution, FrontendSettingsField.BorderlessFullscreen, FrontendSettingsField.UiScale,
         FrontendSettingsField.EdgeScroll, FrontendSettingsField.CameraSpeed, FrontendSettingsField.Onboarding, FrontendSettingsField.ReducedUiMotion,
         FrontendSettingsField.TooltipDelay, FrontendSettingsField.TooltipTextSize,
         FrontendSettingsField.DisplayPreset, FrontendSettingsField.HighContrast, FrontendSettingsField.TeamPalette, FrontendSettingsField.ResetAccessibility, FrontendSettingsField.NotificationMode, FrontendSettingsField.InformationalAlerts],
        [FrontendSettingsField.RenderQuality, FrontendSettingsField.ResolutionScale, FrontendSettingsField.Antialiasing,
         FrontendSettingsField.Shadows, FrontendSettingsField.Textures, FrontendSettingsField.ObjectDetail,
         FrontendSettingsField.SurfaceDetail, FrontendSettingsField.VegetationMotion, FrontendSettingsField.Atmosphere, FrontendSettingsField.Particles],
        [FrontendSettingsField.WorldSoundEnabled, FrontendSettingsField.MasterSoundVolume, FrontendSettingsField.WorldSoundVolume,
         FrontendSettingsField.AlertSoundVolume, FrontendSettingsField.AmbienceSoundVolume, FrontendSettingsField.UiSoundEnabled, FrontendSettingsField.UiSoundVolume],
        [FrontendSettingsField.BindingAction, FrontendSettingsField.BindingKey]
    ];
    public FrontendSettingsSection Section { get; private set; }
    private FrontendSettingsField[] Fields => s_sections[(int)Section];
    public int FocusedSectionIndex => _index;
    public IEnumerable<FrontendSettingsField> VisibleFields => Fields.Skip(VisibleStart).Take(VisibleRowCount);
    private int _index = 1;
    private static readonly PlatformKey[] Keys = Enum.GetValues<PlatformKey>();
    public bool CanApply { get; private set; } = true;
    private PlatformKey? _candidate;
    public PlatformKey? BindingCandidate => _candidate;
    public GameplayAction BindingAction { get; private set; }
    public string BindingFeedback { get; private set; } = string.Empty;
    public int VisibleCapacity { get; set; } = 7;
    public int SectionFieldCount => Fields.Length;
    public int VisibleStart => Math.Max(0, _index - Math.Clamp(VisibleCapacity, 1, 7) + 1);
    public int VisibleRowCount => Math.Min(Math.Clamp(VisibleCapacity, 1, 7), Fields.Length - VisibleStart);
    public void FocusVisible(int row) => Focus(Fields[Math.Clamp(row, 0, VisibleRowCount - 1) + VisibleStart]);

    public FrontendSettingsField FocusedField =>
        Fields[_index];

    public FrontendSettingsField MoveNext()
    {
        _index = (_index + 1) % Fields.Length;
        return FocusedField;
    }

    public FrontendSettingsField MovePrevious()
    {
        _index =
            (_index - 1 + Fields.Length) %
            Fields.Length;
        return FocusedField;
    }

    public void Focus(FrontendSettingsField field)
    {
        int section = Array.FindIndex(s_sections, fields => fields.Contains(field));
        if (section < 0) throw new ArgumentOutOfRangeException(nameof(field));
        Section = (FrontendSettingsSection)section;
        int index = Array.IndexOf(Fields, field);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }

        _index = index;
    }

    public void SelectSection(FrontendSettingsSection section)
    {
        if (!Enum.IsDefined(section)) throw new ArgumentOutOfRangeException(nameof(section));
        Section = section;
        _index = 0;
    }

    public bool HandleSectionInput(InputState input, FrontendGeometry layout)
    {
        if (input.WasKeyPressed(PlatformKey.Tab))
        {
            bool reverse = input.IsKeyDown(PlatformKey.LeftShift) || input.IsKeyDown(PlatformKey.RightShift);
            SelectSection((FrontendSettingsSection)(((int)Section + (reverse ? 3 : 1)) % 4));
            input.SuppressHeldInput();
            return true;
        }
        if (input.TryGetMousePressPosition(PlatformMouseButton.Left, out var point))
            for (int i = 0; i < 4; i++)
                if (layout.SettingsTab(i).Contains(point.X, point.Y))
                {
                    SelectSection((FrontendSettingsSection)i);
                    input.SuppressHeldInput();
                    return true;
                }
        return false;
    }

    public void Adjust(
        SettingsModel model,
        int direction)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (direction == 0)
        {
            return;
        }

        FrontendSettingsSnapshot settings =
            model.Settings;

        RenderQualitySettings graphics = model.GraphicsSettings;
        switch (FocusedField)
        {
            case FrontendSettingsField.OutputResolution:
                (int Width, int Height)[] sizes = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160)];
                int sizeIndex = Array.FindIndex(sizes, size => size.Width == settings.WindowWidth && size.Height == settings.WindowHeight);
                sizeIndex = (sizeIndex + Math.Sign(direction) + sizes.Length) % sizes.Length;
                model.SetDisplay(sizes[sizeIndex].Width, sizes[sizeIndex].Height, settings.BorderlessFullscreen, settings.UiScale);
                break;
            case FrontendSettingsField.BorderlessFullscreen:
                model.SetDisplay(
                    settings.WindowWidth,
                    settings.WindowHeight,
                    !settings.BorderlessFullscreen,
                    settings.UiScale);
                break;
            case FrontendSettingsField.UiScale:
                model.SetDisplay(
                    settings.WindowWidth,
                    settings.WindowHeight,
                    settings.BorderlessFullscreen,
                    Math.Clamp(
                        settings.UiScale +
                        Math.Sign(direction) * 0.05f,
                        FrontendDesign.MinimumScale,
                        FrontendDesign.MaximumScale));
                break;
            case FrontendSettingsField.EdgeScroll:
                model.SetCamera(
                    !settings.EdgeScrollEnabled,
                    settings.CameraPanSpeedMultiplier);
                break;
            case FrontendSettingsField.CameraSpeed:
                model.SetCamera(
                    settings.EdgeScrollEnabled,
                    Math.Clamp(
                        settings.CameraPanSpeedMultiplier +
                        Math.Sign(direction) * 0.1f,
                        .5f,
                        2.5f));
                break;
            case FrontendSettingsField.BindingAction:
                BindingAction = (GameplayAction)(((int)BindingAction + Math.Sign(direction) + GameplayBindingRegistry.Definitions.Count) % GameplayBindingRegistry.Definitions.Count);
                _candidate = null;
                CanApply = true;
                BindingFeedback = string.Empty;
                break;
            case FrontendSettingsField.BindingKey:
                var bindings = settings.GameplayBindings ?? new();
                var registry = model.Bindings;

                PlatformKey current = _candidate ?? registry.Key(BindingAction);
                if (_candidate is null && current == PlatformKey.Unknown) current = GameplayBindingRegistry.Definitions[(int)BindingAction].DefaultKey;
                int keyIndex = Array.IndexOf(Keys, current);
                keyIndex = (keyIndex + Math.Sign(direction) + Keys.Length) % Keys.Length;
                PlatformKey candidate = Keys[keyIndex];
                _candidate = candidate;
                try
                {
                    model.SetGameplayBindings(candidate == PlatformKey.Unknown
                        ? bindings.Reset(BindingAction) : bindings.With(BindingAction, candidate));
                    CanApply = true;
                    BindingFeedback = candidate == PlatformKey.Unknown
                        ? "DEFAULT RESTORED - APPLY TO SAVE" : "BINDING READY - APPLY TO SAVE";
                }
                catch (InvalidDataException exception) { CanApply = false; BindingFeedback = exception.Message.ToUpperInvariant(); }
                break;
            case FrontendSettingsField.RenderQuality:
                model.SetRenderQuality((FrontendRenderQuality)(((int)settings.RenderQuality + Math.Sign(direction) + 4) % 4));
                break;
            case FrontendSettingsField.ResolutionScale:
                float?[] scales = [null, .5f, .75f, 1f];
                int scaleIndex = Array.IndexOf(scales, graphics.ResolutionScale);
                model.SetGraphicsSettings(graphics with { ResolutionScale = scales[(scaleIndex + Math.Sign(direction) + scales.Length) % scales.Length] });
                break;
            case FrontendSettingsField.Antialiasing:
                model.SetGraphicsSettings(graphics with { Antialiasing = Cycle(graphics.Antialiasing, direction) }); break;
            case FrontendSettingsField.Shadows:
                model.SetGraphicsSettings(graphics with { Shadows = Cycle(graphics.Shadows, direction) }); break;
            case FrontendSettingsField.Textures:
                model.SetGraphicsSettings(graphics with { Textures = Cycle(graphics.Textures, direction) }); break;
            case FrontendSettingsField.ObjectDetail:
                model.SetGraphicsSettings(graphics with { ObjectDetail = Cycle(graphics.ObjectDetail, direction) }); break;
            case FrontendSettingsField.Atmosphere:
                model.SetGraphicsSettings(graphics with { Atmosphere = Cycle(graphics.Atmosphere, direction) }); break;
            case FrontendSettingsField.Particles:
                model.SetGraphicsSettings(graphics with { Particles = Cycle(graphics.Particles, direction) }); break;
            case FrontendSettingsField.SurfaceDetail:
                model.SetGraphicsSettings(graphics with { SurfaceDetail = Cycle(graphics.SurfaceDetail, direction) }); break;
            case FrontendSettingsField.VegetationMotion:
                model.SetGraphicsSettings(graphics with { VegetationMotion = Cycle(graphics.VegetationMotion, direction) }); break;
            case FrontendSettingsField.ReducedUiMotion:
                model.SetUiFeedback(settings.FeedbackSettings with { ReducedMotion = !settings.FeedbackSettings.ReducedMotion }); break;
            case FrontendSettingsField.DisplayPreset:
                model.ApplyAccessibilityPreset((AccessibilityPreset)(((int)(model.DisplayPreset ?? AccessibilityPreset.Standard) + Math.Sign(direction) + 3) % 3)); break;
            case FrontendSettingsField.HighContrast:
                model.SetTacticalDisplay(settings.TacticalDisplay with { HighContrast = !settings.TacticalDisplay.HighContrast }); break;
            case FrontendSettingsField.TeamPalette:
                model.SetTacticalDisplay(settings.TacticalDisplay with { TeamPalette = (TacticalTeamPalette)(((int)settings.TacticalDisplay.TeamPalette + Math.Sign(direction) + 3) % 3) }); break;
            case FrontendSettingsField.ResetAccessibility:
                model.ResetAccessibility(); break;
            case FrontendSettingsField.UiSoundEnabled:
                model.SetUiFeedback(settings.FeedbackSettings with { SoundEnabled = !settings.FeedbackSettings.SoundEnabled }); break;
            case FrontendSettingsField.UiSoundVolume:
                model.SetUiFeedback(settings.FeedbackSettings with { SoundVolume = Math.Clamp(settings.FeedbackSettings.SoundVolume + Math.Sign(direction) * .05f, 0, 1) }); break;
            case FrontendSettingsField.WorldSoundEnabled:
                model.SetWorldAudio(settings.WorldAudio with { Enabled = !settings.WorldAudio.Enabled }); break;
            case FrontendSettingsField.MasterSoundVolume:
                model.SetWorldAudio(settings.WorldAudio with { Master = Math.Clamp(settings.WorldAudio.Master + Math.Sign(direction) * .05f, 0, 1) }); break;
            case FrontendSettingsField.WorldSoundVolume:
                model.SetWorldAudio(settings.WorldAudio with { World = Math.Clamp(settings.WorldAudio.World + Math.Sign(direction) * .05f, 0, 1) }); break;
            case FrontendSettingsField.AlertSoundVolume:
                model.SetWorldAudio(settings.WorldAudio with { Alerts = Math.Clamp(settings.WorldAudio.Alerts + Math.Sign(direction) * .05f, 0, 1) }); break;
            case FrontendSettingsField.AmbienceSoundVolume:
                model.SetWorldAudio(settings.WorldAudio with { Ambience = Math.Clamp(settings.WorldAudio.Ambience + Math.Sign(direction) * .05f, 0, 1) }); break;
            case FrontendSettingsField.TooltipDelay:
                model.SetTooltips(settings.Tooltips with { DelayMilliseconds = Math.Clamp(settings.Tooltips.DelayMilliseconds + Math.Sign(direction) * 25, 0, 1000) }); break;
            case FrontendSettingsField.TooltipTextSize:
                model.SetTooltips(settings.Tooltips with { TextSize = (HoverTooltipTextSize)(((int)settings.Tooltips.TextSize + Math.Sign(direction) + 3) % 3) }); break;
            case FrontendSettingsField.NotificationMode:
                model.SetNotifications(settings.Notifications with { Mode = (NotificationMode)(((int)settings.Notifications.Mode + Math.Sign(direction) + 3) % 3) }); break;
            case FrontendSettingsField.InformationalAlerts:
                model.SetNotifications(settings.Notifications with { ShowInformational = !settings.Notifications.ShowInformational }); break;
            case FrontendSettingsField.Onboarding:
                model.SetOnboarding(
                    !settings.ShowOnboarding);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported settings field {FocusedField}.");
        }
    }
    private static T? Cycle<T>(T? current, int direction) where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        int index = current.HasValue ? Array.IndexOf(values, current.Value) + 1 : 0;
        index = (index + Math.Sign(direction) + values.Length + 1) % (values.Length + 1);
        return index == 0 ? null : values[index - 1];
    }
    private static bool? Cycle(bool? current, int direction)
    {
        bool?[] values = [null, false, true];
        return values[(Array.IndexOf(values, current) + Math.Sign(direction) + 3) % 3];
    }
}

public static class FrontendHitTesting
{
    public static int? DetailRow(float pointerX, float pointerY, FrontendLayout layout, int rowCount)
    {
        for (int i = 0; i < Math.Min(rowCount, layout.Geometry.VisibleDetailRows); i++)
            if (layout.Geometry.DetailRow(i).Contains(pointerX, pointerY)) return i;
        return null;
    }
    public static int? DetailAdjust(float pointerX, float pointerY, FrontendLayout layout, int rowCount)
    {
        if (DetailRow(pointerX, pointerY, layout, rowCount) is not int row) return null;
        return layout.Geometry.Decrease(row).Contains(pointerX, pointerY) ? -1 :
            layout.Geometry.Increase(row).Contains(pointerX, pointerY) ? 1 : null;
    }
    public static FrontendSurfaceView WithDetailPointer(in FrontendSurfaceView view, FrontendLayout layout, float x, float y, bool pressed)
    {
        if (view.Kind != FrontendSurfaceKind.Detail) return view;
        int? row = DetailRow(x, y, layout, view.DetailLines.Count);
        if (row is null) return view;
        var line = view.DetailLines[row.Value];
        if (!line.IsSelectable && !line.CanDecrease && !line.CanIncrease) return view;
        var lines = view.DetailLines.ToArray();
        int direction = DetailAdjust(x,y,layout,view.DetailLines.Count) ?? 0;
        bool enabled = direction < 0 ? line.CanDecrease : direction > 0 && line.CanIncrease;
        lines[row.Value] = line with { IsHovered = true, IsPressed = pressed,
            AdjustHover = enabled ? direction : 0, AdjustPress = enabled && pressed ? direction : 0 };
        return view with { DetailLines = lines };
    }
    public static FrontendSurfaceView WithMenuPointer(in FrontendSurfaceView view, FrontendLayout layout, float x, float y, bool pressed)
    {
        var result = view;
        if (view.Kind is FrontendSurfaceKind.MainMenu or FrontendSurfaceKind.PauseMenu)
        {
            var entries = view.MenuEntries.ToArray();
            for(int i=0;i<entries.Length;i++)
            {
                bool hover = entries[i].IsEnabled && layout.Geometry.MenuRow(i).Contains(x,y);
                entries[i] = entries[i] with { IsHovered = hover, IsPressed = hover && pressed };
            }
            result = result with { MenuEntries = entries };
        }
        if (view.SettingsTabs is { } tabs)
        {
            var entries = tabs.ToArray();
            for(int i=0;i<entries.Length;i++)
            {
                bool hover = entries[i].IsEnabled && layout.Geometry.SettingsTab(i).Contains(x,y);
                entries[i] = entries[i] with { IsHovered = hover, IsPressed = hover && pressed };
            }
            result = result with { SettingsTabs = entries };
        }
        return result;
    }
    public static int? DetailAdjust(float x, float y, FrontendLayout layout, in FrontendSurfaceView view)
    {
        int? row = DetailRow(x, y, layout, view.DetailLines.Count);
        if (row is not int index) return null;
        int? direction = DetailAdjust(x, y, layout, view.DetailLines.Count);
        var line = view.DetailLines[index];
        return direction == -1 && line.CanDecrease ? -1 : direction == 1 && line.CanIncrease ? 1 : null;
    }
    public static bool PrimaryAction(float x, float y, FrontendLayout layout) => layout.Geometry.PrimaryAction.Contains(x, y);
    public static bool SecondaryAction(float x, float y, FrontendLayout layout) => layout.Geometry.SecondaryAction.Contains(x, y);
    public static bool Footer(float x, float y, FrontendLayout layout) => layout.Geometry.Footer.Contains(x, y);
    public static string? MainMenu(float x, float y, FrontendLayout layout, IReadOnlyList<MainMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (int i = 0; i < items.Count; i++)
            if (items[i].IsEnabled && layout.Geometry.MenuRow(i).Contains(x, y)) return items[i].Id;
        return null;
    }
    public static string? PauseMenu(float x, float y, FrontendLayout layout, IReadOnlyList<PauseMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (int i = 0; i < items.Count; i++)
            if (items[i].IsEnabled && layout.Geometry.MenuRow(i).Contains(x, y)) return items[i].Id;
        return null;
    }
}
