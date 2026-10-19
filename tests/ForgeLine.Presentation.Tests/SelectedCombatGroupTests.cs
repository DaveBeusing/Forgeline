using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Graphics;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectedCombatGroupTests
{
    [Fact]
    public void CoverageAndCriticalLabelsUseRecognizedPunctuationGlyphs()
    {
        foreach (char symbol in "<=()!")
        {
            string glyph = HudOverlayPrimitives.TextGlyphPattern(symbol);
            Assert.Equal(35, glyph.Length);
            Assert.NotEqual(HudOverlayPrimitives.TextGlyphPattern('?'), glyph);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void CountsUniqueCopiedLiveMembersAndComponentCoverage(int count)
    {
        var members = Enumerable.Range(1, count).Select(i => Member(new EntityId((uint)i, 1),
            i % 2 == 0 ? UnitIds.MainBattleTank : UnitIds.SupplyTruck, i % 2 == 0)).ToArray();
        var selected = new SelectionSet();
        selected.Replace(members.Select(static m => m.Entity).ToArray());
        var operational = new CombatGroupOperationalSnapshot(new SimulationTick(9), members.Concat(members).ToArray());
        var group = SelectedCombatGroup.Create(operational, selected);
        Assert.Equal(count, operational.Members.Count);
        Assert.Equal(count, group.LiveCount);
        Assert.Equal(count / 2, group.CombatCount);
        Assert.Equal(count / 2, group.ReadinessCount);
        Assert.Equal(count, group.HealthCount);
        Assert.Equal(0.2, group.Health, 6);
        Assert.Equal(count, group.DamagedCount);
        Assert.Equal(count, group.UnsuppliedCount);
        Assert.Equal(count / 2, group.Composition(3));
        Assert.Equal(count - count / 2, group.Composition(6));
        if (count > 1) Assert.Equal(0.8, group.Readiness, 6);
    }

    [Fact]
    public void StaleGenerationsAndBuildingsDoNotBecomeUnitComposition()
    {
        var selected = new SelectionSet();
        selected.Replace([new EntityId(1, 1), new EntityId(2, 1), new EntityId(3, 1)]);
        var operational = new CombatGroupOperationalSnapshot(new SimulationTick(5),
            [Member(new EntityId(1, 2), UnitIds.MainBattleTank, true), Member(new EntityId(2, 1), default, false)]);
        var group = SelectedCombatGroup.Create(operational, selected);
        Assert.Equal(3, group.TotalCount);
        Assert.Equal(1, group.LiveCount);
        Assert.Equal(1, group.Composition(7));
        Assert.Equal(new EntityId(2, 1), group.DamagedMember);
    }

    [Fact]
    public void ExtractionCopiesOnlyOwnedLiveControllableUnitsAndRetainedFactsStayImmutable()
    {
        using var scenario = WorldHoverExtractionTests.CreateScenario();
        var entities = scenario.Simulation.Entities;
        EntityId Add(uint owner, ControllableEntityCategory category, double health)
        {
            var entity = entities.CreateEntity();
            entities.AddComponent(entity, new ControllableEntity(new PlayerId(owner), category));
            entities.AddComponent(entity, new UnitIdentity(UnitIds.MainBattleTank, new(1)));
            entities.AddComponent(entity, new HealthState(health, 100));
            return entity;
        }
        var owned = Add(1, ControllableEntityCategory.Unit, 20);
        var foreign = Add(2, ControllableEntityCategory.Unit, 80);
        var dead = Add(1, ControllableEntityCategory.Unit, 0);
        var building = Add(1, ControllableEntityCategory.Building, 80);
        var disabled = Add(1, ControllableEntityCategory.None, 80);
        var buffer = WorldHoverExtractionTests.Observe(scenario, new PresentationInteractionState());
        scenario.Simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        var operational = Assert.IsType<CombatGroupOperationalSnapshot>(SelectedCombatGroup.Resolve(snapshot));
        Assert.Equal(snapshot.SessionId, operational.SessionId);
        Assert.True(operational.TryGet(owned, out var member));
        Assert.Equal(UnitIds.MainBattleTank, member.Unit);
        Assert.False(member.CombatEligible);
        Assert.False(operational.TryGet(foreign, out _));
        Assert.False(operational.TryGet(dead, out _));
        Assert.False(operational.TryGet(building, out _));
        Assert.False(operational.TryGet(disabled, out _));
        entities.SetComponent(owned, HealthState.Full(100));
        scenario.Simulation.AdvanceOneTick();
        Assert.True(operational.TryGet(owned, out var retained));
        Assert.Equal(0.2, retained.HealthFraction);
        var exposed = Assert.IsAssignableFrom<IList<CombatGroupMemberReadModel>>(operational.Members);
        Assert.Throws<NotSupportedException>(() => exposed[0] = default);
    }

    [Fact]
    public void AttentionFocusCyclesWithoutSelectingOrOrderingMembers()
    {
        var snapshot = Snapshot();
        var selection = Selection();
        var controller = new CombatGroupCardController();
        var input = new InputState();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        Assert.Equal(new EntityId(1, 1), controller.Activate(8, snapshot, selection).FocusMember);
        Assert.Equal(new EntityId(2, 1), controller.Activate(8, snapshot, selection).FocusMember);
        Assert.Equal(new EntityId(1, 1), controller.Activate(8, snapshot, selection).FocusMember);
        Assert.Equal(2, selection.Count);
    }

    [Fact]
    public void RebuildingSummaryAllocatesBoundedStorageIndependentOfArmySize()
    {
        var members = Enumerable.Range(1, 1000).Select(i => Member(new EntityId((uint)i, 1), UnitIds.MainBattleTank, true)).ToArray();
        var selection = new SelectionSet();
        selection.Replace(members.Select(static member => member.Entity).ToArray());
        var operational = new CombatGroupOperationalSnapshot(new(4), members);
        for (int i = 0; i < 128; i++) _ = SelectedCombatGroup.Create(operational, selection);
        long start = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int i = 0; i < 256; i++) count = SelectedCombatGroup.Create(operational, selection).LiveCount;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(1000, count);
        Assert.InRange(allocated, 0, 256 * 512);
    }

    internal static CombatGroupMemberReadModel Member(EntityId entity, UnitId unit, bool combat) =>
        new(entity, ControllableEntityCategory.Unit, true, 0.2, true, BattlefieldSupplyStatus.Critical,
            0.3, 0.4, combat, 0.5, 0.8, false, default, false, default, false, default, unit, combat);

    [Fact]
    public void TypeFilterPreservesSavedSlotAndUsesOneRetainedPress()
    {
        var snapshot = Snapshot();
        var selection = Selection();
        var registry = new CombatGroupRegistry();
        registry.Synchronize(snapshot.SessionId, snapshot.CombatGroups!.EligibleEntities);
        registry.Assign(1, selection.Entities, snapshot.CombatGroups.EligibleEntities);
        var input = new InputState();
        var controller = new CombatGroupCardController();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var result = controller.Activate(3, snapshot, selection);
        Assert.True(result.Captured);
        Assert.True(result.SelectionChanged);
        Assert.Equal(1, selection.Count);
        Assert.True(selection.Contains(new EntityId(1, 1)));
        Assert.Equal(2, registry.GetSlot(1).Members.Length);
        Assert.False(controller.Activate(3, snapshot, selection).SelectionChanged);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void FocusAndFormationArePresentationIntentWithoutChangingSelection(int control)
    {
        var selection = Selection();
        var input = new InputState();
        var controller = new CombatGroupCardController();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var result = controller.Activate(control, Snapshot(), selection);
        Assert.Equal(2, selection.Count);
        Assert.Equal(control is 8 or 9, result.FocusMember.IsValid);
        Assert.Equal(control == 10, result.CycleFormation);
        Assert.Equal(control == 11, result.FocusSelection);
        Assert.False(result.SelectionChanged);
    }





    private static SelectionSet Selection()
    { var result = new SelectionSet(); result.Replace([new EntityId(1, 1), new EntityId(2, 1)]); return result; }

    [Fact]
    public void ContextualActionsWaitForExactFilteredSelectionCapture()
    {
        PresentationSnapshot Actions(EntityId[] captured)
        {
            var tick = new SimulationTick(4);
            var tactical = new PlayerTacticalActionReadModel(captured, captured.Length, captured.Length, 0,
                0, 0, false, false, default, default, [], []);
            var actions = new PlayerActionSnapshot(new(1), tick, [], 0, null, null, tactical: tactical);
            var experience = default(PlayerExperienceSnapshot) with
            {
                Tick = tick,
                Selection = PlayerSelectionSummary.Empty with { Count = captured.Length, Kind = PlayerSelectionKind.Unit }
            };
            return new(tick, TimeSpan.Zero, captured.Length, [], sessionId: new(1),
                playerExperience: experience, playerActions: actions);
        }
        var selection = new SelectionSet();
        selection.SetSingle(new EntityId(1, 1));
        var old = Actions([new EntityId(1, 1), new EntityId(2, 1)]);
        var controller = new PlayerActionPanelController();
        var input = new InputState();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        void ClickStop()
        {
            input.BeginFrame();
            input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,PlatformKey.K));
            controller.Update(input, old, 1600, 900, currentSelection: selection);
            input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp,PlatformKey.K));
            input.BeginFrame();
            // Stop is the existing third tactical item.
            for(int i=0;i<2;i++) {
                input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,PlatformKey.Tab));
                controller.Update(input, old, 1600,900,currentSelection:selection);
                input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp,PlatformKey.Tab)); input.BeginFrame();
            }
            input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,PlatformKey.Enter));
        }
        ClickStop();
        controller.Update(input, old, 1600, 900, currentSelection: selection);
        var view = controller.CreateView(1600, 900, old.PlayerActions);
        Assert.True(view.SelectionPending);
        Assert.True(ContextualCommandModel.TryGet(old, 2, out var disabled, selectionPending: view.SelectionPending));
        Assert.Equal("UPDATING SELECTION", disabled.Availability.DisabledReason);
        Assert.False(controller.TryTakeRequest(out _));
        var wrongIdentity = Actions([new EntityId(2, 1)]);
        Assert.False(PlayerActionDockHudModel.MatchesSelection(wrongIdentity.PlayerActions, selection));
        ClickStop();
        controller.Update(input, wrongIdentity, 1600, 900, currentSelection: selection);
        Assert.False(controller.TryTakeRequest(out _));

    }

    private static PresentationSnapshot Snapshot(ulong session = 1, ulong memberTick = 4)
    {
        var tick = new SimulationTick(4);
        var experience = default(PlayerExperienceSnapshot) with
        {
            Tick = tick,
            Selection = PlayerSelectionSummary.Empty with { Count = 2, Kind = PlayerSelectionKind.Mixed }
        };
        var members = new CombatGroupOperationalSnapshot(new SimulationTick(memberTick),
            [Member(new EntityId(1, 1), UnitIds.MainBattleTank, true), Member(new EntityId(2, 1), UnitIds.SupplyTruck, false)], new(session));
        return new(tick, TimeSpan.Zero, 2, [], sessionId: new(session), playerExperience: experience, combatGroups: members);
    }

    private sealed class RetainedDevice : IGraphicsDevice
    {
        public int BufferCount { get; private set; }
        public GraphicsDiagnostics Diagnostics => throw new NotSupportedException();
        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description) => new Pipeline(description);
        public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description) { BufferCount++; return new Buffer(description); }
        public void RenderFrame(GraphicsColor color, Action<IGraphicsCommandContext>? commands = null) { }
        public void Resize(int width, int height) { }
        public void WaitForIdle() { }
        public void Dispose() { }
    }

    private sealed class Pipeline(GraphicsPipelineDescription description) : IGraphicsPipeline
    {
        public GraphicsPipelineDescription Description => description;
        public void Dispose() { }
    }

    private sealed class Buffer(GraphicsBufferDescription description) : IGraphicsBuffer
    {
        public GraphicsBufferDescription Description => description;
        public void SetData<T>(ReadOnlySpan<T> data, int offsetInBytes = 0) where T : unmanaged { }
        public void Dispose() { }
    }
}
