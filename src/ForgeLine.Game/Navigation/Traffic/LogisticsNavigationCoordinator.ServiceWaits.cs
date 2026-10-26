using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

internal sealed partial class LogisticsNavigationCoordinator
{
    // LaneClear can hold a reserved service owner before it has a movement order.
    // Record only a current physical sweep veto, never infer a cycle from its lease.
    // This is optional observation work; it grants no motion or service admission.
    private void CaptureBlockedServiceWaits(SimulationContext context, NavigationTrafficFrame frame,
        Candidate[] candidates, List<NavigationPhysicalWait> observations, ref int consumed)
    {
        int limit = consumed + Math.Min(16384, (Limits.WorkPerTick - consumed) / 4);
        int probes = 0;
        int first = candidates.Length == 0 ? 0 : (int)(context.Tick.Value % (ulong)candidates.Length);
        for (int offset = 0; offset < candidates.Length; offset++)
        {
            var candidate = candidates[(first + offset) % candidates.Length];
            if (!candidate.CanMove || candidate.Vehicle.Movement.Velocity.LengthSquared() > .0025f ||
                context.Tick.Value < candidate.Binding.OriginalOrder.SubmittedAtTick.Value ||
                context.Tick.Value - candidate.Binding.OriginalOrder.SubmittedAtTick.Value < 40) continue;
            foreach (var reservation in frame.Reservations)
            {
                if (!NavigationMotionCapture.Spend(limit, ref consumed) || observations.Count == MaximumVehicles || probes == 4) return;
                if (reservation.Kind != NavigationReservationKind.Dock || reservation.Owner == candidate.Vehicle.Entity ||
                    !NavigationMotionCapture.TryCaptureVehicle(context.Entities, reservation.Owner, _movementOptions, out var owner) ||
                    owner.DirectOrder.HasValue || owner.DockVisit is not { Generation: > 0, LaneEntered: false, Status: VehicleDockStatus.Blocked } visit ||
                    owner.BuildingSlot is not { } slot || slot.Truck != owner.Entity || slot.Owner != owner.Owner ||
                    slot.Generation != visit.Generation || slot.Generation != reservation.Generation || slot.Pose != visit.Pose ||
                    owner.QueueEntry is not { Position: 1, ArrivedAtService: false } queue || queue.Generation == 0 ||
                    queue.Owner != owner.Owner || queue.Building != visit.Building || visit.Pose.ApproachStart != queue.Target ||
                    !NavigationMotionArbitrator.TryBuildSweptBounds(candidate.Vehicle.Pose, candidate.Vehicle.Pose,
                        candidate.Vehicle.Profile, out var occupied) || !visit.Pose.IntersectsLane(occupied)) continue;
                if (limit - consumed < 2) return;
                consumed += 2;
                if (!frame.Collision.World.TrySampleHeight(owner.Pose.Position.X, owner.Pose.Position.Z, out float startHeight) ||
                    !frame.Collision.World.TrySampleHeight(visit.Pose.Exit.X, visit.Pose.Exit.Z, out float endHeight)) continue;
                Vector3 endPosition = visit.Pose.Exit with { Y = endHeight + owner.Pose.Position.Y - startHeight };
                var end = new NavigationPose(endPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitY, visit.Pose.Heading));
                var sweep = new NavigationSweep(NavigationReadAdapter.GetObstacleIdentity(owner.Entity), owner.Pose, end,
                    owner.Profile, MathF.Max(.05f, Vector3.Distance(owner.Pose.Position, endPosition) / owner.Profile.MaximumSpeed));
                if (consumed == limit) return;
                probes++;
                var collision = frame.GetVehicleCollision(candidate.Vehicle.Entity);
                var result = PredictiveTrafficPlanner.SweepGrounded(collision, _geometry!, sweep, limit - consumed);
                if (result.WorkConsumed <= 0 || result.WorkConsumed > limit - consumed) { consumed = limit; return; }
                consumed += result.WorkConsumed;
                if (result.FrameTick != context.Tick.Value || result.Revision != frame.Collision.World.Revision ||
                    result.Status is not (NavigationGeometryStatus.Blocked or NavigationGeometryStatus.Limited) ||
                    result.Reason != NavigationBlockReason.MovingVehicle ||
                    result.Blocker != NavigationReadAdapter.GetObstacleIdentity(candidate.Vehicle.Entity)) continue;
                observations.Add(new(context.Tick, result.Revision, owner, candidate.Vehicle));
            }
        }
    }
}
