using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Logistics;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using ForgeLine.Platform.Windows;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

[Collection("Native scene output")]
public sealed class EconomicInterfaceSkinTests
{
    [Theory]
    [InlineData(ProductionBlockReason.NoInput, "NO INPUT")]
    [InlineData(ProductionBlockReason.NoPower, "NO POWER")]
    [InlineData(ProductionBlockReason.OutputFull, "OUTPUT FULL")]
    [InlineData(ProductionBlockReason.InvalidInventory, "INVENTORY")]
    [InlineData(ProductionBlockReason.DesiredStockReached, "TARGET REACHED")]
    public void ActiveProcessingBlockRemainsVisibleWhileQueueControlsStayAvailable(ProductionBlockReason reason, string label)
    {
        var actions = Processing(reason);
        var visual = EconomicCardVisual.Resolve(PlayerActionPanelMode.Production, 0, actions, false);
        Assert.Equal(UiSkinState.Warning, visual.State);
        Assert.Equal(label, visual.Label);
        Assert.Equal(.42, visual.Progress);
        Assert.True(PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Production, 0, actions).CanActivate);
        Assert.True(PlayerActionDockHudModel.CanUseControl(PlayerActionDockControlKind.Cancel, PlayerActionPanelMode.Production, 0, default, actions));
        Assert.Equal(UiSkinState.Pending, EconomicCardVisual.Resolve(PlayerActionPanelMode.Production, 0, actions, true).State);
    }
    [Theory]
    [InlineData(UnitProductionBlockReason.NoInput, "NO INPUT")]
    [InlineData(UnitProductionBlockReason.NoPower, "NO POWER")]
    [InlineData(UnitProductionBlockReason.InvalidFacility, "INVALID FACILITY")]
    public void UnitQueueRetainsWorkIdentityAndCancelOnlyContract(UnitProductionBlockReason reason, string label)
    {
        var actions = Units(reason);
        var active = EconomicCardVisual.Resolve(PlayerActionPanelMode.UnitProduction, 1, actions, false);
        Assert.Equal(UiSkinState.Warning, active.State); Assert.Equal(label, active.Label);
        Assert.Equal(.63, active.Progress);
        Assert.Equal("CANCEL ONLY", PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.UnitProduction, 1, actions).DisabledReason);
        Assert.True(PlayerActionDockHudModel.CanUseControl(PlayerActionDockControlKind.Cancel, PlayerActionPanelMode.UnitProduction, 1, default, actions));
        var waiting = EconomicCardVisual.Resolve(PlayerActionPanelMode.UnitProduction, 2, actions, false);
        Assert.Equal("QUEUED", waiting.Label); Assert.Null(waiting.Progress);
        Assert.Equal((PlayerSelectionKind.Unit, UnitIds.MainBattleTank.Value), EconomicCardVisual.Portrait(PlayerActionPanelMode.UnitProduction, 1, actions));
        Assert.Equal((PlayerSelectionKind.Unit, UnitIds.CombatEngineer.Value), EconomicCardVisual.Portrait(PlayerActionPanelMode.UnitProduction, 2, actions));
        Assert.Equal(default, EconomicCardVisual.Portrait(PlayerActionPanelMode.UnitProduction, 3, actions));
    }
    [Fact]
    public void RequestAcceptanceDoesNotCompleteWorkAndChangingContextDoesNotRetainIt()
    {
        var running = EconomicCardVisual.Resolve(PlayerActionPanelMode.Production, 0, Processing(ProductionBlockReason.None), false);
        Assert.Equal("STARTED", running.Label); Assert.NotEqual(UiSkinState.Accepted, running.State);
        Assert.Equal("QUEUED / PAUSED", EconomicCardVisual.Resolve(PlayerActionPanelMode.Production, 0, Processing(ProductionBlockReason.None, paused: true), false).Label);
        Assert.Equal("QUEUED", EconomicCardVisual.Resolve(PlayerActionPanelMode.Production, 0, Processing(ProductionBlockReason.None, active: false), false).Label);
        Assert.Equal("NO DATA", EconomicCardVisual.Resolve(PlayerActionPanelMode.Production, 0, null, false).Label);
        Assert.Null(EconomicCardVisual.Resolve(PlayerActionPanelMode.UnitProduction, 2, Units(UnitProductionBlockReason.None), false).Progress);
    }
    [Theory]
    [InlineData(PlayerTechnologyState.Available, UiSkinState.Normal, "AVAILABLE")]
    [InlineData(PlayerTechnologyState.Researching, UiSkinState.Selected, "RESEARCHING")]
    [InlineData(PlayerTechnologyState.Completed, UiSkinState.Accepted, "COMPLETED")]
    [InlineData(PlayerTechnologyState.Blocked, UiSkinState.Warning, "PREREQ")]
    public void OnlyAuthoritativeTechnologyCompletionGetsCompletedTreatment(PlayerTechnologyState state, UiSkinState skin, string label)
    {
        var actions = Technology(state);
        var visual = EconomicCardVisual.Resolve(PlayerActionPanelMode.Technology, 0, actions, false);
        Assert.Equal(skin, visual.State); Assert.Contains(label, visual.Label);
        Assert.Equal(RtsUiIcon.UnitArmor, EconomicCardVisual.Icon(PlayerActionPanelMode.Technology, 0, actions));
        var panel = default(PlayerActionPanelView) with { Mode = PlayerActionPanelMode.Technology };
        var snapshot = Snapshot(actions: actions);
        var hover = new HoverTooltipView(new(1), default, default, new(600,300), true, 1280,720,1,
            PlayerActionPanelMode.Technology, (int)PlayerActionDockControlKind.Item, 0);
        var content = Assert.IsType<HoverTooltipContent>(HoverTooltipResolver.Resolve(snapshot, hover, panel));
        Assert.Equal(skin, content.VisualState);
        Assert.Equal(actions.Technology[0].Costs, content.Costs);
        Assert.Equal(actions.Technology[0].Prerequisites, content.Prerequisites);
        Assert.Null(HoverTooltipResolver.Resolve(Snapshot(actions: actions, tick: 2), hover, panel));
    }
    [Fact]
    public void MissingRoutesAndEmptyProvidersRemainWarningsWithExistingPolicyControls()
    {
        var stock = new PlayerStockPolicyActionReadModel(new(30,1),ResourceIds.Steel,"Steel",0,20,100,180,LogisticsStockPriority.Normal,true,
            PlayerDistributionActionState.Waiting,LogisticsTransportRequestFailureReason.NoRoute,LogisticsBottleneckReason.None,default);
        var actions = new PlayerActionSnapshot(new(1),new(1),[],0,null,null,new(new(10,1),[stock],null),
            new(new(11,1),BattlefieldSupplyStatus.Unsupplied,0,0,true,.4,.5,default,PlayerSupplyProviderState.Empty,0,0,default));
        Assert.Equal(UiSkinState.Warning,EconomicCardVisual.Resolve(PlayerActionPanelMode.Logistics,0,actions,false).State);
        Assert.True(PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Logistics,0,actions).CanActivate);
        Assert.Equal("FUEL EMPTY",EconomicCardVisual.Resolve(PlayerActionPanelMode.Supply,0,actions,false).Label);
        Assert.Equal("AMMUNITION EMPTY",EconomicCardVisual.Resolve(PlayerActionPanelMode.Supply,1,actions,false).Label);
        Assert.Equal(UiSkinState.Warning,EconomicCardVisual.Resolve(PlayerActionPanelMode.Supply,2,actions,false).State);
        Assert.True(PlayerActionDockHudModel.ResolveItemState(PlayerActionPanelMode.Supply,2,actions).CanActivate);
    }
    [Fact]
    public void TechnologyArtIsBoundToStableIdentityInsteadOfFutureDomainGuessing()
    {
        foreach (var (id, icon) in new[] { (1u,RtsUiIcon.BuildingProcessing),(2u,RtsUiIcon.UnitLogistics),
            (3u,RtsUiIcon.UnitArmor),(4u,RtsUiIcon.UnitReconnaissance),(999u,RtsUiIcon.StatusAlert) })
            Assert.Equal(icon,EconomicCardVisual.Icon(PlayerActionPanelMode.Technology,0,Technology(PlayerTechnologyState.Available,id)));
    }
    [Fact]
    public void CopiedEconomicStateResolutionHasNoWarmAllocations()
    {
        var actions = Units(UnitProductionBlockReason.NoPower);
        for (int i = 0; i < 128; i++) Resolve();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) Resolve();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        void Resolve()
        {
            for (int index = 0; index < 3; index++)
            { _ = EconomicCardVisual.Resolve(PlayerActionPanelMode.UnitProduction, index, actions, false); _ = EconomicCardVisual.Portrait(PlayerActionPanelMode.UnitProduction, index, actions); }
        }
    }
    [Fact]
    public void NativeOperationsRejectsStaleFactsAndSelectionNeverCachesRetiredFacilityData()
    {
        using var platform = new WindowsPlatform(); using IWindow window = platform.CreateWindow(new("FORGELINE operations skin", 1280,720));
        using var device = GraphicsDeviceFactory.CreateForWindow(window, new() { ForceSoftwareAdapter = true, EnableVSync = false });
        var catalog = RuntimeAssetCatalog.Load(Path.Combine(Root(), "assets/runtime"));
        using (var surface = new OperationsHudSurface(device, catalog))
        {
            var entity = new EntityId(10,1);
            var row = new OperationsFacility(entity, "Owned factory", OperationsCategory.Production, "NoPower", "NO POWER", "Current allocation is below required power.",
                2, null, null, 100, 20, null, null, PlayerActionPanelMode.UnitProduction);
            var data = new OperationsSnapshot(new(1), new(1), new(1), [row], [], [], 1,0);
            var view = new OperationsView(true, Session: new(1), Selected: entity);
            byte[] Capture(PresentationSnapshot snapshot, OperationsView state) => device.CaptureFrame(GraphicsColor.ForgeLineClear, c =>
            {
                var layout = GameplayHudLayout.Create(c.Width,c.Height,96,1);
                surface.Render(new(c,new RtsCamera(),snapshot,new AxisAlignedBounds(new(-100),new(100)),default,default,default,
                    FormationTemplate.Compact,CombatGroupOverviewView.Empty,default,layout,96,1,null,Operations: state));
            });
            var selected = Capture(Snapshot(operations: data), view);
            var absent = Capture(Snapshot(operations: data), view with { Selected = new(999,2) });
            Assert.False(selected.AsSpan().SequenceEqual(absent));
            var waiting = Capture(Snapshot(), view);
            var stale = Capture(Snapshot(operations: new(new(2),new(1),new(1),[row],[],[],1,0)), view);
            Assert.True(waiting.AsSpan().SequenceEqual(stale));
            long uploads = device.Diagnostics.Resources.TextureUploadCount;
            Capture(Snapshot(operations: data), view);
            Assert.Equal(uploads, device.Diagnostics.Resources.TextureUploadCount);
            Assert.Equal(0, surface.SkinDiagnostics.MissingAssets); Assert.Equal(0, surface.SkinDiagnostics.DroppedQuads);
            Assert.Equal(0, surface.TextDiagnostics.DroppedGlyphs);
        }
        device.WaitForIdle(); Assert.Equal(0, device.Diagnostics.Resources.LoadedTextureCount);
        Assert.Equal(0, device.Diagnostics.Health.SubmissionFaultCount);
    }
    private static PlayerActionSnapshot Processing(ProductionBlockReason reason, bool paused = false, bool active = true) => new(new(1),new(1),[],0,
        new(new(10,1),new(20,1),RecipeIds.Steel,ProductionStatus.Running,reason,.42,[],
            [new(new(20,1),RecipeIds.Steel,"Steel",ProductionPriority.Normal,ProductionRequestMode.Repeat,paused,active)]),null);
    private static PlayerActionSnapshot Units(UnitProductionBlockReason reason) => new(new(1),new(1),[],0,null,
        new(new(10,1),new(20,1),UnitIds.MainBattleTank,UnitProductionStatus.Running,reason,.63,
            [new(UnitIds.MainBattleTank,"Tank",[],80)],
            [new(new(20,1),UnitIds.MainBattleTank,"Tank",ProductionPriority.Normal,false,true),
             new(new(21,1),UnitIds.CombatEngineer,"Engineer",ProductionPriority.Normal,false,false)]));
    private static PlayerActionSnapshot Technology(PlayerTechnologyState state, uint identity = 3) => new(new(1),new(1),[],0,null,null,technology:
        [new(new(identity),"mechanized_systems","Mechanized Systems",TechnologyDomain.Warfare,TechnologyPhase.MechanizedWarfare,180,
            [new(ResourceIds.Steel,"Steel",80,12)],[new(TechnologyIds.IndustrialStandardization,"Industrial Standardization",false)],
            BuildingIds.VehicleFactory,"Vehicle Factory",new(10,1),new(40,1),1,.4,state,
            state == PlayerTechnologyState.Blocked ? TechnologyResearchBlockReason.UnmetPrerequisite : TechnologyResearchBlockReason.None,.35,new(20,1),[])]);
    private static PresentationSnapshot Snapshot(PlayerActionSnapshot? actions = null, OperationsSnapshot? operations = null, ulong tick = 1) =>
        new(new(tick),TimeSpan.FromMilliseconds(50),0,[],sessionId:new(1),
            playerExperience: default(PlayerExperienceSnapshot) with { Tick = new(tick), Player = new(1) }, playerActions: actions, operations: operations);
    private static string Root()
    { for (var p = new DirectoryInfo(AppContext.BaseDirectory); p is not null; p = p.Parent) if (File.Exists(Path.Combine(p.FullName,"ForgeLine.sln"))) return p.FullName; throw new DirectoryNotFoundException(); }
}
