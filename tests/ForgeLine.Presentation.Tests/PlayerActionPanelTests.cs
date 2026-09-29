using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PlayerActionPanelTests
{
    [Fact]
    public void ConstructionPaletteNavigatesByKeyboardAndCreatesPlacementRequest()
    {
        PresentationSnapshot snapshot =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant"),
                    Construction(
                        BuildingIds.VehicleFactory,
                        "Vehicle Factory")
                ]);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(
            input,
            PlatformKey.B);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Construction,
            controller.Mode);

        Release(
            input,
            PlatformKey.B);
        Press(
            input,
            PlatformKey.Tab);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            1,
            controller.SelectedIndex);

        Release(
            input,
            PlatformKey.Tab);
        Press(
            input,
            PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.BeginBuildingPlacement,
            request.Kind);
        Assert.Equal(
            BuildingIds.VehicleFactory,
            request.BuildingId);
        Assert.Equal(
            PlayerActionPanelMode.Closed,
            controller.Mode);
    }

    [Fact]
    public void ProductionPaletteExposesModePriorityDesiredStockPauseAndCancel()
    {
        EntityId facility =
            new(50, 1);
        EntityId queuedRequest =
            new(51, 1);
        var recipe =
            new PlayerProductionRecipeActionReadModel(
                RecipeIds.Steel,
                "Steel",
                [
                    new PlayerActionResourceAmount(
                        ResourceIds.FerrousOre,
                        "Ferrous Ore",
                        10.0,
                        100.0)
                ],
                [
                    new PlayerActionResourceAmount(
                        ResourceIds.Steel,
                        "Steel",
                        10.0,
                        20.0)
                ]);
        var production =
            new PlayerProductionFacilityActionReadModel(
                facility,
                queuedRequest,
                RecipeIds.Steel,
                ProductionStatus.Running,
                ProductionBlockReason.None,
                0.25,
                [recipe],
                [
                    new PlayerProductionRequestReadModel(
                        queuedRequest,
                        RecipeIds.Steel,
                        "Steel",
                        ProductionPriority.Normal,
                        ProductionRequestMode.Repeat,
                        false,
                        true)
                ]);
        PresentationSnapshot snapshot =
            CreateSnapshot(
                production: production);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.P);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.P);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Press(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.M);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            ProductionRequestMode.DesiredStock,
            controller.ProductionMode);

        Press(input, PlatformKey.T);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.T);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            ProductionPriority.High,
            controller.Priority);

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest queue));
        Assert.Equal(
            PlayerActionRequestKind.QueueProduction,
            queue.Kind);
        Assert.Equal(
            facility,
            queue.Facility);
        Assert.Equal(
            RecipeIds.Steel,
            queue.RecipeId);
        Assert.Equal(
            ProductionPriority.High,
            queue.Priority);
        Assert.Equal(
            ProductionRequestMode.DesiredStock,
            queue.ProductionMode);
        Assert.Equal(
            ResourceIds.Steel,
            queue.DesiredStockResourceId);
        Assert.True(
            queue.DesiredStockQuantity >
            20.0);

        Press(input, PlatformKey.Tab);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Tab);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            1,
            controller.SelectedIndex);

        Press(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.Enter);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest pause));
        Assert.Equal(
            PlayerActionRequestKind.SetProductionPaused,
            pause.Kind);
        Assert.Equal(
            queuedRequest,
            pause.RequestEntity);
        Assert.True(pause.Paused);

        Press(input, PlatformKey.C);
        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Release(input, PlatformKey.C);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest cancel));
        Assert.Equal(
            PlayerActionRequestKind.CancelProduction,
            cancel.Kind);
        Assert.Equal(
            queuedRequest,
            cancel.RequestEntity);
    }

    [Fact]
    public void PointerActivationIsCapturedAndFocusLossDoesNotRepeatModeToggle()
    {
        PresentationSnapshot snapshot =
            CreateSnapshot(
                construction:
                [
                    Construction(
                        BuildingIds.PowerPlant,
                        "Power Plant")
                ]);
        var input = new InputState();
        var controller =
            new PlayerActionPanelController();

        controller.Update(
            input,
            snapshot,
            1600,
            900);
        Press(input, PlatformKey.B);
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        input.Apply(
            PlatformInputEvent.FocusLost());
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.Equal(
            PlayerActionPanelMode.Construction,
            controller.Mode);

        input.Apply(
            PlatformInputEvent.PointerMoved(
                1_000,
                168));
        input.Apply(
            PlatformInputEvent.MouseButtonChanged(
                PlatformInputEventKind.MouseButtonDown,
                PlatformMouseButton.Left,
                1_000,
                168));
        controller.Update(
            input,
            snapshot,
            1600,
            900);

        Assert.True(
            controller.PointerCaptured);
        Assert.True(
            controller.TryTakeRequest(
                out PlayerActionRequest request));
        Assert.Equal(
            PlayerActionRequestKind.BeginBuildingPlacement,
            request.Kind);
        Assert.Equal(
            BuildingIds.PowerPlant,
            request.BuildingId);
    }

    private static PlayerConstructionActionReadModel Construction(
        BuildingId buildingId,
        string name) =>
        new(
            buildingId,
            name,
            [
                new PlayerActionResourceAmount(
                    ResourceIds.FerrousOre,
                    "Ferrous Ore",
                    10.0,
                    100.0)
            ],
            false);

    private static PresentationSnapshot CreateSnapshot(
        IReadOnlyList<PlayerConstructionActionReadModel>? construction = null,
        PlayerProductionFacilityActionReadModel? production = null,
        PlayerUnitProductionFacilityActionReadModel? unitProduction = null)
    {
        var session =
            new SimulationSessionId(101);
        var actions =
            new PlayerActionSnapshot(
                session,
                new SimulationTick(4),
                construction ?? [],
                0,
                production,
                unitProduction);

        return new PresentationSnapshot(
            new SimulationTick(4),
            TimeSpan.FromMilliseconds(50),
            0,
            ReadOnlySpan<RenderInstance>.Empty,
            sessionId: session,
            playerActions: actions);
    }

    private static void Press(
        InputState input,
        PlatformKey key) =>
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyDown,
                key));

    private static void Release(
        InputState input,
        PlatformKey key) =>
        input.Apply(
            PlatformInputEvent.KeyChanged(
                PlatformInputEventKind.KeyUp,
                key));
}
