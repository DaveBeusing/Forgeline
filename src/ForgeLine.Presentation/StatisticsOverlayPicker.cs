using ForgeLine.Input;
using ForgeLine.Platform;

namespace ForgeLine.Presentation;

/// <summary>
/// Nonmodal session-local selection for the existing statistics and debug views.
/// This owns no diagnostic data, simulation state, or privileged capability.
/// </summary>
public sealed class StatisticsOverlayPicker
{
    private static readonly DebugOverlayCategory[] DeveloperCategories =
    [
        DebugOverlayCategory.Rendering,
        DebugOverlayCategory.Navigation,
        DebugOverlayCategory.World,
        DebugOverlayCategory.Logistics,
        DebugOverlayCategory.Sensors,
        DebugOverlayCategory.Combat,
        DebugOverlayCategory.Entities
    ];

    public bool Visible { get; private set; }
    public bool PointerCaptured { get; private set; }

    public void Close() => Visible = false;

    public void ClearDeveloperSelection(DebugOverlayController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        Visible = false;
        controller.SetEnabled(false);
        controller.SetCategories(DebugOverlayCategory.None);
    }

    /// <returns>True when an input was owned by the picker this frame.</returns>
    public bool Update(InputState input, bool canOpen, bool developerAuthorized,
        int width, int height, float scale, DebugOverlayController controller,
        ref bool performanceEnabled)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(controller);
        PointerCaptured = false;
        if (!canOpen || input.FocusLostThisFrame || width <= 0 || height <= 0)
        {
            Visible = false;
            return false;
        }

        bool unmodifiedF1 = input.WasKeyPressed(PlatformKey.F1) &&
            !input.WasKeyPressedWithShift(PlatformKey.F1) &&
            !input.WasKeyPressedWithControl(PlatformKey.F1) &&
            !input.IsKeyDown(PlatformKey.LeftShift) &&
            !input.IsKeyDown(PlatformKey.RightShift) &&
            !input.IsKeyDown(PlatformKey.LeftControl) &&
            !input.IsKeyDown(PlatformKey.RightControl);

        if (unmodifiedF1)
        {
            Visible = !Visible;
            input.SuppressKeyUntilRelease(PlatformKey.F1);
            return true;
        }

        if (!Visible) return false;
        if (input.WasKeyPressed(PlatformKey.Escape))
        {
            Visible = false;
            input.SuppressKeyUntilRelease(PlatformKey.Escape);
            return true;
        }

        StatisticsOverlayPickerGeometry geometry =
            StatisticsOverlayPickerGeometry.Create(width, height, scale, developerAuthorized);
        PointerCaptured = input.HasPointerPosition &&
            geometry.Contains(input.PointerPosition.X, input.PointerPosition.Y);

        if (!input.TryGetMousePressPosition(PlatformMouseButton.Left, out var point))
            return false;

        bool inside = geometry.Contains(point.X, point.Y);
        if (!inside)
        {
            Visible = false;
        }
        else
        {
            int row = geometry.RowAt(point.Y);
            if (row == 0)
                performanceEnabled = !performanceEnabled;
            else if (developerAuthorized && row > 0 && row <= DeveloperCategories.Length)
            {
                DebugOverlayCategory category = DeveloperCategories[row - 1];
                bool selected = controller.View.IsEnabled(category);
                DebugOverlayCategory next = selected
                    ? controller.Categories & ~category
                    : controller.Categories | category;
                controller.SetCategories(next);
                controller.SetEnabled(next != DebugOverlayCategory.None);
            }
        }

        // A close click is not also a selection, order or build command.
        input.SuppressMouseButtonUntilRelease(PlatformMouseButton.Left);
        PointerCaptured = inside;
        return true;
    }

    public StatisticsOverlayPickerView Capture(bool developerAuthorized, bool performanceEnabled,
        DebugOverlayView debug) => new(Visible, developerAuthorized, performanceEnabled,
            developerAuthorized ? debug.EffectiveCategories : DebugOverlayCategory.None);
}

public readonly record struct StatisticsOverlayPickerView(
    bool Visible, bool DeveloperAuthorized, bool Performance,
    DebugOverlayCategory Categories)
{
    public bool IsSelected(DebugOverlayCategory category) =>
        DeveloperAuthorized && (Categories & category) == category;
}

/// <summary>Identical physical-pixel geometry for input and native rendering.</summary>
public readonly record struct StatisticsOverlayPickerGeometry(
    float X, float Y, float Width, float Height, float HeaderHeight,
    float RowHeight, float Scale, int RowCount)
{
    public static StatisticsOverlayPickerGeometry Create(int width, int height,
        float scale, bool developerAuthorized)
    {
        int rows = developerAuthorized ? 8 : 1;
        float requested = float.IsFinite(scale) ? Math.Clamp(scale, .75f, 4f) : 1f;
        float effective = Math.Min(requested, Math.Max(.65f,
            (height - 24f) / (32f + rows * 23f)));
        float w = Math.Min(310f * effective, Math.Max(0, width - 24f));
        float h = (32f + rows * 23f) * effective;
        float x = 12f * effective;
        float y = Math.Clamp(height * .48f - h * .5f, 12f, Math.Max(12f, height - h - 12f));
        return new(x, y, w, h, 32f * effective, 23f * effective, effective, rows);
    }

    public bool Contains(float x, float y) =>
        x >= X && x < X + Width && y >= Y && y < Y + Height;

    public int RowAt(float y)
    {
        float position = y - Y - HeaderHeight;
        return position < 0 ? -1 :
            Math.Clamp((int)(position / RowHeight), 0, RowCount - 1);
    }
}
