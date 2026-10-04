namespace ForgeLine.UI;

public enum FrontendSettingsField : byte
{
    BorderlessFullscreen = 1,
    UiScale = 2,
    EdgeScroll = 3,
    CameraSpeed = 4,
    Onboarding = 5
}

public sealed class SettingsInteractionModel
{
    private static readonly FrontendSettingsField[] s_fields =
        Enum.GetValues<FrontendSettingsField>();
    private int _index;

    public FrontendSettingsField FocusedField =>
        s_fields[_index];

    public FrontendSettingsField MoveNext()
    {
        _index = (_index + 1) % s_fields.Length;
        return FocusedField;
    }

    public FrontendSettingsField MovePrevious()
    {
        _index =
            (_index - 1 + s_fields.Length) %
            s_fields.Length;
        return FocusedField;
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

        switch (FocusedField)
        {
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
                        0.25f,
                        3.0f));
                break;
            case FrontendSettingsField.Onboarding:
                model.SetOnboarding(
                    !settings.ShowOnboarding);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported settings field {FocusedField}.");
        }
    }
}

public static class FrontendHitTesting
{
    public static string? MainMenu(
        float pointerX,
        float pointerY,
        float scale,
        IReadOnlyList<MainMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        float y = 338f * scale;

        foreach (MainMenuItem item in items)
        {
            if (item.IsEnabled &&
                pointerX >= 82f * scale &&
                pointerX <= 642f * scale &&
                pointerY >= y &&
                pointerY <= y + 52f * scale)
            {
                return item.Id;
            }

            y += 72f * scale;
        }

        return null;
    }
}
