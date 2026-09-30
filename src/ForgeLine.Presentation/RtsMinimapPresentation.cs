using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct RtsFogPresentation(
    IntelligenceState IntelligenceState,
    RtsFogPattern Pattern,
    float Opacity)
{
    public static RtsFogPresentation Resolve(
        IntelligenceState state) =>
        state switch
        {
            IntelligenceState.Visible =>
                new RtsFogPresentation(
                    state,
                    RtsFogPattern.Clear,
                    0.0f),
            IntelligenceState.Explored =>
                new RtsFogPresentation(
                    state,
                    RtsFogPattern.Hatch,
                    0.52f),
            IntelligenceState.Unexplored =>
                new RtsFogPresentation(
                    state,
                    RtsFogPattern.Solid,
                    0.90f),
            _ =>
                new RtsFogPresentation(
                    state,
                    RtsFogPattern.Hatch,
                    0.58f)
        };
}

public readonly record struct RtsFogCell(
    VisibilityCellCoordinate Cell,
    RtsFogPresentation Presentation);

public readonly record struct RtsMinimapSymbol(
    RtsMinimapSymbolKind Kind,
    RtsUiIcon Icon,
    Vector3 WorldPosition,
    EntityId Entity,
    bool IsCurrent,
    bool IsSelected);

public sealed class RtsMinimapModel
{
    private readonly RtsFogCell[] _fogCells;
    private readonly RtsMinimapSymbol[] _symbols;

    public RtsMinimapModel(
        in AxisAlignedBounds worldBounds,
        float intelligenceCellSizeMeters,
        IReadOnlyList<RtsFogCell> fogCells,
        IReadOnlyList<RtsMinimapSymbol> symbols)
    {
        if (!float.IsFinite(intelligenceCellSizeMeters) ||
            intelligenceCellSizeMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intelligenceCellSizeMeters));
        }

        WorldBounds = worldBounds;
        IntelligenceCellSizeMeters =
            intelligenceCellSizeMeters;
        _fogCells =
            fogCells?.ToArray() ??
            throw new ArgumentNullException(nameof(fogCells));
        _symbols =
            symbols?.ToArray() ??
            throw new ArgumentNullException(nameof(symbols));
    }

    public AxisAlignedBounds WorldBounds { get; }

    public float IntelligenceCellSizeMeters { get; }

    public IReadOnlyList<RtsFogCell> FogCells =>
        _fogCells;

    public IReadOnlyList<RtsMinimapSymbol> Symbols =>
        _symbols;
}

public static class RtsMinimapModelBuilder
{
    public static RtsMinimapModel Build(
        PresentationSnapshot snapshot,
        in AxisAlignedBounds worldBounds,
        PlayerId localPlayer,
        IReadOnlyCollection<EntityId>? selectedEntities = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!localPlayer.IsSpecified)
        {
            throw new ArgumentException(
                "Minimap mapping requires a local player.",
                nameof(localPlayer));
        }

        var selected =
            selectedEntities is null
                ? []
                : new HashSet<EntityId>(
                    selectedEntities);
        var symbols =
            new List<RtsMinimapSymbol>(
                snapshot.InstanceCount + 16);

        for (int index = 0;
             index < snapshot.InstanceCount;
             index++)
        {
            RenderInstance instance =
                snapshot.GetInstance(index);

            if ((instance.Visibility &
                 RenderVisibilityMask.World) == 0 ||
                instance.VfxFeature.IsSpecified)
            {
                continue;
            }

            if (instance.UnitFeature.IsSpecified)
            {
                PlayerId owner =
                    instance.Selectable.Owner;
                if (!owner.IsSpecified)
                {
                    continue;
                }

                bool friendly =
                    owner == localPlayer;
                RtsMinimapSymbolKind kind =
                    friendly
                        ? RtsMinimapSymbolKind.FriendlyUnit
                        : RtsMinimapSymbolKind.VisibleEnemy;
                bool isSelected =
                    selected.Contains(
                        instance.Entity);

                symbols.Add(
                    new RtsMinimapSymbol(
                        kind,
                        RtsUiIconCatalog.ResolveMinimap(
                            kind),
                        instance.Transform.Position,
                        instance.Entity,
                        true,
                        isSelected));

                if (isSelected)
                {
                    symbols.Add(
                        new RtsMinimapSymbol(
                            RtsMinimapSymbolKind.SelectedGroup,
                            RtsUiIcon.MinimapSelectedGroup,
                            instance.Transform.Position,
                            instance.Entity,
                            true,
                            true));
                }

                continue;
            }

            if (instance.BuildingFeature.IsSpecified)
            {
                PlayerId owner =
                    instance.Selectable.Owner;
                BuildingId building =
                    instance.BuildingFeature.Building;
                RtsMinimapSymbolKind kind =
                    building == BuildingIds.CommandCore
                        ? RtsMinimapSymbolKind.CommandStructure
                        : building is var depot &&
                          (depot == BuildingIds.SupplyDepot ||
                           depot == BuildingIds.StorageDepot)
                            ? RtsMinimapSymbolKind.Depot
                            : RtsMinimapSymbolKind.Building;
                bool isSelected =
                    selected.Contains(
                        instance.Entity);

                symbols.Add(
                    new RtsMinimapSymbol(
                        kind,
                        RtsUiIconCatalog.ResolveMinimap(
                            kind),
                        instance.Transform.Position,
                        instance.Entity,
                        true,
                        isSelected));

                if (building == BuildingIds.CommandCore)
                {
                    symbols.Add(
                        new RtsMinimapSymbol(
                            RtsMinimapSymbolKind.Objective,
                            RtsUiIcon.MinimapObjective,
                            instance.Transform.Position,
                            instance.Entity,
                            true,
                            isSelected));

                    if (owner == localPlayer &&
                        snapshot.PlayerExperience is
                            PlayerExperienceSnapshot experience &&
                        (experience.Alerts &
                         (PlayerAlertState.CommandCoreDamaged |
                          PlayerAlertState.CommandCoreDestroyed)) != 0)
                    {
                        symbols.Add(
                            new RtsMinimapSymbol(
                                RtsMinimapSymbolKind.AttackNotification,
                                RtsUiIcon.MinimapAttackNotification,
                                instance.Transform.Position,
                                instance.Entity,
                                true,
                                isSelected));
                    }
                }

                if (isSelected)
                {
                    symbols.Add(
                        new RtsMinimapSymbol(
                            RtsMinimapSymbolKind.SelectedGroup,
                            RtsUiIcon.MinimapSelectedGroup,
                            instance.Transform.Position,
                            instance.Entity,
                            true,
                            true));
                }

                continue;
            }

            if (instance.WorldFeature.IsSpecified &&
                instance.WorldFeature.Kind ==
                    WorldPresentationKind.ResourceDeposit)
            {
                symbols.Add(
                    new RtsMinimapSymbol(
                        RtsMinimapSymbolKind.Resource,
                        RtsUiIconCatalog.ResolveWorldResource(
                            instance.WorldFeature.Visual),
                        instance.Transform.Position,
                        instance.Entity,
                        true,
                        false));
            }
        }

        FactionIntelligenceSnapshot? intelligence =
            snapshot.Intelligence;
        RtsFogCell[] fogCells =
            intelligence is null
                ? []
                : intelligence.Cells
                    .Select(
                        static cell =>
                            new RtsFogCell(
                                cell.Cell,
                                RtsFogPresentation.Resolve(
                                    cell.State)))
                    .ToArray();

        if (intelligence is not null)
        {
            for (int index = 0;
                 index < intelligence.Contacts.Count;
                 index++)
            {
                IntelligenceContact contact =
                    intelligence.Contacts[index];

                if (contact.State != IntelligenceState.Detected &&
                    contact.IsCurrent)
                {
                    continue;
                }

                symbols.Add(
                    new RtsMinimapSymbol(
                        RtsMinimapSymbolKind.DetectedContact,
                        RtsUiIcon.MinimapDetectedContact,
                        contact.LastKnownPosition,
                        EntityId.Invalid,
                        contact.IsCurrent,
                        false));
            }
        }

        return new RtsMinimapModel(
            worldBounds,
            intelligence?.CellSizeMeters ??
                IntelligenceGridSettings.DefaultCellSizeMeters,
            fogCells,
            symbols);
    }

    public static Vector2 NormalizeWorldPosition(
        Vector3 position,
        in AxisAlignedBounds worldBounds)
    {
        float width =
            worldBounds.Maximum.X -
            worldBounds.Minimum.X;
        float depth =
            worldBounds.Maximum.Z -
            worldBounds.Minimum.Z;

        if (!float.IsFinite(width) ||
            !float.IsFinite(depth) ||
            width <= 0.0f ||
            depth <= 0.0f)
        {
            return Vector2.Zero;
        }

        return new Vector2(
            Math.Clamp(
                (position.X -
                 worldBounds.Minimum.X) /
                width,
                0.0f,
                1.0f),
            Math.Clamp(
                (position.Z -
                 worldBounds.Minimum.Z) /
                depth,
                0.0f,
                1.0f));
    }
}

public readonly record struct RtsInformationLayerView(
    bool MinimapEnabled,
    StrategicOverlayMode OverlayMode,
    RtsCursorKind Cursor,
    bool HasPointer,
    Vector2 PointerPosition,
    bool IsDragSelecting,
    Vector2 DragStart,
    Vector2 DragCurrent,
    EntityId[] SelectedEntities)
{
    public static RtsInformationLayerView Empty =>
        new(
            true,
            StrategicOverlayMode.None,
            RtsCursorKind.Default,
            false,
            Vector2.Zero,
            false,
            Vector2.Zero,
            Vector2.Zero,
            []);
}

public sealed class RtsInformationLayerController
{
    public bool MinimapEnabled { get; private set; } =
        true;

    public StrategicOverlayMode OverlayMode { get; private set; } =
        StrategicOverlayMode.None;

    public void ToggleMinimap() =>
        MinimapEnabled =
            !MinimapEnabled;

    public void CycleOverlay()
    {
        OverlayMode =
            OverlayMode switch
            {
                StrategicOverlayMode.None =>
                    StrategicOverlayMode.Logistics,
                StrategicOverlayMode.Logistics =>
                    StrategicOverlayMode.Supply,
                StrategicOverlayMode.Supply =>
                    StrategicOverlayMode.Sensors,
                StrategicOverlayMode.Sensors =>
                    StrategicOverlayMode.Navigation,
                StrategicOverlayMode.Navigation =>
                    StrategicOverlayMode.All,
                _ =>
                    StrategicOverlayMode.None
            };
    }
}
