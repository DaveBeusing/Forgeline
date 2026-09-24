using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class GroundMovementSystem : ISimulationSystem
{
    private const float MinimumDirectionLengthSquared = 0.000001f;
    private const float RadiansToDegrees = 180.0f / MathF.PI;

    private readonly ITerrainQuery? _terrainQuery;
    private readonly SpatialGridIndex? _spatialIndex;
    private readonly GroundMovementSystemOptions _options;
    private readonly SpatialQueryBuffer _neighborBuffer = new(128);
    private readonly SpatialQueryBuffer _obstacleBuffer = new(64);
    private readonly List<EntityId> _completedOrders = new();
    private readonly List<GroundMovementDebugAgent> _debugAgents = new();
    private GroundMovementDebugSnapshot _lastDebugSnapshot =
        GroundMovementDebugSnapshot.Empty;

    public GroundMovementSystem(
        ITerrainQuery? terrainQuery = null,
        SpatialGridIndex? spatialIndex = null,
        GroundMovementSystemOptions? options = null)
    {
        _terrainQuery = terrainQuery;
        _spatialIndex = spatialIndex;
        _options = options ?? new GroundMovementSystemOptions();
        _options.Validate();
    }

    public SimulationPhase Phase => SimulationPhase.Movement;

    public GroundMovementDiagnosticsSnapshot LastDiagnostics { get; private set; }

    public bool DebugCaptureEnabled { get; set; }

    public GroundMovementDebugSnapshot CaptureDebugSnapshot()
    {
        return _lastDebugSnapshot;
    }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        float deltaSeconds = (float)context.TickDuration.TotalSeconds;
        if (deltaSeconds <= 0.0f)
        {
            return;
        }

        _completedOrders.Clear();
        _debugAgents.Clear();

        int groundUnits = 0;
        int orderedUnits = 0;
        int movingUnits = 0;
        int stuckUnits = 0;
        int arrivedUnits = 0;
        int terrainBlockedUnits = 0;
        int neighborAdjustments = 0;
        int obstacleAdjustments = 0;

        foreach (EntityId entity in context.Entities.Query<
                     WorldTransform,
                     GroundMovementState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            if (!context.Entities.TryGetComponent(
                    entity,
                    out GroundMovement movement))
            {
                continue;
            }

            groundUnits++;

            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);
            GroundMovementState state =
                context.Entities.GetComponent<GroundMovementState>(entity);

            if (!context.Entities.TryGetComponent(
                    entity,
                    out MovementOrder order))
            {
                GroundMovementState idleState = state with
                {
                    Velocity = Vector3.Zero,
                    Status = GroundMovementStatus.Idle,
                    ObservedOrderTick = SimulationTick.Zero,
                    PreviousDistanceToTarget = float.PositiveInfinity,
                    StalledTicks = 0
                };

                context.Entities.SetComponent(entity, idleState);
                AddDebugAgent(
                    entity,
                    transform.Position,
                    idleState,
                    movement,
                    default,
                    hasTarget: false);
                continue;
            }

            orderedUnits++;

            ProcessOrderedMovement(
                context.Entities,
                entity,
                movement,
                order,
                transform,
                state,
                deltaSeconds,
                ref movingUnits,
                ref stuckUnits,
                ref arrivedUnits,
                ref terrainBlockedUnits,
                ref neighborAdjustments,
                ref obstacleAdjustments);
        }

        for (int index = 0; index < _completedOrders.Count; index++)
        {
            EntityId entity = _completedOrders[index];

            if (context.Entities.IsAlive(entity) &&
                context.Entities.HasComponent<MovementOrder>(entity))
            {
                context.Entities.RemoveComponent<MovementOrder>(entity);
            }
        }

        LastDiagnostics = new GroundMovementDiagnosticsSnapshot(
            groundUnits,
            orderedUnits,
            movingUnits,
            stuckUnits,
            arrivedUnits,
            terrainBlockedUnits,
            neighborAdjustments,
            obstacleAdjustments);

        _lastDebugSnapshot =
            DebugCaptureEnabled && _debugAgents.Count > 0
                ? new GroundMovementDebugSnapshot(
                    System.Runtime.InteropServices.CollectionsMarshal.AsSpan(
                        _debugAgents))
                : GroundMovementDebugSnapshot.Empty;
    }

    private void ProcessOrderedMovement(
        EntityRegistry entities,
        EntityId entity,
        in GroundMovement movement,
        in MovementOrder order,
        in WorldTransform transform,
        in GroundMovementState state,
        float deltaSeconds,
        ref int movingUnits,
        ref int stuckUnits,
        ref int arrivedUnits,
        ref int terrainBlockedUnits,
        ref int neighborAdjustments,
        ref int obstacleAdjustments)
    {
        Vector3 position = transform.Position;
        Vector3 targetDelta = Horizontal(order.WorldTarget - position);
        float distanceToTarget = targetDelta.Length();

        bool newOrder =
            state.ObservedOrderTick.Value != order.AcceptedAtTick.Value;

        if (distanceToTarget <= movement.StopRadius)
        {
            WorldTransform arrivedTransform = SnapToTerrain(
                transform,
                movement);
            GroundMovementState arrivedState = state with
            {
                Velocity = Vector3.Zero,
                Status = GroundMovementStatus.Arrived,
                ObservedOrderTick = order.AcceptedAtTick,
                PreviousDistanceToTarget = distanceToTarget,
                StalledTicks = 0
            };

            entities.SetComponent(entity, arrivedTransform);
            entities.SetComponent(entity, arrivedState);
            _completedOrders.Add(entity);
            arrivedUnits++;

            AddDebugAgent(
                entity,
                arrivedTransform.Position,
                arrivedState,
                movement,
                order.WorldTarget,
                hasTarget: true);
            return;
        }

        Vector3 targetDirection = NormalizeHorizontal(targetDelta);
        Vector3 steering = targetDirection;

        steering += CalculateSeparationSteering(
            entity,
            position,
            movement,
            ref neighborAdjustments);

        steering += CalculateObstacleSteering(
            entity,
            position,
            targetDirection,
            movement,
            ref obstacleAdjustments);

        Vector3 desiredDirection =
            steering.LengthSquared() > MinimumDirectionLengthSquared
                ? NormalizeHorizontal(steering)
                : targetDirection;

        float desiredHeading = MathF.Atan2(
            desiredDirection.X,
            desiredDirection.Z);
        float heading = MoveTowardsAngle(
            state.HeadingRadians,
            desiredHeading,
            movement.TurnRateRadiansPerSecond * deltaSeconds);

        Vector3 forward = new(
            MathF.Sin(heading),
            0.0f,
            MathF.Cos(heading));

        float currentSpeed = Horizontal(state.Velocity).Length();
        float remainingDistance = MathF.Max(
            0.0f,
            distanceToTarget - movement.StopRadius);
        float brakingSpeed = MathF.Sqrt(
            MathF.Max(
                0.0f,
                2.0f * movement.Deceleration * remainingDistance));
        float desiredSpeed = MathF.Min(
            movement.MaximumSpeed,
            brakingSpeed);

        float speedChange = desiredSpeed >= currentSpeed
            ? movement.Acceleration * deltaSeconds
            : movement.Deceleration * deltaSeconds;
        float speed = MoveTowards(
            currentSpeed,
            desiredSpeed,
            speedChange);

        Vector3 candidate =
            position + forward * (speed * deltaSeconds);

        ResolveMobilePenetration(
            entity,
            ref candidate,
            movement);
        ResolveStaticPenetration(
            entity,
            ref candidate,
            movement);

        bool terrainBlocked = !TryApplyTerrain(
            ref candidate,
            movement);

        if (terrainBlocked)
        {
            candidate = position;
            speed = MoveTowards(
                speed,
                0.0f,
                movement.Deceleration * deltaSeconds);
            terrainBlockedUnits++;
        }

        Vector3 newDelta = Horizontal(order.WorldTarget - candidate);
        float newDistance = newDelta.Length();

        bool arrived =
            newDistance <= movement.StopRadius + 0.01f;

        if (arrived)
        {
            GroundMovementState arrivedState = state with
            {
                Velocity = Vector3.Zero,
                HeadingRadians = heading,
                Status = GroundMovementStatus.Arrived,
                ObservedOrderTick = order.AcceptedAtTick,
                PreviousDistanceToTarget = newDistance,
                StalledTicks = 0
            };

            WorldTransform arrivedTransform = transform with
            {
                Position = candidate,
                Rotation = Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    heading)
            };

            entities.SetComponent(entity, arrivedTransform);
            entities.SetComponent(entity, arrivedState);
            _completedOrders.Add(entity);
            arrivedUnits++;

            AddDebugAgent(
                entity,
                candidate,
                arrivedState,
                movement,
                order.WorldTarget,
                hasTarget: true);
            return;
        }

        float previousDistance = newOrder
            ? distanceToTarget
            : state.PreviousDistanceToTarget;
        float progress = previousDistance - newDistance;
        int stalledTicks =
            progress >= _options.ProgressEpsilonMeters
                ? 0
                : checked(state.StalledTicks + 1);

        GroundMovementStatus status =
            stalledTicks >= _options.StuckTickThreshold
                ? GroundMovementStatus.Stuck
                : GroundMovementStatus.Moving;

        Vector3 actualVelocity =
            (candidate - position) / deltaSeconds;

        var updatedState = new GroundMovementState(
            actualVelocity,
            heading,
            status,
            order.AcceptedAtTick,
            newDistance,
            stalledTicks);

        var updatedTransform = transform with
        {
            Position = candidate,
            Rotation = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                heading)
        };

        entities.SetComponent(entity, updatedTransform);
        entities.SetComponent(entity, updatedState);

        if (status == GroundMovementStatus.Stuck)
        {
            stuckUnits++;
        }
        else
        {
            movingUnits++;
        }

        AddDebugAgent(
            entity,
            candidate,
            updatedState,
            movement,
            order.WorldTarget,
            hasTarget: true);
    }

    private Vector3 CalculateSeparationSteering(
        EntityId entity,
        Vector3 position,
        in GroundMovement movement,
        ref int adjustmentCount)
    {
        if (_spatialIndex is null)
        {
            _neighborBuffer.Clear();
            return Vector3.Zero;
        }

        if (_options.SeparationWeight <= 0.0f)
        {
            _neighborBuffer.Clear();
            return Vector3.Zero;
        }

        _spatialIndex.QueryRadius(
            position,
            movement.SeparationRadius,
            _neighborBuffer,
            new SpatialQueryFilter(Mobility: SpatialMobility.Mobile),
            SpatialQueryOrder.StableEntityId);

        Vector3 steering = Vector3.Zero;
        ReadOnlySpan<EntityId> neighbors = _neighborBuffer.Results;

        for (int index = 0; index < neighbors.Length; index++)
        {
            EntityId neighbor = neighbors[index];
            if (neighbor == entity ||
                !_spatialIndex.TryGetEntry(
                    neighbor,
                    out SpatialEntry entry))
            {
                continue;
            }

            float neighborRadius = HorizontalRadius(entry.Bounds);
            float influenceRadius = MathF.Max(
                movement.SeparationRadius,
                movement.Radius + neighborRadius);

            Vector3 away = Horizontal(position - entry.Position);
            float distanceSquared = away.LengthSquared();
            if (distanceSquared >= influenceRadius * influenceRadius)
            {
                continue;
            }

            float distance = MathF.Sqrt(
                MathF.Max(distanceSquared, 0.0f));

            if (distanceSquared <= MinimumDirectionLengthSquared)
            {
                away = DeterministicSeparationAxis(entity, neighbor);
                distance = 0.0f;
            }
            else
            {
                away /= distance;
            }

            float strength =
                1.0f - Math.Clamp(
                    distance / influenceRadius,
                    0.0f,
                    1.0f);

            steering +=
                away * (strength * _options.SeparationWeight);
            adjustmentCount++;
        }

        return steering;
    }

    private Vector3 CalculateObstacleSteering(
        EntityId entity,
        Vector3 position,
        Vector3 targetDirection,
        in GroundMovement movement,
        ref int adjustmentCount)
    {
        if (_spatialIndex is null ||
            movement.ObstacleLookAhead <= 0.0f ||
            _options.ObstacleSteeringWeight <= 0.0f)
        {
            _obstacleBuffer.Clear();
            return Vector3.Zero;
        }

        float queryRadius =
            movement.ObstacleLookAhead +
            movement.Radius +
            _options.ObstacleClearanceMeters;

        _spatialIndex.QueryRadius(
            position,
            queryRadius,
            _obstacleBuffer,
            new SpatialQueryFilter(Mobility: SpatialMobility.Static),
            SpatialQueryOrder.StableEntityId);

        Vector3 probe =
            position + targetDirection * movement.ObstacleLookAhead;
        Vector3 steering = Vector3.Zero;
        ReadOnlySpan<EntityId> obstacles = _obstacleBuffer.Results;

        for (int index = 0; index < obstacles.Length; index++)
        {
            EntityId obstacle = obstacles[index];
            if (obstacle == entity ||
                !_spatialIndex.TryGetEntry(
                    obstacle,
                    out SpatialEntry entry))
            {
                continue;
            }

            Vector3 closest = ClosestHorizontalPoint(
                probe,
                entry.Bounds);
            Vector3 away = Horizontal(probe - closest);
            float distanceSquared = away.LengthSquared();
            float clearance =
                movement.Radius +
                _options.ObstacleClearanceMeters;

            if (distanceSquared > clearance * clearance)
            {
                continue;
            }

            float distance = MathF.Sqrt(
                MathF.Max(distanceSquared, 0.0f));

            if (distanceSquared <= MinimumDirectionLengthSquared)
            {
                away = Horizontal(position - entry.Bounds.Center);
                if (away.LengthSquared() <= MinimumDirectionLengthSquared)
                {
                    away = PerpendicularAvoidanceAxis(
                        targetDirection,
                        entity);
                }
                else
                {
                    away = Vector3.Normalize(away);
                }
            }
            else
            {
                away /= distance;
            }

            if (Vector3.Dot(away, targetDirection) < -0.5f)
            {
                Vector3 lateral = PerpendicularAvoidanceAxis(
                    targetDirection,
                    entity);
                away = NormalizeHorizontal(away + lateral * 1.5f);
            }

            float strength =
                1.0f - Math.Clamp(
                    distance / MathF.Max(clearance, 0.0001f),
                    0.0f,
                    1.0f);

            steering +=
                away * (strength * _options.ObstacleSteeringWeight);
            adjustmentCount++;
        }

        return steering;
    }

    private void ResolveMobilePenetration(
        EntityId entity,
        ref Vector3 candidate,
        in GroundMovement movement)
    {
        if (_spatialIndex is null)
        {
            return;
        }

        ReadOnlySpan<EntityId> neighbors = _neighborBuffer.Results;

        for (int index = 0; index < neighbors.Length; index++)
        {
            EntityId neighbor = neighbors[index];
            if (neighbor == entity ||
                !_spatialIndex.TryGetEntry(
                    neighbor,
                    out SpatialEntry entry) ||
                entry.Metadata.Mobility != SpatialMobility.Mobile)
            {
                continue;
            }

            float combinedRadius =
                movement.Radius + HorizontalRadius(entry.Bounds);
            Vector3 away = Horizontal(candidate - entry.Position);
            float distanceSquared = away.LengthSquared();

            if (distanceSquared >= combinedRadius * combinedRadius)
            {
                continue;
            }

            float distance = MathF.Sqrt(
                MathF.Max(distanceSquared, 0.0f));

            if (distanceSquared <= MinimumDirectionLengthSquared)
            {
                away = DeterministicSeparationAxis(entity, neighbor);
                distance = 0.0f;
            }
            else
            {
                away /= distance;
            }

            candidate += away * (combinedRadius - distance);
        }
    }

    private void ResolveStaticPenetration(
        EntityId entity,
        ref Vector3 candidate,
        in GroundMovement movement)
    {
        if (_spatialIndex is null)
        {
            return;
        }

        ReadOnlySpan<EntityId> obstacles = _obstacleBuffer.Results;
        float clearance =
            movement.Radius +
            _options.ObstacleClearanceMeters;

        for (int index = 0; index < obstacles.Length; index++)
        {
            EntityId obstacle = obstacles[index];
            if (obstacle == entity ||
                !_spatialIndex.TryGetEntry(
                    obstacle,
                    out SpatialEntry entry) ||
                entry.Metadata.Mobility != SpatialMobility.Static)
            {
                continue;
            }

            AxisAlignedBounds bounds = entry.Bounds;
            bool insideX =
                candidate.X >= bounds.Minimum.X &&
                candidate.X <= bounds.Maximum.X;
            bool insideZ =
                candidate.Z >= bounds.Minimum.Z &&
                candidate.Z <= bounds.Maximum.Z;

            if (insideX && insideZ)
            {
                PushOutsideBounds(
                    ref candidate,
                    bounds,
                    clearance);
                continue;
            }

            Vector3 closest = ClosestHorizontalPoint(
                candidate,
                bounds);
            Vector3 away = Horizontal(candidate - closest);
            float distanceSquared = away.LengthSquared();

            if (distanceSquared >= clearance * clearance)
            {
                continue;
            }

            float distance = MathF.Sqrt(
                MathF.Max(distanceSquared, 0.0f));
            if (distanceSquared <= MinimumDirectionLengthSquared)
            {
                away = DeterministicSeparationAxis(entity, obstacle);
                distance = 0.0f;
            }
            else
            {
                away /= distance;
            }

            candidate += away * (clearance - distance);
        }
    }

    private bool TryApplyTerrain(
        ref Vector3 candidate,
        in GroundMovement movement)
    {
        if (_terrainQuery is null)
        {
            return true;
        }

        if (!_terrainQuery.TrySampleHeight(
                candidate.X,
                candidate.Z,
                out float height) ||
            !_terrainQuery.TrySampleNormal(
                candidate.X,
                candidate.Z,
                out Vector3 normal))
        {
            return false;
        }

        float slopeDegrees =
            MathF.Acos(Math.Clamp(normal.Y, -1.0f, 1.0f)) *
            RadiansToDegrees;

        if (slopeDegrees > movement.MaximumSlopeDegrees)
        {
            return false;
        }

        candidate.Y = height + movement.HeightOffset;
        return true;
    }

    private WorldTransform SnapToTerrain(
        in WorldTransform transform,
        in GroundMovement movement)
    {
        if (_terrainQuery is null ||
            !_terrainQuery.TrySampleHeight(
                transform.Position.X,
                transform.Position.Z,
                out float height))
        {
            return transform;
        }

        return transform with
        {
            Position = new Vector3(
                transform.Position.X,
                height + movement.HeightOffset,
                transform.Position.Z)
        };
    }

    private void AddDebugAgent(
        EntityId entity,
        Vector3 position,
        in GroundMovementState state,
        in GroundMovement movement,
        Vector3 target,
        bool hasTarget)
    {
        if (!DebugCaptureEnabled)
        {
            return;
        }

        _debugAgents.Add(
            new GroundMovementDebugAgent(
                entity,
                position,
                state.Velocity,
                target,
                movement.Radius,
                movement.SeparationRadius,
                state.Status,
                hasTarget));
    }

    private static Vector3 ClosestHorizontalPoint(
        Vector3 point,
        in AxisAlignedBounds bounds)
    {
        return new Vector3(
            Math.Clamp(point.X, bounds.Minimum.X, bounds.Maximum.X),
            point.Y,
            Math.Clamp(point.Z, bounds.Minimum.Z, bounds.Maximum.Z));
    }

    private static void PushOutsideBounds(
        ref Vector3 point,
        in AxisAlignedBounds bounds,
        float clearance)
    {
        float toMinimumX = point.X - bounds.Minimum.X;
        float toMaximumX = bounds.Maximum.X - point.X;
        float toMinimumZ = point.Z - bounds.Minimum.Z;
        float toMaximumZ = bounds.Maximum.Z - point.Z;

        float nearest = MathF.Min(
            MathF.Min(toMinimumX, toMaximumX),
            MathF.Min(toMinimumZ, toMaximumZ));

        if (nearest == toMinimumX)
        {
            point.X = bounds.Minimum.X - clearance;
        }
        else if (nearest == toMaximumX)
        {
            point.X = bounds.Maximum.X + clearance;
        }
        else if (nearest == toMinimumZ)
        {
            point.Z = bounds.Minimum.Z - clearance;
        }
        else
        {
            point.Z = bounds.Maximum.Z + clearance;
        }
    }

    private static float HorizontalRadius(
        in AxisAlignedBounds bounds)
    {
        return MathF.Max(
            bounds.Extents.X,
            bounds.Extents.Z);
    }

    private static Vector3 DeterministicSeparationAxis(
        EntityId left,
        EntityId right)
    {
        return left < right
            ? -Vector3.UnitX
            : Vector3.UnitX;
    }

    private static Vector3 PerpendicularAvoidanceAxis(
        Vector3 direction,
        EntityId entity)
    {
        Vector3 lateral = new(
            direction.Z,
            0.0f,
            -direction.X);

        if ((entity.Index & 1U) != 0)
        {
            lateral = -lateral;
        }

        return NormalizeHorizontal(lateral);
    }

    private static Vector3 NormalizeHorizontal(Vector3 value)
    {
        Vector3 horizontal = Horizontal(value);
        float lengthSquared = horizontal.LengthSquared();

        return lengthSquared <= MinimumDirectionLengthSquared
            ? Vector3.Zero
            : horizontal / MathF.Sqrt(lengthSquared);
    }

    private static Vector3 Horizontal(Vector3 value)
    {
        return new Vector3(value.X, 0.0f, value.Z);
    }

    private static float MoveTowards(
        float current,
        float target,
        float maximumDelta)
    {
        if (MathF.Abs(target - current) <= maximumDelta)
        {
            return target;
        }

        return current +
               MathF.CopySign(maximumDelta, target - current);
    }

    private static float MoveTowardsAngle(
        float current,
        float target,
        float maximumDelta)
    {
        float delta = MathF.IEEERemainder(
            target - current,
            MathF.Tau);
        float next = MathF.Abs(delta) <= maximumDelta
            ? target
            : current + MathF.CopySign(maximumDelta, delta);

        return GroundMovementState.NormalizeAngle(next);
    }
}
