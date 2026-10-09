using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CameraFocusInteractionTests
{
    [Fact]
    public void HomeUsesOwnedCoreThenLiveBuildingFallbackWithoutChangingSelectionOrCameraAngles()
    {
        var core = Instance(1, new(90, 2, 40), BuildingIds.CommandCore);
        var factory = Instance(2, new(40, 0, 20), BuildingIds.VehicleFactory);
        var foreign = Instance(3, new(900, 0, 900), BuildingIds.CommandCore) with
        { Selectable = new(new PlayerId(2), ControllableEntityCategory.Building) };
        var camera = new RtsCamera();
        var old = camera.CaptureState();
        var selection = new SelectionSet();
        selection.SetSingle(factory.Entity);
        var controller = new RtsCameraFocusController(new PlayerId(1));
        var input = new InputState();
        Tap(input, PlatformKey.Home);
        Assert.True(controller.Update(input, Snapshot(core, foreign, factory), camera, selection, false, default));
        Assert.Equal(core.Transform.Position, camera.Target);
        Assert.Equal(old.Distance, camera.Distance);
        Assert.Equal(old.YawRadians, camera.YawRadians);
        Assert.True(selection.Contains(factory.Entity));
        Tap(input, PlatformKey.Home);
        Assert.True(controller.Update(input, Snapshot(foreign, factory), camera, selection, false, default));
        Assert.Equal(factory.Transform.Position, camera.Target);
        Tap(input, PlatformKey.Home);
        Assert.False(controller.Update(input, Snapshot(foreign), camera, selection, false, default));
    }

    [Fact]
    public void BlockedOrCustomBoundHomeCannotFocusOrLeakAfterUnblocking()
    {
        var input = new InputState();
        var controller = new RtsCameraFocusController(new PlayerId(1));
        var snapshot = Snapshot(Instance(1, new(80, 0, 40), BuildingIds.CommandCore));
        var camera = new RtsCamera();
        var selection = new SelectionSet();
        Tap(input, PlatformKey.Home);
        Assert.False(controller.Update(input, snapshot, camera, selection, false, default, blocked: true));
        Assert.False(controller.Update(input, snapshot, camera, selection, false, default));
        Tap(input, PlatformKey.Home);
        Assert.False(controller.Update(input, snapshot, camera, selection, false, default,
            bindings: new RtsCameraBindings { PanLeft = PlatformKey.Home }));
    }

    [Fact]
    public void GroupFocusUsesCurrentVisibleOwnedPositionsAndFullGeneration()
    {
        var first = Instance(1, new(10, 0, 20), BuildingIds.CommandCore);
        var second = Instance(2, new(30, 0, 40), BuildingIds.VehicleFactory);
        var hidden = Instance(3, new(900, 0, 900), BuildingIds.PowerPlant) with { Visibility = RenderVisibilityMask.None };
        var selection = new SelectionSet();
        selection.Replace([first.Entity, second.Entity, hidden.Entity, new EntityId(4, 1)]);
        var reused = Instance(4, new(500, 0, 500), BuildingIds.PowerPlant) with { Entity = new EntityId(4, 2) };
        Assert.True(RtsCameraFocusController.TryGroup(Snapshot(first, second, hidden, reused), new(1), selection, out var target));
        Assert.Equal(new Vector3(20, 0, 30), target);
    }

    [Fact]
    public void DoubleTapRetainsShortKeyEdgesButRejectsHoldsTimeoutModifiersAndInterruptedSlots()
    {
        var member = new CombatGroupMemberReadModel(new(1, 1), ControllableEntityCategory.Unit,
            false, 0, false, default, 0, 0, false, 0, 0, false, default, false, default, false, default);
        var snapshot = new PresentationSnapshot(new(1), TimeSpan.FromMilliseconds(50), 1, [],
            sessionId: new(1), combatGroups: new(new(1), [member]));
        var registry = new CombatGroupRegistry();
        registry.Synchronize(snapshot.SessionId, snapshot.CombatGroups!.EligibleEntities);
        var selection = new SelectionSet();
        selection.SetSingle(member.Entity);
        registry.Assign(1, selection.Entities, snapshot.CombatGroups.EligibleEntities);
        var input = new InputState();
        var controller = new CombatGroupInputController(TimeSpan.FromMilliseconds(200));
        CombatGroupInputResult Update(int ms = 0, bool blocked = false) =>
            controller.Update(input, snapshot, registry, selection, blocked, TimeSpan.FromMilliseconds(ms));
        Tap(input, PlatformKey.D1);
        Assert.False(Update().FocusRequested);
        Tap(input, PlatformKey.D1);
        Assert.True(Update(100).FocusRequested);
        Assert.False(Update().Handled);
        Tap(input, PlatformKey.D1);
        Assert.False(Update().FocusRequested);
        Tap(input, PlatformKey.D1);
        Assert.False(Update(201).FocusRequested);
        Tap(input, PlatformKey.D2);
        Update();
        Tap(input, PlatformKey.D1);
        Assert.False(Update().FocusRequested);
        Update(blocked: true);
        Tap(input, PlatformKey.D1);
        Assert.False(Update().FocusRequested);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, PlatformKey.LeftControl));
        Tap(input, PlatformKey.D1);
        Assert.Equal(CombatGroupInputAction.Assigned, Update().Action);
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, PlatformKey.LeftControl));
        Tap(input, PlatformKey.D1);
        Assert.False(Update().FocusRequested);
    }

    internal static void Tap(InputState input, PlatformKey key)
    {
        input.BeginFrame();
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown, key));
        input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyUp, key));
    }

    private static PresentationSnapshot Snapshot(params RenderInstance[] instances) =>
        new(new(1), TimeSpan.FromMilliseconds(50), instances.Length, instances, sessionId: new(1));

    private static RenderInstance Instance(uint id, Vector3 position, BuildingId building) =>
        new(new(id, 1), new(position, Quaternion.Identity, Vector3.One), new(1), RenderMaterialHandle.Default,
            RenderVisibilityMask.World, Selectable: new(new PlayerId(1), ControllableEntityCategory.Building),
            BuildingFeature: new(building, BuildingPresentationState.Operational));
}
