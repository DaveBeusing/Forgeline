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
    public void SelectedQueueProgressRemainsVisibleAboveTheFocusBorder()
    {
        using var platform = new WindowsPlatform(); using IWindow window = platform.CreateWindow(new("FORGELINE selected queue",1280,720));
        using var device = GraphicsDeviceFactory.CreateForWindow(window,new() { ForceSoftwareAdapter = true,EnableVSync = false });
        using var renderer = new PlayerActionDockHudRenderer(device,RuntimeAssetCatalog.Load(Path.Combine(Root(),"assets/runtime")));
        HudRect card = default; int width = 0; float scale = 1;
        byte[] Capture(double progress) => device.CaptureFrame(GraphicsColor.ForgeLineClear,c =>
        {
            var layout = GameplayHudLayout.Create(c.Width,c.Height,96,1); width = c.Width;
            scale = PlayerActionDockInteractionLayout.Scale(layout);
            Assert.False(PlayerActionDockInteractionLayout.Compact(layout));
            card = PlayerActionDockInteractionLayout.GetCardRect(layout,0);
            renderer.Render(c,Snapshot(actions:Processing(ProductionBlockReason.None,progress:progress)),
                default(PlayerActionPanelView) with { Mode = PlayerActionPanelMode.Production,SelectedIndex = 0 },default,FormationTemplate.Compact,layout);
        });
        var empty = Capture(0); var full = Capture(1);
        int changes = 0;
        // The progress lane sits above the full-width selected border and below card details.
        for (int y = (int)(card.Bottom - 10 * scale); y < (int)(card.Bottom - 4 * scale); y++)
        for (int x = (int)(card.X + 8 * scale); x < (int)(card.Right - 8 * scale); x++)
        { int pixel = (y * width + x) * 4; if (!empty.AsSpan(pixel,4).SequenceEqual(full.AsSpan(pixel,4))) changes++; }
        Assert.True(changes > 0,"Selected border obscured the production progress fill.");
    }
    [Fact]
    public void NativeThinMetersRetainVisibleClippedFillAndUnknownNeverLooksCompleted()
    {
        using var platform = new WindowsPlatform(); using IWindow window = platform.CreateWindow(new("FORGELINE queue meters",1280,720));
        using var device = GraphicsDeviceFactory.CreateForWindow(window,new() { ForceSoftwareAdapter = true,EnableVSync = false });
        using var skin = new GameplayHudSkin(device,RuntimeAssetCatalog.Load(Path.Combine(Root(),"assets/runtime")));
        foreach (float height in new[] { 2f,4f,6f })
        {
            byte[] Capture(double fraction) => device.CaptureFrame(GraphicsColor.ForgeLineClear,c =>
            { skin.Begin(c); skin.Bar(new(20,20,200,height),fraction,Vector4.One); skin.Flush(c); });
            var empty = Capture(0); var half = Capture(.5); var full = Capture(1); var unknown = Capture(double.NaN);
            Assert.True(empty.AsSpan().SequenceEqual(unknown));
            int halfChanges = Differences(empty,half), fullChanges = Differences(empty,full);
            Assert.True(halfChanges > 0,$"{height}px meter lost its fill");
            Assert.True(fullChanges > halfChanges,$"{height}px meter did not clip progress");
            Assert.Equal(0,skin.Diagnostics.DroppedQuads);
        }
        static int Differences(byte[] left, byte[] right)
        { int count = 0; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) count++; return count; }
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
            int width = 0,height = 0;
            byte[] Capture(PresentationSnapshot snapshot, OperationsView state, GraphicsColor? background = null) => device.CaptureFrame(background ?? GraphicsColor.ForgeLineClear, c =>
            {
                width = c.Width; height = c.Height;
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
            var blue = Capture(Snapshot(operations:data),view,new(0,0,1,1));
            var red = Capture(Snapshot(operations:data),view,new(1,0,0,1));
            Assert.False(blue.AsSpan().SequenceEqual(red));
            var panel = OperationsLayout.Panel(GameplayHudLayout.Create(width,height,96,1));
            int pixel = ((int)(panel.Y + panel.Height * .7f) * width + (int)(panel.X + panel.Width * .5f)) * 4;
            Assert.True(blue.AsSpan(pixel,4).SequenceEqual(red.AsSpan(pixel,4))); // Existing opaque ownership, underneath transparent artwork.
            long uploads = device.Diagnostics.Resources.TextureUploadCount;
            Capture(Snapshot(operations: data), view);
            Assert.Equal(uploads, device.Diagnostics.Resources.TextureUploadCount);
            Assert.Equal(0, surface.SkinDiagnostics.MissingAssets); Assert.Equal(0, surface.SkinDiagnostics.DroppedQuads);
            Assert.Equal(0, surface.TextDiagnostics.DroppedGlyphs);
        }
        device.WaitForIdle(); Assert.Equal(0, device.Diagnostics.Resources.LoadedTextureCount);
        Assert.Equal(0, device.Diagnostics.Health.SubmissionFaultCount);
    }
    private static PlayerActionSnapshot Processing(ProductionBlockReason reason, bool paused = false, bool active = true, double progress = .42) => new(new(1),new(1),[],0,
        new(new(10,1),new(20,1),RecipeIds.Steel,ProductionStatus.Running,reason,progress,[],
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
