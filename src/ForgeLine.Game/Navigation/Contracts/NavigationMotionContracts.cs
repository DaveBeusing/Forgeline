using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>All supply, tactical, formation and convoy limits must already be combined by Game.</summary>
public readonly record struct NavigationMotionInput(
    NavigationOrderBinding Binding, SimulationTick Tick, float DeltaSeconds,
    NavigationVehicleObservation Vehicle, NavigationTrafficDirective Traffic,
    NavigationRouteProposal? Route, float EffectiveMaximumSpeed, bool CanMove);

/// <summary>A proposal only. The authoritative movement path must still approve its entire sweep.</summary>
public readonly record struct NavigationMotionProposal(
    NavigationModuleAvailability Availability, NavigationOrderBinding Binding,
    SimulationTick Tick, NavigationSnapshotRevision Revision, NavigationPose Start, NavigationPose End,
    float DesiredSpeed, float Curvature, float BrakingDistance, float SafeRemainingDistance,
    NavigationBlockReason Reason);

/// <summary>Stateless fixed-tick planning; any future history requires a typed state-transfer contract.</summary>
public interface INavigationMotionPlanner
{
    NavigationModuleAvailability Availability { get; }
    NavigationMotionProposal Propose(in NavigationMotionInput input,
        NavigationCollisionFrame collision, INavigationGeometryQuery geometry,
        int workAllowance, out int workConsumed);
}
