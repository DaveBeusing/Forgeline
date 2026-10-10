using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using ForgeLine.Input;
using ForgeLine.Graphics;
using ForgeLine.Platform;
using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class SelectedCombatGroupTests
{
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
        Click(input, layout, 3);
        var result = controller.Update(input, snapshot, selection, layout);
        Assert.True(result.Captured);
        Assert.True(result.SelectionChanged);
        Assert.Equal(1, selection.Count);
        Assert.True(selection.Contains(new EntityId(1, 1)));
        Assert.Equal(2, registry.GetSlot(1).Members.Length);
        Assert.False(controller.Update(input, snapshot, selection, layout).SelectionChanged);
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
        Click(input, layout, control);
        var result = controller.Update(input, Snapshot(), selection, layout);
        Assert.Equal(2, selection.Count);
        Assert.Equal(control is 8 or 9, result.FocusMember.IsValid);
        Assert.Equal(control == 10, result.CycleFormation);
        Assert.Equal(control == 11, result.FocusSelection);
        Assert.False(result.SelectionChanged);
    }

    [Fact]
    public void BlockedFocusResizeSessionAndStaleTicksConsumePressWithoutLaterActivation()
    {
        var input = new InputState();
        var controller = new CombatGroupCardController();
        var selection = Selection();
        var layout = GameplayHudLayout.Create(1600, 900, 96);
        var snapshot = Snapshot();
        Click(input, layout, 3);
        Assert.False(controller.Update(input, snapshot, selection, layout, blocked: true).SelectionChanged);
        Assert.False(controller.Update(input, snapshot, selection, layout).SelectionChanged);
        Click(input, layout, 3);
        Assert.False(controller.Update(input, snapshot, selection, GameplayHudLayout.Create(1920, 1080, 96)).SelectionChanged);
        Click(input, layout, 3);
        Assert.False(controller.Update(input, Snapshot(session: 2), selection, layout).SelectionChanged);
        Click(input, layout, 3);
        Assert.False(controller.Update(input, Snapshot(session: 2, memberTick: 7), selection, layout).SelectionChanged);
        Click(input, layout, 3);
        input.Apply(PlatformInputEvent.FocusLost());
        Assert.False(controller.Update(input, Snapshot(session: 2), selection, layout).SelectionChanged);
        Assert.Equal(2, selection.Count);
    }

    [Theory]
    [InlineData(1024, 720, 96, 1f)]
    [InlineData(1024, 720, 192, 2f)]
    [InlineData(1600, 900, 144, 1.25f)]
    [InlineData(1920, 1080, 96, 1f)]
    [InlineData(3840, 2160, 192, 2f)]
    public void RenderAndControlsShareBoundedGeometryWithZeroWarmAllocation(int width, int height, uint dpi, float scale)
    {
        var snapshot = Snapshot();
        var group = SelectedCombatGroup.Create(snapshot.CombatGroups!, Selection());
        var layout = GameplayHudLayout.Create(width, height, dpi, scale);
        for (int i = 0; i < 12; i++)
        {
            var rect = CombatGroupCardLayout.Control(layout, i);
            Assert.True(layout.SelectionInspector.Contains(new Vector2(rect.X, rect.Y)));
            Assert.True(layout.SelectionInspector.Contains(new Vector2(rect.Right, rect.Bottom)));
        }
        using var device = new SelectionOverlayRenderingTests.RecordingDevice();
        using var renderer = new SelectionInspectorHudRenderer(device, null);
        var context = new SelectionOverlayRenderingTests.RecordingContext { Width = width, Height = height };
        renderer.Render(context, snapshot, layout, group);
        using var retained = new RetainedDevice();
        using var measured = new SelectionInspectorHudRenderer(retained, null);
        for (int i = 0; i < 128; i++) measured.Render(context, snapshot, layout, group);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) measured.Render(context, snapshot, layout, group);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.InRange(renderer.LastRenderedVertexCount, 1, 131072);
        Assert.All(device.Buffer!.Vertices, vertex =>
        { Assert.InRange(vertex.Position.X, -1, 1); Assert.InRange(vertex.Position.Y, -1, 1); });
    }

    private static SelectionSet Selection()
    { var result = new SelectionSet(); result.Replace([new EntityId(1, 1), new EntityId(2, 1)]); return result; }

    private static PresentationSnapshot Snapshot(ulong session = 1, ulong memberTick = 4)
    {
        var tick = new SimulationTick(4);
        var experience = default(PlayerExperienceSnapshot) with { Tick = tick,
            Selection = PlayerSelectionSummary.Empty with { Count = 2, Kind = PlayerSelectionKind.Mixed } };
        var members = new CombatGroupOperationalSnapshot(new SimulationTick(memberTick),
            [Member(new EntityId(1, 1), UnitIds.MainBattleTank, true), Member(new EntityId(2, 1), UnitIds.SupplyTruck, false)], new(session));
        return new(tick, TimeSpan.Zero, 2, [], sessionId: new(session), playerExperience: experience, combatGroups: members);
    }

    private static void Click(InputState input, in GameplayHudLayout layout, int index)
    {
        input.BeginFrame();
        var rect = CombatGroupCardLayout.Control(layout, index);
        int x = (int)(rect.X + rect.Width / 2), y = (int)(rect.Y + rect.Height / 2);
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown, PlatformMouseButton.Left, x, y));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonUp, PlatformMouseButton.Left, x, y));
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
