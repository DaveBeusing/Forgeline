using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class MechanicalMotionQualityTests
{
    private static readonly EntityId Entity = new(4, 1);

    [Fact]
    public void RepeatedCompletedTickPreservesWheelDoorAndRecoilState()
    {
        var history = new MechanicalPoseHistory();
        history.Begin(SimulationSessionId.None, 10, 0.05);
        Capture(history, Entity, 0);
        history.Begin(SimulationSessionId.None, 11, 0.05);
        MechanicalPose first = history.Capture(Entity, Vector3.UnitZ, Quaternion.Identity,
            active: true, shot: true, elevation: 0.2f, openDoor: true);

        history.Begin(SimulationSessionId.None, 11, 0.05);
        MechanicalPose repeated = history.Capture(Entity, Vector3.UnitZ, Quaternion.Identity,
            active: false, shot: false, elevation: 0, openDoor: false);

        Assert.Equal(first, repeated);
        Assert.Equal(1, repeated.TravelMeters);
        Assert.Equal(0.05, repeated.ActiveSeconds, 6);
        Assert.Equal(0.05f, repeated.Door, 6);
        Assert.Equal(0.55, repeated.LastShotSeconds, 6);
        Assert.True(history.HasMoved(Entity));
    }

    [Fact]
    public void DuplicateCaptureWithinTickReturnsOnePoseWithoutAdvancingAnimation()
    {
        var history = new MechanicalPoseHistory();
        history.Begin(SimulationSessionId.None, 1, 0.05);
        MechanicalPose first = Capture(history, Entity, 0);
        MechanicalPose repeated = Capture(history, Entity, 0);

        Assert.Equal(first, repeated);
        Assert.False(history.HasMoved(Entity));
    }

    [Fact]
    public void TickAfterRepeatedExtractionAdvancesExactlyOnce()
    {
        var history = new MechanicalPoseHistory();
        history.Begin(SimulationSessionId.None, 10, 0.05);
        Capture(history, Entity, 0);
        history.Begin(SimulationSessionId.None, 11, 0.05);
        Capture(history, Entity, 1);
        history.Begin(SimulationSessionId.None, 11, 0.05);
        Capture(history, Entity, 1);
        history.Begin(SimulationSessionId.None, 12, 0.05);
        MechanicalPose next = Capture(history, Entity, 2);

        Assert.Equal(2, next.TravelMeters);
        Assert.Equal(0.1, next.ActiveSeconds, 6);
        Assert.Equal(0.1f, next.Door, 6);
    }

    [Fact]
    public void HiddenTickPrunesMotionHistoryBeforeReappearance()
    {
        var history = new MechanicalPoseHistory();
        history.Begin(SimulationSessionId.None, 1, 0.05);
        Capture(history, Entity, 0);
        history.Begin(SimulationSessionId.None, 2, 0.05);
        history.Begin(SimulationSessionId.None, 3, 0.05);
        MechanicalPose visibleAgain = Capture(history, Entity, 12);

        Assert.Equal(0, visibleAgain.TravelMeters);
        Assert.False(history.HasMoved(Entity));
    }

    [Fact]
    public void ReusedEntitySlotDoesNotInheritRemovedUnitWheelPhase()
    {
        var history = new MechanicalPoseHistory();
        history.Begin(SimulationSessionId.None, 1, 0.05);
        Capture(history, Entity, 0);
        history.Begin(SimulationSessionId.None, 2, 0.05);
        Capture(history, Entity, 3);
        history.Begin(SimulationSessionId.None, 3, 0.05);
        MechanicalPose replacement = Capture(history, new EntityId(4, 2), 5);

        Assert.Equal(0, replacement.TravelMeters);
    }

    [Fact]
    public void NonContiguousTickResetsHistoryInsteadOfInventingTravel()
    {
        var history = new MechanicalPoseHistory();
        history.Begin(SimulationSessionId.None, 1, 0.05);
        Capture(history, Entity, 0);
        history.Begin(SimulationSessionId.None, 2, 0.05);
        Capture(history, Entity, 3);
        history.Begin(SimulationSessionId.None, 20, 0.05);
        MechanicalPose resumed = Capture(history, Entity, 5);

        Assert.Equal(0, resumed.TravelMeters);
        Assert.Equal(0, resumed.ActiveSeconds);
    }

    [Fact]
    public void ReverseTravelAndRenderInterpolationPreserveSignedWheelDistance()
    {
        var history = new MechanicalPoseHistory();
        history.Begin(SimulationSessionId.None, 1, 0.05);
        Capture(history, Entity, 0);
        history.Begin(SimulationSessionId.None, 2, 0.05);
        MechanicalPose forward = Capture(history, Entity, 1);
        history.Begin(SimulationSessionId.None, 3, 0.05);
        MechanicalPose reverse = Capture(history, Entity, 0.5f);

        Assert.Equal(1, forward.TravelMeters);
        Assert.Equal(0.5, reverse.TravelMeters);
        Assert.Equal(1, MechanicalPose.Interpolate(forward, reverse, 0).TravelMeters);
        Assert.Equal(0.75, MechanicalPose.Interpolate(forward, reverse, 0.5f).TravelMeters);
        Assert.Equal(0.5, MechanicalPose.Interpolate(forward, reverse, 1).TravelMeters);
    }

    private static MechanicalPose Capture(MechanicalPoseHistory history, EntityId entity, float z) =>
        history.Capture(entity, new Vector3(0, 0, z), Quaternion.Identity,
            active: true, shot: false, elevation: 0, openDoor: true);
}
