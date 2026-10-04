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

    public void Focus(FrontendSettingsField field)
    {
        int index = Array.IndexOf(s_fields, field);
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(field));
        }

        _index = index;
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
    private const float DetailStartY = 390f;
    private const float DetailRowHeight = 58f;
    public static int? DetailRow(float pointerX, float pointerY, float scale, int rowCount)
    {
        if (rowCount <= 0 ||
            pointerX < 92f * scale ||
            pointerX > 1072f * scale)
        {
            return null;
        }

        float localY = pointerY / scale - DetailStartY;
        if (localY < 0)
        {
            return null;
        }

        int row = (int)(localY / DetailRowHeight);
        return row < rowCount ? row : null;
    }

    public static int? DetailAdjust(float pointerX, float pointerY, float scale, int rowCount)
    {
        int? row = DetailRow(pointerX, pointerY, scale, rowCount);
        if (row is null)
        {
            return null;
        }

        float x = pointerX / scale;
        if (x >= 830f && x <= 902f)
        {
            return -1;
        }

        if (x >= 916f && x <= 988f)
        {
            return 1;
        }

        return null;
    }

    public static bool PrimaryAction(float pointerX, float pointerY, float scale) =>
        pointerX >= 782f * scale &&
        pointerX <= 1072f * scale &&
        pointerY >= 838f * scale &&
        pointerY <= 896f * scale;

    public static bool SecondaryAction(float pointerX, float pointerY, float scale) =>
        pointerX >= 92f * scale &&
        pointerX <= 312f * scale &&
        pointerY >= 838f * scale &&
        pointerY <= 896f * scale;

    public static bool Footer(float pointerX, float pointerY, float scale) =>
        pointerX >= 82f * scale &&
        pointerX <= 1072f * scale &&
        pointerY >= 900f * scale &&
        pointerY <= 970f * scale;

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
