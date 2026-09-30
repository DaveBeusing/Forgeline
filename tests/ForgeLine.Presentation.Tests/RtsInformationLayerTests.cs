using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RtsInformationLayerTests
{
    private static readonly PlayerId LocalPlayer = new(1);
    private static readonly PlayerId EnemyPlayer = new(2);
    private static readonly FactionId LocalFaction = new(1);
    private static readonly FactionId EnemyFaction = new(2);

    [Fact]
    public void CatalogContainsStableDistinctProductionIconFamilies()
    {
        Assert.Equal(
            61,
            RtsUiIconCatalog.All.Count);

        Assert.Equal(
            RtsUiIcon.UnitArmor,
            RtsUiIconCatalog.ResolveUnitRole(
                UnitIds.MainBattleTank));
        Assert.Equal(
            RtsUiIcon.UnitRepair,
            RtsUiIconCatalog.ResolveUnitRole(
                UnitIds.CombatEngineer));
        Assert.Equal(
            RtsUiIcon.BuildingSupply,
            RtsUiIconCatalog.ResolveBuildingRole(
                BuildingIds.SupplyDepot));
        Assert.Equal(
            RtsUiIcon.SupplyCritical,
            RtsUiIconCatalog.ResolveSupply(
                BattlefieldSupplyStatus.Critical));

        string[] ids =
            RtsUiIconCatalog.All
                .Select(
                    static definition =>
                        definition.AssetId)
                .ToArray();

        Assert.Equal(
            ids.Length,
            ids.Distinct(
                    StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public void CursorResolverUsesGeometrySemanticBeforeGenericMovement()
    {
        RtsCursorContext buildInvalid =
            new(
                HasPointer: true,
                IsDragSelecting: false,
                IsPanning: false,
                BuildingPlacementActive: true,
                BuildingPlacementValid: false,
                TacticalTargetingMode.None,
                TacticalTargetValid: false,
                HoveredSelectable: false,
                HasSelection: true);

        Assert.Equal(
            RtsCursorKind.Invalid,
            RtsCursorResolver.Resolve(
                buildInvalid));

        Assert.Equal(
            RtsCursorKind.AttackMove,
            RtsCursorResolver.Resolve(
                buildInvalid with
                {
                    BuildingPlacementActive = false,
                    TargetingMode =
                        TacticalTargetingMode.AttackMove,
                    TacticalTargetValid = true
                }));

        Assert.Equal(
            RtsCursorKind.DragSelect,
            RtsCursorResolver.Resolve(
                buildInvalid with
                {
                    BuildingPlacementActive = false,
                    IsDragSelecting = true
                }));
    }

    [Fact]
    public void DpiScaleIsBoundedAndUsesNinetySixDpiBaseline()
    {
        Assert.Equal(
            1.0f,
            RtsUiLayout.ScaleForDpi(
                96));
        Assert.Equal(
            1.5f,
            RtsUiLayout.ScaleForDpi(
                144));
        Assert.Equal(
            0.75f,
            RtsUiLayout.ScaleForDpi(
                1));
        Assert.Equal(
            2.5f,
            RtsUiLayout.ScaleForDpi(
                480));
    }

    [Fact]
    public void FogStatesUseDistinctOpacityAndPattern()
    {
        RtsFogPresentation unexplored =
            RtsFogPresentation.Resolve(
                IntelligenceState.Unexplored);
        RtsFogPresentation explored =
            RtsFogPresentation.Resolve(
                IntelligenceState.Explored);
        RtsFogPresentation visible =
            RtsFogPresentation.Resolve(
                IntelligenceState.Visible);

        Assert.Equal(
            RtsFogPattern.Solid,
            unexplored.Pattern);
        Assert.Equal(
            RtsFogPattern.Hatch,
            explored.Pattern);
        Assert.Equal(
            RtsFogPattern.Clear,
            visible.Pattern);
        Assert.True(
            unexplored.Opacity >
            explored.Opacity);
        Assert.Equal(
            0.0f,
            visible.Opacity);
    }

    [Fact]
    public void MinimapMapsVisibleEntitiesAndOpaqueDetectedContactsOnly()
    {
        var bounds =
            new AxisAlignedBounds(
                Vector3.Zero,
                new Vector3(
                    128.0f,
                    10.0f,
                    128.0f));
        var selected =
            new EntityId(
                1,
                1);
        var visibleEnemy =
            new EntityId(
                2,
                1);
        var resource =
            new EntityId(
                3,
                1);
        var commandCore =
            new EntityId(
                4,
                1);
        var detectedEnemy =
            new EntityId(
                99,
                1);

        var intelligence =
            new FactionIntelligenceStore(
                new IntelligenceGridSettings
                {
                    CellSizeMeters =
                        32.0f
                });
        intelligence.BeginTick(
            new SimulationTick(
                7));
        intelligence.MarkVisibleCircle(
            LocalFaction,
            new Vector3(
                24.0f,
                0.0f,
                24.0f),
            20.0f);
        intelligence.Observe(
            LocalFaction,
            detectedEnemy,
            new IntelligenceSignature(
                EnemyFaction,
                identityKey: 501),
            new Vector3(
                96.0f,
                0.0f,
                96.0f),
            IntelligenceState.Detected,
            new SimulationTick(
                7));

        RenderInstance[] instances =
        [
            Unit(
                selected,
                new Vector3(
                    20.0f,
                    0.0f,
                    20.0f),
                LocalPlayer,
                UnitIds.MainBattleTank),
            Unit(
                visibleEnemy,
                new Vector3(
                    60.0f,
                    0.0f,
                    60.0f),
                EnemyPlayer,
                UnitIds.ScoutVehicle),
            Resource(
                resource,
                new Vector3(
                    40.0f,
                    0.0f,
                    80.0f)),
            Building(
                commandCore,
                new Vector3(
                    16.0f,
                    0.0f,
                    16.0f),
                LocalPlayer,
                BuildingIds.CommandCore)
        ];

        var snapshot =
            new PresentationSnapshot(
                new SimulationTick(
                    7),
                TimeSpan.FromMilliseconds(
                    50),
                instances.Length,
                instances,
                intelligence.Capture(
                    LocalFaction,
                    bounds));

        RtsMinimapModel model =
            RtsMinimapModelBuilder.Build(
                snapshot,
                bounds,
                LocalPlayer,
                [selected]);

        Assert.Contains(
            model.Symbols,
            symbol =>
                symbol.Kind ==
                    RtsMinimapSymbolKind.FriendlyUnit &&
                symbol.Entity ==
                    selected);
        Assert.Contains(
            model.Symbols,
            symbol =>
                symbol.Kind ==
                    RtsMinimapSymbolKind.SelectedGroup &&
                symbol.Entity ==
                    selected);
        Assert.Contains(
            model.Symbols,
            symbol =>
                symbol.Kind ==
                    RtsMinimapSymbolKind.VisibleEnemy &&
                symbol.Entity ==
                    visibleEnemy);
        Assert.Contains(
            model.Symbols,
            symbol =>
                symbol.Kind ==
                    RtsMinimapSymbolKind.Resource);
        Assert.Contains(
            model.Symbols,
            symbol =>
                symbol.Kind ==
                    RtsMinimapSymbolKind.Objective &&
                symbol.Entity ==
                    commandCore);
        Assert.Contains(
            model.Symbols,
            symbol =>
                symbol.Kind ==
                    RtsMinimapSymbolKind.DetectedContact &&
                !symbol.Entity.IsValid &&
                symbol.IsCurrent);

        Assert.DoesNotContain(
            model.Symbols,
            symbol =>
                symbol.Entity ==
                    detectedEnemy);
        Assert.Contains(
            model.FogCells,
            cell =>
                cell.Presentation.Pattern ==
                    RtsFogPattern.Solid);
        Assert.Contains(
            model.FogCells,
            cell =>
                cell.Presentation.Pattern ==
                    RtsFogPattern.Clear);
    }

    [Fact]
    public void SupplyStatesResolveToDistinctSemanticIcons()
    {
        RtsUiIcon[] icons =
        [
            RtsUiIconCatalog.ResolveSupply(
                BattlefieldSupplyStatus.Supplied),
            RtsUiIconCatalog.ResolveSupply(
                BattlefieldSupplyStatus.LowSupply),
            RtsUiIconCatalog.ResolveSupply(
                BattlefieldSupplyStatus.Critical),
            RtsUiIconCatalog.ResolveSupply(
                BattlefieldSupplyStatus.Unsupplied)
        ];

        Assert.Equal(
            4,
            icons.Distinct().Count());
    }

    [Fact]
    public void WorldMarkersUseDistinctGeometryForSelectionHoverAndInvalidTargets()
    {
        var draw =
            new DebugDraw
            {
                Enabled = true
            };
        RenderInstance unit =
            Unit(
                new EntityId(
                    11,
                    1),
                Vector3.Zero,
                LocalPlayer,
                UnitIds.MainBattleTank);
        RenderInstance building =
            Building(
                new EntityId(
                    12,
                    1),
                new Vector3(
                    20.0f,
                    0.0f,
                    20.0f),
                LocalPlayer,
                BuildingIds.CommandCore);
        Vector4 selectedColor =
            new(
                0.1f,
                0.8f,
                1.0f,
                1.0f);
        Vector4 invalidColor =
            new(
                1.0f,
                0.2f,
                0.1f,
                1.0f);

        RtsWorldMarkerVisualization.DrawSelected(
            draw,
            unit,
            selectedColor);
        int unitSelectionLines =
            draw.Lines.Length;

        draw.Clear();
        RtsWorldMarkerVisualization.DrawSelected(
            draw,
            building,
            selectedColor);
        int buildingSelectionLines =
            draw.Lines.Length;

        draw.Clear();
        RtsWorldMarkerVisualization.DrawHover(
            draw,
            unit,
            selectedColor);
        int hoverLines =
            draw.Lines.Length;

        draw.Clear();
        RtsWorldMarkerVisualization.DrawTarget(
            draw,
            Vector3.Zero,
            valid: false,
            selectedColor,
            invalidColor);
        DebugLine[] invalidTargetLines =
            draw.Lines.ToArray();

        Assert.True(
            unitSelectionLines >
            buildingSelectionLines);
        Assert.Equal(
            4,
            buildingSelectionLines);
        Assert.Equal(
            4,
            hoverLines);
        Assert.Equal(
            2,
            invalidTargetLines.Length);
        Assert.All(
            invalidTargetLines,
            line =>
                Assert.Equal(
                    invalidColor,
                    line.Color));
    }

    [Fact]
    public void OverlayControllerCyclesOnlyPresentationModes()
    {
        var controller =
            new RtsInformationLayerController();

        Assert.True(
            controller.MinimapEnabled);
        Assert.Equal(
            StrategicOverlayMode.None,
            controller.OverlayMode);

        controller.CycleOverlay();
        Assert.Equal(
            StrategicOverlayMode.Logistics,
            controller.OverlayMode);
        controller.CycleOverlay();
        Assert.Equal(
            StrategicOverlayMode.Supply,
            controller.OverlayMode);
        controller.CycleOverlay();
        Assert.Equal(
            StrategicOverlayMode.Sensors,
            controller.OverlayMode);
        controller.CycleOverlay();
        Assert.Equal(
            StrategicOverlayMode.Navigation,
            controller.OverlayMode);
        controller.CycleOverlay();
        Assert.Equal(
            StrategicOverlayMode.All,
            controller.OverlayMode);
        controller.CycleOverlay();
        Assert.Equal(
            StrategicOverlayMode.None,
            controller.OverlayMode);

        controller.ToggleMinimap();
        Assert.False(
            controller.MinimapEnabled);
    }

    private static RenderInstance Unit(
        EntityId entity,
        Vector3 position,
        PlayerId owner,
        UnitId unit) =>
        new(
            entity,
            new RenderTransform(
                position,
                Quaternion.Identity,
                new Vector3(
                    8.0f)),
            new RenderMeshHandle(
                1),
            RenderMaterialHandle.Default,
            RenderVisibilityMask.World,
            entity.Index,
            new SelectablePresentationMetadata(
                owner,
                ControllableEntityCategory.Unit),
            UnitFeature:
                new UnitFeaturePresentationMetadata(
                    unit,
                    UnitPresentationDamageState.Intact));

    private static RenderInstance Building(
        EntityId entity,
        Vector3 position,
        PlayerId owner,
        BuildingId building) =>
        new(
            entity,
            new RenderTransform(
                position,
                Quaternion.Identity,
                new Vector3(
                    16.0f)),
            new RenderMeshHandle(
                1),
            RenderMaterialHandle.Default,
            RenderVisibilityMask.World,
            entity.Index,
            new SelectablePresentationMetadata(
                owner,
                ControllableEntityCategory.Building),
            BuildingFeature:
                new BuildingFeaturePresentationMetadata(
                    building,
                    BuildingPresentationState.Operational));

    private static RenderInstance Resource(
        EntityId entity,
        Vector3 position) =>
        new(
            entity,
            new RenderTransform(
                position,
                Quaternion.Identity,
                new Vector3(
                    10.0f)),
            new RenderMeshHandle(
                1),
            RenderMaterialHandle.Default,
            RenderVisibilityMask.World,
            entity.Index,
            WorldFeature:
                new WorldFeaturePresentationMetadata(
                    WorldVisualId.ResourceFerrousOre,
                    WorldPresentationKind.ResourceDeposit,
                    ResourceDepositPresentationState.Untouched,
                    Inspectable: true));
}
