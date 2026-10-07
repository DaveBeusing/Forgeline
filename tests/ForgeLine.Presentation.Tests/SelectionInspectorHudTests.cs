using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectionInspectorHudTests : IDisposable
{
    private readonly FakeGraphicsDevice _graphics =
        new();

    public void Dispose()
    {
        _graphics.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ModelResolvesSemanticRoleIcons()
    {
        PlayerSelectionSummary unit =
            CreateSelection(
                PlayerSelectionKind.Unit,
                commonUnitId:
                    UnitIds.MainBattleTank);
        PlayerSelectionSummary building =
            CreateSelection(
                PlayerSelectionKind.Building,
                commonBuildingId:
                    BuildingIds.PowerPlant);
        PlayerSelectionSummary construction =
            CreateSelection(
                PlayerSelectionKind.Construction,
                commonBuildingId:
                    BuildingIds.VehicleFactory);
        PlayerSelectionSummary mixed =
            CreateSelection(
                PlayerSelectionKind.Mixed,
                count: 3);

        Assert.Equal(
            RtsUiIcon.UnitArmor,
            SelectionInspectorHudModel.ResolveRoleIcon(
                unit));
        Assert.Equal(
            RtsUiIcon.BuildingPower,
            SelectionInspectorHudModel.ResolveRoleIcon(
                building));
        Assert.Equal(
            RtsUiIcon.BuildingFactory,
            SelectionInspectorHudModel.ResolveRoleIcon(
                construction));
        Assert.Equal(
            RtsUiIcon.MinimapSelectedGroup,
            SelectionInspectorHudModel.ResolveRoleIcon(
                mixed));
    }

    [Fact]
    public void ModelUsesExplicitOperationalStatusLabels()
    {
        Assert.Equal(
            "SUPPLIED",
            SelectionInspectorHudModel.ResolveSupplyLabel(
                BattlefieldSupplyStatus.Supplied));
        Assert.Equal(
            "CRITICAL",
            SelectionInspectorHudModel.ResolveSupplyLabel(
                BattlefieldSupplyStatus.Critical));
        Assert.Equal(
            "UNSUPPLIED",
            SelectionInspectorHudModel.ResolveSupplyLabel(
                BattlefieldSupplyStatus.Unsupplied));
        Assert.Equal(
            "POWERED",
            SelectionInspectorHudModel.ResolvePowerLabel(
                PowerOperationalState.Powered));
        Assert.Equal(
            "BROWNOUT",
            SelectionInspectorHudModel.ResolvePowerLabel(
                PowerOperationalState.Brownout));
        Assert.Equal(
            "BLOCKED",
            SelectionInspectorHudModel.ResolveWorkStateLabel(
                PlayerWorkState.Blocked));
    }

    [Fact]
    public void SurfaceOwnsOnlySelectionInspectorRegion()
    {
        using var surface =
            new SelectionInspectorHudSurface(
                _graphics,
                runtimeAssets: null);

        Assert.Equal(
            GameplayHudRegion.SelectionInspector,
            surface.Regions);
    }

    [Fact]
    public void RendererDoesNotRetainClearedSelection()
    {
        using var renderer =
            new SelectionInspectorHudRenderer(
                _graphics,
                runtimeAssets: null);
        var context =
            new FakeGraphicsCommandContext();
        GameplayHudLayout layout =
            GameplayHudLayout.Create(
                context.Width,
                context.Height,
                96);

        renderer.Render(
            context,
            Snapshot(
                CreateSelection(
                    PlayerSelectionKind.Unit,
                    commonUnitId:
                        UnitIds.MainBattleTank)),
            layout);

        Assert.True(
            renderer.LastRenderedVertexCount >
            0);

        renderer.Render(
            context,
            Snapshot(
                PlayerSelectionSummary.Empty),
            layout);

        Assert.Equal(
            0,
            renderer.LastRenderedVertexCount);
    }

    [Fact]
    public void MultiSelectionRendersWithoutSingleEntityDetails()
    {
        PlayerSelectionSummary selection =
            CreateSelection(
                PlayerSelectionKind.Unit,
                count: 4,
                displayName: "Units");

        Assert.False(
            selection.HasSingleEntityDetails);
        Assert.False(
            selection.HasCommonIdentity);
        Assert.Equal(
            RtsUiIcon.MinimapSelectedGroup,
            SelectionInspectorHudModel.ResolveRoleIcon(
                selection));
    }

    [Fact]
    public void WorkKindsUseExistingSemanticIcons()
    {
        Assert.Equal(
            RtsUiIcon.CommandBuild,
            SelectionInspectorHudModel.ResolveWorkIcon(
                PlayerWorkKind.Construction));
        Assert.Equal(
            RtsUiIcon.BuildingFactory,
            SelectionInspectorHudModel.ResolveWorkIcon(
                PlayerWorkKind.UnitProduction));
        Assert.Equal(
            RtsUiIcon.BuildingProcessing,
            SelectionInspectorHudModel.ResolveWorkIcon(
                PlayerWorkKind.Processing));
    }

    private static PlayerSelectionSummary CreateSelection(
        PlayerSelectionKind kind,
        int count = 1,
        string displayName = "Selection",
        UnitId commonUnitId = default,
        BuildingId commonBuildingId = default) =>
        new(
            count,
            new EntityId(
                1,
                1),
            kind,
            displayName,
            HasHealth: count == 1,
            HealthFraction: 0.8,
            HasSupply: count == 1 &&
                kind == PlayerSelectionKind.Unit,
            SupplyStatus:
                BattlefieldSupplyStatus.Supplied,
            FuelFraction: 0.7,
            AmmunitionFraction: 0.6,
            HasReadiness: count == 1 &&
                kind == PlayerSelectionKind.Unit,
            Readiness: 0.75,
            HasPower: count == 1 &&
                kind is
                    PlayerSelectionKind.Building or
                    PlayerSelectionKind.Construction,
            PowerState:
                PowerOperationalState.Powered,
            HasInventory: false,
            InventoryQuantity: 0.0,
            Work:
                PlayerWorkSummary.None,
            commonUnitId,
            commonBuildingId);

    private static PresentationSnapshot Snapshot(
        PlayerSelectionSummary selection)
    {
        var experience =
            new PlayerExperienceSnapshot(
                new PlayerId(1),
                new SimulationTick(1),
                PlayerMatchStatus.Active,
                default,
                default,
                default,
                default,
                selection,
                PlayerAlertState.None,
                CriticalSupplyUnits: 0,
                BlockedProductionFacilities: 0,
                PlayerCommandFeedback.None,
                default);

        return new PresentationSnapshot(
            new SimulationTick(1),
            TimeSpan.FromMilliseconds(50),
            0,
            [],
            playerExperience:
                experience);
    }

    private sealed class FakeGraphicsDevice :
        IGraphicsDevice
    {
        public GraphicsDiagnostics Diagnostics =>
            throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(
            GraphicsPipelineDescription description) =>
            new FakeGraphicsPipeline(
                description);

        public IGraphicsBuffer CreateBuffer(
            GraphicsBufferDescription description) =>
            new FakeGraphicsBuffer(
                description);

        public void RenderFrame(
            GraphicsColor clearColor,
            Action<IGraphicsCommandContext>? recordCommands = null)
        {
            recordCommands?.Invoke(
                new FakeGraphicsCommandContext());
        }

        public void Resize(
            int width,
            int height)
        {
        }

        public void WaitForIdle()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsPipeline :
        IGraphicsPipeline
    {
        public FakeGraphicsPipeline(
            GraphicsPipelineDescription description)
        {
            Description =
                description;
        }

        public GraphicsPipelineDescription Description { get; }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsBuffer :
        IGraphicsBuffer
    {
        public FakeGraphicsBuffer(
            GraphicsBufferDescription description)
        {
            Description =
                description;
        }

        public GraphicsBufferDescription Description { get; }

        public void SetData<T>(
            ReadOnlySpan<T> data,
            int offsetInBytes = 0)
            where T : unmanaged
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeGraphicsCommandContext :
        IGraphicsCommandContext
    {
        public int Width => 1600;

        public int Height => 900;

        public int FrameIndex => 0;

        public void SetViewport(
            float x,
            float y,
            float width,
            float height)
        {
        }

        public void SetScissor(
            int left,
            int top,
            int right,
            int bottom)
        {
        }

        public void SetPipeline(
            IGraphicsPipeline pipeline)
        {
        }

        public void SetVertexBuffer(
            IGraphicsBuffer buffer,
            int strideInBytes,
            int offsetInBytes = 0,
            int inputSlot = 0)
        {
        }

        public void SetIndexBuffer(
            IGraphicsBuffer buffer,
            GraphicsIndexFormat format,
            int offsetInBytes = 0)
        {
        }

        public void SetVertexConstants(
            ReadOnlySpan<float> values)
        {
        }

        public void Draw(
            int vertexCount,
            int startVertex = 0)
        {
        }

        public void DrawIndexed(
            int indexCount,
            int startIndex = 0,
            int baseVertex = 0)
        {
        }

        public void DrawIndexedInstanced(
            int indexCount,
            int instanceCount,
            int startIndex = 0,
            int baseVertex = 0,
            int startInstance = 0)
        {
        }
    }
}
