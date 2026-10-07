namespace ForgeLine.Presentation;

[Flags]
public enum DebugOverlayCategory : ushort
{
    None = 0,
    Navigation = 1 << 0,
    World = 1 << 1,
    Logistics = 1 << 2,
    Sensors = 1 << 3,
    Combat = 1 << 4,
    Entities = 1 << 5,
    Rendering = 1 << 6,
    All =
        Navigation |
        World |
        Logistics |
        Sensors |
        Combat |
        Entities |
        Rendering
}

public readonly record struct DebugOverlayView(
    bool Enabled,
    DebugOverlayCategory Categories)
{
    public static DebugOverlayView Disabled =>
        new(
            false,
            DebugOverlayCategory.All);

    public DebugOverlayCategory EffectiveCategories =>
        Enabled
            ? Categories &
              DebugOverlayCategory.All
            : DebugOverlayCategory.None;

    public bool IsEnabled(
        DebugOverlayCategory category) =>
        category !=
            DebugOverlayCategory.None &&
        (EffectiveCategories &
         category) ==
        category;
}

public sealed class DebugOverlayController
{
    private DebugOverlayCategory _categories =
        DebugOverlayCategory.All;

    public bool Enabled { get; private set; }

    public DebugOverlayCategory Categories =>
        _categories;

    public DebugOverlayView View =>
        new(
            Enabled,
            _categories);

    public void ToggleMaster()
    {
        Enabled =
            !Enabled;
    }

    public void SetEnabled(
        bool enabled)
    {
        Enabled =
            enabled;
    }

    public void ToggleCategory(
        DebugOverlayCategory category)
    {
        ValidateSingleCategory(
            category);

        if ((_categories &
             category) != 0)
        {
            _categories &=
                ~category;
        }
        else
        {
            _categories |=
                category;
        }
    }

    public void SetCategories(
        DebugOverlayCategory categories)
    {
        if ((categories &
             ~DebugOverlayCategory.All) !=
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(categories));
        }

        _categories =
            categories;
    }

    private static void ValidateSingleCategory(
        DebugOverlayCategory category)
    {
        ushort raw =
            (ushort)category;

        if (category ==
                DebugOverlayCategory.None ||
            (category &
             ~DebugOverlayCategory.All) !=
                0 ||
            (raw &
             (raw - 1)) !=
                0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(category),
                "Debug overlay category toggles require one supported category.");
        }
    }
}

public static class DebugOverlayPolicy
{
    public const DebugOverlayCategory SimulationDataCategories =
        DebugOverlayCategory.Navigation |
        DebugOverlayCategory.World |
        DebugOverlayCategory.Logistics |
        DebugOverlayCategory.Sensors |
        DebugOverlayCategory.Combat |
        DebugOverlayCategory.Entities;

    public static DebugOverlayCategory ResolveRequiredData(
        DebugOverlayCategory debugCategories,
        StrategicOverlayMode strategicOverlay)
    {
        _ = strategicOverlay;

        return
            debugCategories &
            SimulationDataCategories;
    }

    public static bool RequiresPresentationDebugSnapshot(
        DebugOverlayCategory debugCategories,
        StrategicOverlayMode strategicOverlay) =>
        ResolveRequiredData(
            debugCategories,
            strategicOverlay) !=
        DebugOverlayCategory.None;
}
