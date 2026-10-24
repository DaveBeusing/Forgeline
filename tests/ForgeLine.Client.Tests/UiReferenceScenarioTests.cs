using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.UI;
using Xunit;

namespace ForgeLine.Client.Tests;

public sealed class UiReferenceScenarioTests
{
    [Theory]
    [InlineData(nameof(UiReferenceScenario.OperationsRuleDraft))]
    [InlineData(nameof(UiReferenceScenario.OperationsRulePending))]
    [InlineData(nameof(UiReferenceScenario.OperationsRuleRejected))]
    [InlineData(nameof(UiReferenceScenario.OperationsRuleManagedPause))]
    [InlineData(nameof(UiReferenceScenario.OperationsRuleManualOverride))]
    public void RuleReferenceStatesExposeExactOwnedDraftAndStatus(string stateName)
    {
        UiReferenceScenario state = Enum.Parse<UiReferenceScenario>(stateName);
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var original = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain).Snapshot;
        var (snapshot, panel) = UiReferenceStates.Hud(original, state, 1280, 720, 192, 2);
        Assert.False(panel.IsOpen);
        Assert.True(original.Instances.SequenceEqual(snapshot.Instances));
        var view = UiReferenceStates.RuleView(snapshot, state).RuleControls!.Value;
        Assert.Equal(snapshot.PlayerExperience!.Value.Player, view.Data!.Player);
        Assert.Equal(view.Data.Facility, view.Draft.Facility);
        Assert.Equal(state == UiReferenceScenario.OperationsRuleDraft, view.NewDraft);
        Assert.Equal(state == UiReferenceScenario.OperationsRulePending, view.Pending);
        if (state == UiReferenceScenario.OperationsRuleManagedPause)
        {
            Assert.True(view.Observed!.Value.ManagedProductionRequest.IsValid);
            Assert.Single(view.Data.ManagedRequests);
        }
        if (state == UiReferenceScenario.OperationsRuleManualOverride)
        {
            Assert.Equal(OperationsRuleResult.ManualOverride, view.Observed!.Value.Result);
            Assert.False(view.Observed.Value.ManagedProductionRequest.IsValid);
        }
        if (state == UiReferenceScenario.OperationsRuleRejected) Assert.True(view.Dirty);
    }

    [Fact]
    public void GroupReferenceStatesRetainWorldAndFitTheSharedHudAcrossAcceptanceDisplays()
    {
        var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var source=VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat,terrain).Snapshot;
        foreach(var state in new[]{UiReferenceScenario.GroupRing,UiReferenceScenario.GroupRingTools,UiReferenceScenario.GroupRingPartial,UiReferenceScenario.GroupRingEdge})
        foreach(var display in new[]{(1280,720),(1920,1080),(2560,1440),(3440,1440),(3840,2160)})
        foreach(var density in new[]{(96u,1f),(192u,2f)})
        {
            var snapshot=GroupActionRingCapture.Fixture(source,state);
            Assert.True(source.Instances.SequenceEqual(snapshot.Instances));
            var buffer=new PresentationSnapshotBuffer();buffer.Publish(snapshot);var world=new RenderWorld();world.Update(buffer);
            var layout=GameplayHudLayout.Create(display.Item1,display.Item2,density.Item1,density.Item2);
            var panel=GroupActionRingCapture.View(snapshot,state,world,new RtsCamera(),layout,density.Item1,density.Item2);
            Assert.True(panel.Ring.Open);Assert.True(panel.Ring.Geometry.IsValid,$"{state} {display} {density}");
            Assert.False(panel.DockExpanded);Assert.NotNull(panel.Ring.Group);
            var occupied=new HudRect[8];
            RadialActionLayout.ResolveObstacles(occupied,layout,OnboardingPresentation.Resolve(layout,0),
                notifications:ActionableAlertLayout.Resolve(snapshot,layout));
            foreach(var rect in occupied)
            {
                if(rect.IsEmpty) continue;
                var nearest=new System.Numerics.Vector2(Math.Clamp(panel.Ring.Geometry.Center.X,rect.X,rect.Right),Math.Clamp(panel.Ring.Geometry.Center.Y,rect.Y,rect.Bottom));
                Assert.True(System.Numerics.Vector2.Distance(nearest,panel.Ring.Geometry.Center)>=panel.Ring.Geometry.OuterRadius);
            }
            Assert.True(layout.SafeArea.Contains(panel.Ring.Geometry.Center-new System.Numerics.Vector2(panel.Ring.Geometry.OuterRadius)));
            Assert.True(layout.SafeArea.Contains(panel.Ring.Geometry.Center+new System.Numerics.Vector2(panel.Ring.Geometry.OuterRadius)));
            if(state==UiReferenceScenario.GroupRingPartial) Assert.InRange(panel.Ring.VisibleMembers,1,panel.Ring.Group.Count-1);
        }
    }

    [Fact]
    public void NativeInteractionPairsMustChangePixelsWithinTheSameDisplayScope()
    {
        var hashes = new Dictionary<string,string>(StringComparer.Ordinal);
        UiReferenceStates.ConfirmDistinct(hashes,"compact-",UiReferenceScenario.DockHover,[1,2,3,255]);
        Assert.Throws<InvalidDataException>(() => UiReferenceStates.ConfirmDistinct(hashes,"compact-",UiReferenceScenario.DockPressed,[1,2,3,255]));
        UiReferenceStates.ConfirmDistinct(hashes,"wide-",UiReferenceScenario.DockPressed,[1,2,3,255]);
        UiReferenceStates.ConfirmDistinct(hashes,"wide-",UiReferenceScenario.DockHover,[2,2,3,255]);
    }

    [Fact]
    public void NamedMatrixRejectsUnsupportedOrAmbiguousInputsBeforeNativeStartup()
    {
        var request = new VisualReferenceCaptureRequest { OutputRoot = "unused", RuntimeRoot = "unused", Commit = new('a', 40),
            UiScenarios = [UiReferenceScenario.Menu], Scenes = [VisualReferenceSceneKind.BattlefieldCombat],
            Zooms = [RtsReferenceZoom.NormalGameplay], Displays = [new(1280, 720)], UiScales = [.75f, 1, 2] };
        request.Validate();
        Assert.Throws<ArgumentException>(() => (request with { UiScenarios = [UiReferenceScenario.Menu, UiReferenceScenario.Menu] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { UiScenarios = [(UiReferenceScenario)255] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { UiScales = [float.NaN] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { UiScales = [1, 1] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { CameraPath = ReferenceCameraPath.Traverse }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Scenes = [VisualReferenceSceneKind.IndustrialBase] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { UiScenarios = [UiReferenceScenario.World] }).Validate());
    }

    [Fact]
    public void FrontendStatesUseSharedBoundedInputRegionsAcrossAcceptanceMatrix()
    {
        foreach (var d in new[] { (1280,720), (1920,1080), (2560,1440), (3440,1440), (3840,2160) })
        foreach (uint dpi in new uint[] {96,144,192})
        foreach (float scale in new[] {.75f,1,2})
        foreach (var state in Enum.GetValues<UiReferenceScenario>().Where(UiReferenceStates.IsFrontend))
        {
            var view = UiReferenceStates.Frontend(state, d.Item1, d.Item2, dpi, scale);
            var g = view.Layout!.Value;
            var layout = FrontendDesign.ResolveLayout(d.Item1, d.Item2, scale, dpi, view.Kind == FrontendSurfaceKind.Controls);
            for (int i = 0; i < view.MenuEntries.Count; i++)
            {
                var r = g.MenuRow(i);
                Assert.True(r.X >= 0 && r.Right <= d.Item1 && r.Y >= 0 && r.Bottom <= d.Item2);
                Assert.True(r.Contains(r.X + 1, r.Y + 1));
                Assert.False(r.Contains(r.Right, r.Y + 1));
            }
            for (int i = 0; i < Math.Min(view.DetailLines.Count, g.VisibleDetailRows); i++)
            {
                var r = g.DetailRow(i);
                Assert.Equal(i, FrontendHitTesting.DetailRow(r.X + 1, r.Y + 1, layout, view.DetailLines.Count));
                Assert.True(r.Bottom < g.PrimaryAction.Y);
            }
            Assert.True(g.PrimaryAction.Right <= d.Item1 && g.Feedback.Bottom <= d.Item2);
            if (state == UiReferenceScenario.Pause)
            {
                var backdrop = g.PauseBackdrop;
                Assert.True(backdrop.Y <= g.Header.Y && backdrop.Bottom >= g.Footer.Bottom);
                Assert.True(backdrop.Y <= g.Title.Y && backdrop.Bottom >= g.MenuRow(view.MenuEntries.Count - 1).Bottom);
                Assert.Equal(g.Navigation.X, backdrop.X);
                Assert.Equal(g.Navigation.Right, backdrop.Right);
                Assert.True(backdrop.Right < d.Item1); // Battlefield context remains visible.
                Assert.False(backdrop.Interactive);
            }
        }
        Assert.Empty(UiReferenceStates.Frontend(UiReferenceScenario.LoadBroken,1920,1080,96,1).PrimaryAction);
        Assert.Contains(UiReferenceStates.Frontend(UiReferenceScenario.MenuDisabled,1920,1080,96,1).MenuEntries,x=>!x.IsEnabled);
    }

    [Fact]
    public void HudStatesRetainOriginalIntelligenceAndWorldWithoutIssuingCommands()
    {
        var terrain = CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var original = VisualReferenceScene.Create(VisualReferenceSceneKind.BattlefieldCombat, terrain).Snapshot;
        foreach (var state in Enum.GetValues<UiReferenceScenario>().Where(x => !UiReferenceStates.IsFrontend(x)))
        {
            var (snapshot, panel) = UiReferenceStates.Hud(original,state,1920,1080,96,1);
            Assert.Same(original.Intelligence,snapshot.Intelligence);
            Assert.True(original.Instances.SequenceEqual(snapshot.Instances));
            Assert.Equal(original.SessionId,snapshot.SessionId);
            Assert.Equal(original.Tick,snapshot.Tick);
            if(ObjectActionRingCapture.IsRing(state) && state < UiReferenceScenario.GroupRing &&
                state is not (UiReferenceScenario.SelectedGroup or UiReferenceScenario.MixedSelection))
            {
                var entity=snapshot.PlayerExperience!.Value.Selection.PrimaryEntity;
                var actor=Assert.Single(snapshot.Instances.ToArray(),i=>i.Entity==entity);
                var expected=ObjectActionRingCapture.Mode(state) is PlayerActionPanelMode.Tactical or PlayerActionPanelMode.Supply
                    ? ControllableEntityCategory.Unit : ControllableEntityCategory.Building;
                Assert.NotEqual(ControllableEntityCategory.None,actor.Selectable.Category & expected);
                Assert.Equal(1,snapshot.PlayerExperience.Value.Selection.Count);
            }
            if (state is UiReferenceScenario.SelectedGroup or UiReferenceScenario.MixedSelection)
            {
                var members = Assert.IsType<CombatGroupOperationalSnapshot>(SelectedCombatGroup.Resolve(snapshot));
                Assert.Equal(snapshot.PlayerExperience!.Value.Selection.Count, members.Members.Count);
                Assert.All(members.Members, m => Assert.Contains(original.Instances.ToArray(), i => i.Entity == m.Entity && i.Selectable.Owner == original.PlayerExperience!.Value.Player));
            }
            if (state == UiReferenceScenario.SelectionUnit) Assert.Equal(ForgeLine.Game.UnitIds.MainBattleTank, snapshot.PlayerExperience!.Value.Selection.CommonUnitId);
            if (state == UiReferenceScenario.SelectionBuilding) Assert.Equal(ForgeLine.Game.BuildingIds.VehicleFactory, snapshot.PlayerExperience!.Value.Selection.CommonBuildingId);
            if (state == UiReferenceScenario.SelectionEmpty) Assert.Equal(0, snapshot.PlayerExperience!.Value.Selection.Count);
            if (state == UiReferenceScenario.MissingPlayerData) Assert.Null(snapshot.PlayerExperience);
            if (state == UiReferenceScenario.DockPending) Assert.True(panel.ContextualPending);
            if (state == UiReferenceScenario.ProductionQueue) { Assert.Equal(PlayerActionPanelMode.Production,panel.Mode); Assert.Equal(1,panel.SelectedIndex); }
            if (state == UiReferenceScenario.LogisticsBlocked) Assert.Equal(PlayerActionPanelMode.Logistics,panel.Mode);
            if (state == UiReferenceScenario.LogisticsInputs)
            {
                var logistics = Assert.IsType<PlayerLogisticsActionReadModel>(snapshot.PlayerActions!.Logistics);
                Assert.Equal(logistics.Policies.Count, panel.SelectedIndex);
                Assert.True(ProductionStockProposal.Create(logistics,Assert.Single(logistics.Requirements),1).FitsCapacity);
                Assert.Equal("USE INPUTS",PlayerActionDockHudModel.ResolvePrimaryActionLabel(panel.Mode,panel.SelectedIndex,snapshot.PlayerActions));
            }

            if (state == UiReferenceScenario.SupplyPolicy) Assert.Equal(PlayerActionPanelMode.Supply,panel.Mode);
            if (state == UiReferenceScenario.TechnologyBlocked) Assert.Equal(PlayerTechnologyState.Locked,snapshot.PlayerActions!.Technology[0].State);
            if (state == UiReferenceScenario.Operations) Assert.Single(snapshot.Operations!.Facilities);
            if (state == UiReferenceScenario.OperationsEmpty) Assert.Empty(snapshot.Operations!.Facilities);
            if (state == UiReferenceScenario.OperationsMissing) Assert.Null(snapshot.Operations);
            if (state is UiReferenceScenario.DockHover or UiReferenceScenario.DockPressed)
            {
                Assert.Equal(1, panel.HoveredIndex);
                Assert.Equal(0, panel.SelectedIndex); // Hover a separate enabled card instead of the focused or technology-locked unit.
                Assert.Equal(PlayerActionPanelMode.Construction,panel.Mode);
                Assert.True(snapshot.PlayerActions!.Construction[1].HasRequiredResources);
                Assert.Equal(state == UiReferenceScenario.DockPressed, panel.PointerPressed);
            }
            if (state == UiReferenceScenario.Targeting) Assert.Equal(PlayerActionPanelMode.Tactical, panel.Mode);
            if (state == UiReferenceScenario.DockRejected) Assert.Equal(PlayerCommandFeedbackState.Rejected,snapshot.PlayerExperience!.Value.Feedback.State);
            if (state == UiReferenceScenario.DockAccepted) Assert.Equal(PlayerCommandFeedbackState.Accepted,snapshot.PlayerExperience!.Value.Feedback.State);
        }
    }
}
