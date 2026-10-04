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
    private static (float X, float Y) Local(float pointerX, float pointerY, FrontendLayout layout) =>
        ((pointerX - layout.OffsetX) / layout.Scale, (pointerY - layout.OffsetY) / layout.Scale);
    private const float DetailStartY = 390f;
    private const float DetailRowHeight = 58f;
    public static int? DetailRow(float pointerX, float pointerY, FrontendLayout layout, int rowCount)
    {
        (float x, float y) = Local(pointerX, pointerY, layout);
        if (rowCount <= 0 ||
            x < 92f ||
            x > 1072f)
        {
            return null;
        }

        float localY = y - DetailStartY;
        if (localY < 0)
        {
            return null;
        }

        int row = (int)(localY / DetailRowHeight);
        return row < rowCount ? row : null;
    }

    public static int? DetailAdjust(float pointerX, float pointerY, FrontendLayout layout, int rowCount)
    {
        int? row = DetailRow(pointerX, pointerY, layout, rowCount);
        if (row is null)
        {
            return null;
        }

        (float x, _) = Local(pointerX, pointerY, layout);
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

    public static bool PrimaryAction(float pointerX, float pointerY, FrontendLayout layout)
    {
        (float x, float y) = Local(pointerX, pointerY, layout);
        return x >= 782f &&
            x <= 1072f &&
            y >= 838f &&
            y <= 896f;
    }

    public static bool SecondaryAction(float pointerX, float pointerY, FrontendLayout layout)
    {
        (float x, float y) = Local(pointerX, pointerY, layout);
        return x >= 92f &&
            x <= 312f &&
            y >= 838f &&
            y <= 896f;
    }

    public static bool Footer(float pointerX, float pointerY, FrontendLayout layout)
    {
        (float x, float y) = Local(pointerX, pointerY, layout);
        return x >= 82f &&
            x <= 1072f &&
            y >= 900f &&
            y <= 970f;
    }

    public static string? MainMenu(
        float pointerX,
        float pointerY,
        FrontendLayout layout,
        IReadOnlyList<MainMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        (float x, float pointerLocalY) = Local(pointerX, pointerY, layout);
        float y = 338f;

        foreach (MainMenuItem item in items)
        {
            if (item.IsEnabled &&
                x >= 82f &&
                x <= 642f &&
                pointerLocalY >= y &&
                pointerLocalY <= y + 52f)
            {
                return item.Id;
            }

            y += 72f;
        }

        return null;
    }
}
