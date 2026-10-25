using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

internal sealed partial class LogisticsNavigationCoordinator
{
    private readonly record struct Candidate(NavigationVehicleObservation Vehicle, NavigationOrderBinding Binding,
        NavigationRouteProposal Route, float MaximumSpeed, bool CanMove);
    private sealed record PhysicalCapture(NavigationCollisionFrame Collision, float MaximumSpeed, int Work);

    private LogisticsNavigationPlan PrepareCaptured(SimulationContext context)
    {
        var snapshot = _snapshot!;
        var physical = CapturePhysical(context, snapshot, Limits.WorkPerTick - _legacyWork);
        int consumed = _legacyWork + physical.Work;
        if (!physical.Collision.IsComplete) return Unavailable(NavigationBlockReason.MissingGeometry, consumed);
        var candidates = new Candidate[_active.Count];
        var observations = new NavigationVehicleObservation[_active.Count];
        var intents = new NavigationTrafficIntent[_active.Count];
        var probes = new List<NavigationPassageProbe>(_active.Count * 4);
        var neighborhoods = new Dictionary<EntityId, NavigationCollisionFrame>();
        for (int i = 0; i < _active.Count; i++)
        {
            if (!NavigationMotionCapture.Spend(Limits.WorkPerTick, ref consumed)) return Unavailable(NavigationBlockReason.SearchWork, consumed);
            var entity = _active[i];
            if (!NavigationMotionCapture.TryCaptureVehicle(context.Entities, entity, _movementOptions, out var vehicle) ||
                !context.Entities.TryGetComponent(entity, out NavigationRouteState route) ||
                route.SourceRequest is not { } source || source.Requester != entity || source.RequestId == 0 ||
                source.Destination != route.OriginalOrder.WorldTarget || source.NavigationVersion != snapshot.Revision.Geometry ||
                route.Path.Version != snapshot.Revision.Geometry || source.Capabilities != snapshot.Capabilities ||
                !new NavigationPose(source.Start, route.StartRotation).IsValid)
                return Unavailable(NavigationBlockReason.StaleSnapshot, consumed);
            if (!snapshot.Grid.TryWorldToCell(vehicle.Pose.Position, out _)) return Unavailable(NavigationBlockReason.OutsideWorld, consumed);
            var actor = _actors[entity];
            var binding = new NavigationOrderBinding(entity, route.OriginalOrder, source.RequestId, actor.Generation,
                vehicle.DockVisit?.Generation ?? 0, vehicle.QueueEntry?.Generation ?? 0);
            if (!NavigationPrecisionOwnership.Matches(vehicle, binding) || !TryAdaptRoute(entity, route, vehicle.Profile,
                snapshot, context.Entities.GetComponent<GroundMovement>(entity).StopRadius, ref consumed, out var adapted)) return Unavailable(NavigationBlockReason.StaleSnapshot, consumed);
            var (maximum, canMove) = CaptureConstraints(context, entity, vehicle.Profile.MaximumSpeed);
            if (!float.IsFinite(maximum) || maximum < 0) return Unavailable(NavigationBlockReason.Unknown, consumed);
            candidates[i] = new(vehicle, binding, adapted, maximum, canMove);
            observations[i] = vehicle;
            int waypoint = route.NextWaypointIndex - 1;
            intents[i] = new(binding, vehicle.DirectOrder!.Value.WorldTarget, maximum, canMove, adapted, waypoint);
            Vector3 probeStart = vehicle.Pose.Position;
            float probeRemaining = MathF.Max(snapshot.Grid.Settings.CellSizeMeters, maximum * Limits.PlanningHorizonSeconds);
            for (int probeIndex = waypoint, segment = 0; probeRemaining > 0 && probeIndex < adapted.Waypoints.Count; probeIndex++, segment++)
            {
                if (segment == 16) return Unavailable(NavigationBlockReason.SearchWork, consumed);
                Vector3 target = adapted.Waypoints[probeIndex];
                float distance = new Vector2(target.X - probeStart.X, target.Z - probeStart.Z).Length();
                if (!float.IsFinite(distance)) return Unavailable(NavigationBlockReason.Unknown, consumed);
                probes.Add(new(probeStart, target, probeRemaining));
                probeRemaining -= distance;
                probeStart = target;
            }
            var local = CaptureNeighborhood(physical, vehicle, context.Tick, ref consumed);
            if (local is null) return Unavailable(NavigationBlockReason.SearchWork, consumed);
            // Current-pose occupancy is the planning model; active participants have
            // separate copied kinematic predictions. Every actual move is later
            // checked against held source poses and all prior accepted swept bodies.
            local = FreezePoses(local, ref consumed);
            if (local is null) return Unavailable(NavigationBlockReason.SearchWork, consumed);
            neighborhoods.Add(entity, local);
        }
        var passageCapture = NavigationPassageDiscovery.Capture(snapshot, CollectionsMarshal.AsSpan(probes), Limits.WorkPerTick - consumed);
        consumed += passageCapture.WorkConsumed;
        if (!passageCapture.Complete) return Unavailable(NavigationBlockReason.SearchWork, consumed);
        var reservations = CaptureServiceReservations(context, ref consumed, out bool reservationsComplete);
        if (!reservationsComplete) return Unavailable(NavigationBlockReason.MissingGeometry, consumed);
        var frame = new NavigationTrafficFrame(context.Tick, physical.Collision, true, observations,
            reservations, Limits, passageCapture.Passages.ToArray(), neighborhoods);
        var directives = _traffic.PlanBatch(frame, intents, _geometry!, Limits.PlanningHorizonSeconds,
            Limits.WorkPerTick - consumed, out int trafficWork);
        consumed += trafficWork;
        return PlanMotion(context, frame, candidates, directives, consumed);
    }

    private PhysicalCapture CapturePhysical(SimulationContext context, NavigationWorldSnapshot snapshot, int allowance)
    {
        var bodies = new List<NavigationCollisionBody>(Math.Min(MaximumBodies, 256));
        bool complete = !_authoredOverflow && _legacyComplete;
        int consumed = 0;
        float maximumSpeed = 0;
        var bounds = snapshot.WorldBounds;
        var coverage = new AxisAlignedBounds(new(bounds.Minimum.X, bounds.Minimum.Y - 1024, bounds.Minimum.Z),
            new(bounds.Maximum.X, bounds.Maximum.Y + 1024, bounds.Maximum.Z));
        foreach (var entity in context.Entities.Query<WorldTransform, SpatialPresence>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!NavigationMotionCapture.Spend(allowance, ref consumed) || bodies.Count == MaximumBodies)
            { complete = false; break; }
            var transform = context.Entities.GetComponent<WorldTransform>(entity);
            var presence = context.Entities.GetComponent<SpatialPresence>(entity);
            // Recapture from current components after legacy movement, never certify an old spatial entry.
            if (!NavigationMotionCapture.TryCaptureBody(context.Entities, presence.CreateEntry(entity, transform), out var body))
            { complete = false; continue; }
            bodies.Add(body);
            if (context.Entities.TryGetComponent(entity, out GroundMovement movement)) maximumSpeed = MathF.Max(maximumSpeed, movement.MaximumSpeed);
            maximumSpeed = MathF.Max(maximumSpeed, body.Velocity.Length());
        }
        for (int i = 0; i < _authored.Length; i++)
        {
            if (!NavigationMotionCapture.Spend(allowance, ref consumed) || bodies.Count == MaximumBodies)
            { complete = false; break; }
            var authored = _authored[i];
            float clearance = snapshot.Grid.Settings.StaticObstacleClearanceMeters;
            var body = new NavigationCollisionBody(new(NavigationObstacleSource.AuthoredGeometry, (ulong)i + 1, snapshot.Revision.Geometry.Value),
                NavigationObstacleKind.Structure, new(new Vector3(authored.Center.X, coverage.Center.Y, authored.Center.Z), Quaternion.Identity),
                new Vector3(MathF.Max(0.001f, authored.Extents.X + clearance), coverage.Extents.Y,
                    MathF.Max(0.001f, authored.Extents.Z + clearance)), Vector3.Zero);
            if (!NavigationMotionCapture.IsValid(body)) { complete = false; continue; }
            bodies.Add(body);
        }
        foreach (var envelope in _legacyEnvelopes)
        {
            if (!NavigationMotionCapture.Spend(allowance, ref consumed) || bodies.Count == MaximumBodies)
            { complete = false; break; }
            bodies.Add(envelope);
        }
        if (!float.IsFinite(maximumSpeed)) complete = false;
        return new(new(snapshot, context.Tick.Value, coverage, complete, CollectionsMarshal.AsSpan(bodies), MaximumBodies),
            maximumSpeed, consumed);
    }

    private NavigationCollisionFrame? CaptureNeighborhood(PhysicalCapture physical,
        in NavigationVehicleObservation vehicle, SimulationTick tick, ref int consumed)
    {
        float radius = MathF.Max(24, vehicle.Profile.MaximumSpeed * Limits.PlanningHorizonSeconds) + vehicle.Profile.HalfExtents.Length() + 2;
        var whole = physical.Collision.Coverage;
        var center = vehicle.Pose.Position;
        var coverage = new AxisAlignedBounds(new(MathF.Max(whole.Minimum.X, center.X - radius), whole.Minimum.Y,
                MathF.Max(whole.Minimum.Z, center.Z - radius)),
            new(MathF.Min(whole.Maximum.X, center.X + radius), whole.Maximum.Y, MathF.Min(whole.Maximum.Z, center.Z + radius)));
        var bodies = new List<NavigationCollisionBody>(MaximumLocalBodies);
        float incoming = physical.MaximumSpeed * Limits.PlanningHorizonSeconds;
        bool complete = physical.Collision.IsComplete;
        foreach (var body in physical.Collision.Bodies)
        {
            if (!NavigationMotionCapture.Spend(Limits.WorkPerTick, ref consumed)) return null;
            Vector3 projected = Vector3.Abs(Vector3.Transform(Vector3.UnitX, body.Pose.Rotation)) * body.HalfExtents.X +
                Vector3.Abs(Vector3.Transform(Vector3.UnitY, body.Pose.Rotation)) * body.HalfExtents.Y +
                Vector3.Abs(Vector3.Transform(Vector3.UnitZ, body.Pose.Rotation)) * body.HalfExtents.Z;
            float reach = new Vector2(projected.X, projected.Z).Length() + incoming;
            if (body.Pose.Position.X + reach < coverage.Minimum.X || body.Pose.Position.X - reach > coverage.Maximum.X ||
                body.Pose.Position.Z + reach < coverage.Minimum.Z || body.Pose.Position.Z - reach > coverage.Maximum.Z) continue;
            if (bodies.Count == MaximumLocalBodies) { complete = false; break; }
            bodies.Add(body);
        }
        return new(_snapshot!, tick.Value, coverage, complete, CollectionsMarshal.AsSpan(bodies), MaximumLocalBodies);
    }

    private bool TryAdaptRoute(EntityId entity, in NavigationRouteState source, in NavigationVehicleProfile profile,
        NavigationWorldSnapshot snapshot, float positionTolerance, ref int consumed, out NavigationRouteProposal proposal)
    {
        proposal = null!;
        if (source.SourceRequest is not { } request || source.Path.Waypoints.Count > MaximumWaypoints || source.Path.Waypoints.Count == 0) return false;
        if (_routes.TryGetValue(entity, out var cached) && ReferenceEquals(cached.Path, source.Path) &&
            ReferenceEquals(cached.Snapshot, snapshot) && cached.Profile == profile && cached.RequestId == request.RequestId &&
            cached.Proposal.Request.Goal.PositionTolerance == positionTolerance)
        { proposal = cached.Proposal; return true; }
        int copyWork = checked(source.Path.Waypoints.Count * 2);
        if (copyWork > Limits.WorkPerTick - consumed) { consumed = Limits.WorkPerTick; return false; }
        consumed += copyWork;
        // The legacy endpoint kernel visits a five-by-five cell square. This adapter does not
        // claim that the old search evaluated a vehicle footprint; current sweeps supply that veto.
        var goal = new NavigationGoalConstraints(snapshot.Grid.Settings.CellSizeMeters * 2.5f * MathF.Sqrt(2),
            positionTolerance, null, 0);
        if (!goal.IsValid) return false;
        var specification = new NavigationSearchSpecification(request.RequestId,
            new NavigationPose(request.Start, source.StartRotation), request.Destination, NavigationTargetSemantics.Transit,
            goal, request.Capabilities, profile, snapshot.Revision, snapshot.KnowledgeScope,
            Limits.WorkPerSearch, Limits.MaximumSearchBytes, Limits.MaximumEndpointCandidates);
        proposal = new(specification, source.Path.Waypoints[^1], source.Path.Waypoints.ToArray(), [], source.Path.Diagnostics, MaximumWaypoints);
        _routes[entity] = new(source.Path, snapshot, profile, request.RequestId, proposal);
        return true;
    }

    private static (float Maximum, bool CanMove) CaptureConstraints(SimulationContext context, EntityId entity, float authoredMaximum)
    {
        var entities = context.Entities;
        float maximum = authoredMaximum;
        bool canMove = true;
        if (entities.TryGetComponent(entity, out SupplyMovementConstraint supply)) { maximum *= supply.MaximumSpeedScale; canMove &= supply.CanMove; }
        if (entities.TryGetComponent(entity, out SuppressionMovementConstraint suppression)) { maximum *= suppression.MaximumSpeedScale; canMove &= suppression.CanMove; }
        if (entities.TryGetComponent(entity, out TacticalMovementConstraint tactical)) canMove &= tactical.CanMove;
        if (entities.TryGetComponent(entity, out FormationMovementConstraint formation)) maximum = MathF.Min(maximum, formation.MaximumSpeed);
        if (entities.TryGetComponent(entity, out CargoConvoyMotionConstraint convoy)) { maximum = MathF.Min(maximum, convoy.MaximumSpeed); canMove &= !convoy.Hold; }
        return (maximum, canMove && float.IsFinite(maximum) && maximum >= 0);
    }

    private NavigationTrafficReservation[] CaptureServiceReservations(SimulationContext context, ref int consumed, out bool complete)
    {
        var reservations = new List<NavigationTrafficReservation>();
        complete = true;
        foreach (var building in context.Entities.Query<VehicleDockSlot>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!NavigationMotionCapture.Spend(Limits.WorkPerTick, ref consumed)) { complete = false; break; }
            var slot = context.Entities.GetComponent<VehicleDockSlot>(building);
            if (!slot.Truck.IsValid || slot.Generation == 0) continue;
            var bounds = slot.Pose.CorridorBounds;
            if (!NavigationMotionCapture.Finite(bounds.Extents) || bounds.Extents.X <= 0 || bounds.Extents.Z <= 0 ||
                reservations.Count == Limits.MaximumReservations) { complete = false; break; }
            reservations.Add(new(0xD000000000000000UL | ((ulong)building.Index + 1), slot.Truck, slot.Generation, 0,
                NavigationReservationKind.Dock, new(bounds.Center, Quaternion.Identity),
                new Vector3(bounds.Extents.X, MathF.Max(1, bounds.Extents.Y), bounds.Extents.Z), SimulationTick.Zero, SimulationTick.Zero, true, false, false));
        }
        foreach (var entity in context.Entities.Query<VehicleQueueEntry>(QueryIterationOrder.StableByEntityIndex))
        {
            if (!NavigationMotionCapture.Spend(Limits.WorkPerTick, ref consumed)) { complete = false; break; }
            var queue = context.Entities.GetComponent<VehicleQueueEntry>(entity);
            if (queue.Generation == 0 || queue.Position <= 0) continue;
            if (!NavigationMotionCapture.TryCaptureVehicle(context.Entities, entity, _movementOptions, out var vehicle) ||
                !NavigationMotionCapture.Finite(queue.Target) || !float.IsFinite(queue.Heading) ||
                reservations.Count == Limits.MaximumReservations) { complete = false; break; }
            reservations.Add(new(0xA000000000000000UL | ((ulong)entity.Index + 1), entity, queue.Generation, 0,
                NavigationReservationKind.Queue, new(queue.Target, Quaternion.CreateFromAxisAngle(Vector3.UnitY, queue.Heading)),
                vehicle.Profile.HalfExtents, queue.RegisteredAtTick, SimulationTick.Zero, queue.ArrivedAtService, true, false, queue.Building, queue.Position));
        }
        return reservations.ToArray();
    }
}
