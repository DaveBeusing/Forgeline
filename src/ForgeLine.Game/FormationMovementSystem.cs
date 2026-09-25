using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class FormationMovementSystem : ISimulationSystem
{
    private const float MinimumDirectionLengthSquared = 0.000001f;
    private const float SlotTargetUpdateDistanceSquared = 0.25f;

    private readonly HierarchicalPathfinder _pathfinder;
    private readonly FormationMovementSystemOptions _options;
    private readonly ConcurrentQueue<NavigationPathResult> _completed = new();
    private readonly List<EntityId> _groups = new();
    private readonly List<EntityId> _invalidMembers = new();
    private readonly List<EntityId> _groupsToDestroy = new();
    private readonly Dictionary<EntityId, List<MemberRuntime>> _membersByGroup = new();
    private readonly List<FormationMovementDebugGroup> _debugGroups = new();
    private readonly List<FormationMovementDebugSlot> _debugSlots = new();
    private readonly List<FormationMovementDebugRoutePoint> _debugRoutePoints = new();

    private Vector2[] _localSlotBuffer = [];
    private Vector3[] _worldSlotBuffer = [];
    private bool[] _claimedSlotBuffer = [];

    private ulong _nextRequestId = 1;
    private ulong _sharedPathRequestCount;
    private ulong _completedGroupCount;
    private ulong _failedGroupCount;
    private ulong _slotReassignmentCount;
    private ulong _compressionEventCount;
    private ulong _splitEventCount;
    private ulong _blockedSlotProjectionCount;

    private int _activeGroupCount;
    private int _activeMemberCount;
    private int _largestGroupSize;
    private int _compressedGroupCount;

    private FormationMovementDebugSnapshot _lastDebugSnapshot =
        FormationMovementDebugSnapshot.Empty;

    public FormationMovementSystem(
        HierarchicalPathfinder pathfinder,
        FormationMovementSystemOptions? options = null)
    {
        _pathfinder = pathfinder ??
            throw new ArgumentNullException(nameof(pathfinder));
        _options = options ?? new FormationMovementSystemOptions();
        _options.Validate();
    }

    public SimulationPhase Phase => SimulationPhase.NavigationRequests;

    public NavigationWorld World => _pathfinder.World;

    public FormationMovementDiagnosticsSnapshot LastDiagnostics { get; private set; }

    public NavigationPath? LastCompletedPath { get; private set; }

    public bool DebugCaptureEnabled { get; set; }

    public FormationMovementDebugSnapshot CaptureDebugSnapshot() =>
        _lastDebugSnapshot;

    public void UpdateWorld(NavigationWorld world)
    {
        _pathfinder.UpdateWorld(world);
    }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ApplyCompletedResults(context);
        GatherGroupsAndMembers(context);

        _groupsToDestroy.Clear();
        _activeGroupCount = 0;
        _activeMemberCount = 0;
        _largestGroupSize = 0;
        _compressedGroupCount = 0;
        _debugGroups.Clear();
        _debugSlots.Clear();
        _debugRoutePoints.Clear();

        for (int index = 0; index < _groups.Count; index++)
        {
            EntityId group = _groups[index];

            if (!context.Entities.IsAlive(group) ||
                !context.Entities.TryGetComponent(
                    group,
                    out MovementGroupOrder order) ||
                !context.Entities.TryGetComponent(
                    group,
                    out MovementGroupState state))
            {
                continue;
            }

            _membersByGroup.TryGetValue(
                group,
                out List<MemberRuntime>? members);

            if (members is null || members.Count == 0)
            {
                _groupsToDestroy.Add(group);
                continue;
            }

            ProcessGroup(
                context,
                group,
                order,
                state,
                members);
        }

        CleanupInvalidMembers(context);

        for (int index = 0; index < _groupsToDestroy.Count; index++)
        {
            EntityId group = _groupsToDestroy[index];
            if (context.Entities.IsAlive(group))
            {
                context.Entities.DestroyEntity(group);
            }
        }

        LastDiagnostics = new FormationMovementDiagnosticsSnapshot(
            _activeGroupCount,
            _activeMemberCount,
            _largestGroupSize,
            _compressedGroupCount,
            _sharedPathRequestCount,
            _completedGroupCount,
            _failedGroupCount,
            _slotReassignmentCount,
            _compressionEventCount,
            _splitEventCount,
            _blockedSlotProjectionCount);

        _lastDebugSnapshot =
            DebugCaptureEnabled && _debugGroups.Count > 0
                ? new FormationMovementDebugSnapshot(
                    CollectionsMarshal.AsSpan(_debugGroups),
                    CollectionsMarshal.AsSpan(_debugSlots),
                    CollectionsMarshal.AsSpan(_debugRoutePoints))
                : FormationMovementDebugSnapshot.Empty;
    }

    private void GatherGroupsAndMembers(SimulationContext context)
    {
        _groups.Clear();
        _invalidMembers.Clear();

        foreach (List<MemberRuntime> members in _membersByGroup.Values)
        {
            members.Clear();
        }

        foreach (EntityId group in context.Entities.Query<
                     MovementGroupOrder,
                     MovementGroupState>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            _groups.Add(group);
        }

        foreach (EntityId entity in context.Entities.Query<
                     MovementGroupMember,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            MovementGroupMember membership =
                context.Entities.GetComponent<MovementGroupMember>(entity);

            if (!context.Entities.IsAlive(membership.Group) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out GroundMovement movement) ||
                !context.Entities.TryGetComponent(
                    entity,
                    out NavigationAgent navigationAgent))
            {
                _invalidMembers.Add(entity);
                continue;
            }

            if (!_membersByGroup.TryGetValue(
                    membership.Group,
                    out List<MemberRuntime>? members))
            {
                members = new List<MemberRuntime>();
                _membersByGroup.Add(membership.Group, members);
            }

            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);

            members.Add(
                new MemberRuntime(
                    entity,
                    transform.Position,
                    movement,
                    navigationAgent,
                    membership.SlotIndex));
        }
    }

    private void ProcessGroup(
        SimulationContext context,
        EntityId group,
        in MovementGroupOrder order,
        in MovementGroupState previousState,
        List<MemberRuntime> members)
    {
        GroupMetrics metrics = CalculateGroupMetrics(members);

        _activeGroupCount++;
        _activeMemberCount += members.Count;
        _largestGroupSize = Math.Max(
            _largestGroupSize,
            members.Count);

        bool hasRoute =
            context.Entities.TryGetComponent(
                group,
                out MovementGroupRoute route);
        bool hasPending =
            context.Entities.TryGetComponent(
                group,
                out MovementGroupPendingPath pending);

        if (hasRoute &&
            route.Path.Version != World.Version)
        {
            context.Entities.RemoveComponent<MovementGroupRoute>(group);
            hasRoute = false;
        }

        if (hasPending &&
            pending.Request.NavigationVersion != World.Version)
        {
            context.Entities.RemoveComponent<MovementGroupPendingPath>(group);
            hasPending = false;
        }

        if (!hasRoute)
        {
            ClearMemberLocalOrdersAndConstraints(
                context,
                members);

            MovementGroupState waitingState = previousState with
            {
                Status = MovementGroupStatus.AwaitingRoute,
                Centroid = metrics.Centroid,
                BoundsMinimum = metrics.BoundsMinimum,
                BoundsMaximum = metrics.BoundsMaximum,
                Forward = ResolveForward(
                    previousState.Forward,
                    order.Destination - metrics.Centroid),
                MemberCount = members.Count,
                RepresentativeRadius = metrics.MaximumRadius,
                EffectiveSpeed = metrics.MinimumSpeed,
                CompressionScale = 1.0f,
                SplitCohortCount = 1,
                UpdatedAtTick = context.Tick
            };
            context.Entities.SetComponent(group, waitingState);

            bool failedForCurrentWorld =
                previousState.Status == MovementGroupStatus.Failed &&
                context.Entities.TryGetComponent(
                    group,
                    out MovementGroupFailureState failure) &&
                failure.NavigationVersion == World.Version;

            if (!hasPending && !failedForCurrentWorld)
            {
                ScheduleSharedRoute(
                    context,
                    group,
                    order,
                    metrics,
                    members);
            }

            AddPendingDebugGroup(
                group,
                order,
                waitingState);
            return;
        }

        if (route.Path.Waypoints.Count == 0)
        {
            FailGroup(
                context,
                group,
                NavigationFailureReason.NoLocalRoute);
            return;
        }

        int waypointIndex = Math.Clamp(
            route.NextWaypointIndex,
            0,
            route.Path.Waypoints.Count - 1);

        waypointIndex = AdvanceWaypoint(
            route.Path,
            waypointIndex,
            metrics.Centroid,
            metrics.MaximumRadius);

        if (waypointIndex != route.NextWaypointIndex)
        {
            route = route with
            {
                NextWaypointIndex = waypointIndex
            };
            context.Entities.SetComponent(group, route);
        }

        Vector3 activeWaypoint = route.Path.Waypoints[waypointIndex];
        Vector3 forward = ResolveForward(
            previousState.Forward,
            activeWaypoint - metrics.Centroid);
        Vector3 right = new(forward.Z, 0.0f, -forward.X);

        float spacing =
            metrics.MaximumRadius * 2.0f +
            _options.SpacingMarginMeters;

        EnsureSlotCapacity(members.Count);
        GenerateLocalSlots(
            order.Formation,
            members.Count,
            spacing,
            splitCohortCount: 1);

        float desiredWidth =
            CalculateDesiredWidth(
                members.Count,
                metrics.MaximumRadius);
        float corridorWidth =
            EstimateCorridorWidth(
                activeWaypoint,
                forward,
                metrics.RepresentativeCapabilities);

        float compressionScale = 1.0f;
        int splitCohortCount = 1;

        if (float.IsFinite(corridorWidth) &&
            corridorWidth < desiredWidth)
        {
            float usableWidth =
                MathF.Max(
                    corridorWidth - metrics.MaximumRadius * 2.0f,
                    0.0f);
            float formationSpan =
                MathF.Max(
                    desiredWidth - metrics.MaximumRadius * 2.0f,
                    0.001f);
            float rawScale = usableWidth / formationSpan;

            if (rawScale < _options.MinimumCompressionScale &&
                members.Count > _options.SplitCohortSize)
            {
                splitCohortCount =
                    (members.Count + _options.SplitCohortSize - 1) /
                    _options.SplitCohortSize;
                compressionScale = 1.0f;
                GenerateLocalSlots(
                    FormationTemplate.Column,
                    members.Count,
                    spacing,
                    splitCohortCount);
            }
            else
            {
                compressionScale = Math.Clamp(
                    rawScale,
                    _options.MinimumCompressionScale,
                    1.0f);
                ApplyLateralCompression(
                    members.Count,
                    compressionScale);
            }
        }

        if (compressionScale < 0.999f)
        {
            _compressedGroupCount++;
            if (previousState.CompressionScale >= 0.999f)
            {
                _compressionEventCount++;
            }
        }

        if (splitCohortCount > 1 &&
            previousState.SplitCohortCount <= 1)
        {
            _splitEventCount++;
        }

        BuildWorldSlots(
            members.Count,
            activeWaypoint,
            forward,
            right,
            metrics.RepresentativeCapabilities);

        EnsureStableSlotAssignments(
            context,
            group,
            members,
            metrics.Centroid,
            forward,
            right);

        float maximumSlotError = CalculateMaximumSlotError(
            members);
        float effectiveSpeed =
            maximumSlotError >
            spacing * _options.StretchSlowdownDistanceMultiplier
                ? metrics.MinimumSpeed * _options.StretchedSpeedFactor
                : metrics.MinimumSpeed;

        bool finalWaypoint =
            waypointIndex >= route.Path.Waypoints.Count - 1;
        bool allArrived = finalWaypoint;

        for (int index = 0; index < members.Count; index++)
        {
            MemberRuntime member = members[index];
            int slotIndex = member.SlotIndex;

            if ((uint)slotIndex >= (uint)members.Count)
            {
                allArrived = false;
                continue;
            }

            Vector3 slotTarget = _worldSlotBuffer[slotIndex];
            float distance =
                HorizontalDistance(
                    member.Position,
                    slotTarget);
            float arrivalTolerance = MathF.Max(
                _options.ArrivalToleranceMeters,
                member.Movement.StopRadius + 0.25f);

            if (distance > arrivalTolerance)
            {
                allArrived = false;
            }

            SetFormationConstraint(
                context,
                member.Entity,
                effectiveSpeed);
            SetFormationLocalOrder(
                context,
                member.Entity,
                order,
                slotTarget);

            if (DebugCaptureEnabled)
            {
                _debugSlots.Add(
                    new FormationMovementDebugSlot(
                        group,
                        member.Entity,
                        slotIndex,
                        member.Position,
                        slotTarget));
            }
        }

        var state = new MovementGroupState(
            MovementGroupStatus.Moving,
            metrics.Centroid,
            metrics.BoundsMinimum,
            metrics.BoundsMaximum,
            forward,
            members.Count,
            metrics.MaximumRadius,
            effectiveSpeed,
            compressionScale,
            splitCohortCount,
            context.Tick);

        context.Entities.SetComponent(group, state);

        if (DebugCaptureEnabled)
        {
            AddDebugGroup(
                group,
                order,
                state,
                activeWaypoint,
                route.Path);
        }

        if (allArrived)
        {
            CompleteGroup(
                context,
                group,
                members);
        }
    }

    private void ScheduleSharedRoute(
        SimulationContext context,
        EntityId group,
        in MovementGroupOrder order,
        in GroupMetrics metrics,
        IReadOnlyList<MemberRuntime> members)
    {
        Vector3 start = ResolveRouteStart(
            metrics.Centroid,
            metrics.RepresentativeCapabilities,
            members);

        ulong requestId = _nextRequestId++;
        if (requestId == 0)
        {
            throw new OverflowException(
                "Formation navigation request identifier space has been exhausted.");
        }

        var request = new NavigationPathRequest(
            requestId,
            group,
            start,
            order.Destination,
            metrics.RepresentativeCapabilities,
            World.Version);

        context.Entities.AddComponent(
            group,
            new MovementGroupPendingPath(request));

        _sharedPathRequestCount++;

        if (context.Jobs.IsAvailable)
        {
            context.Jobs.Schedule(
                cancellationToken =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _completed.Enqueue(
                            new NavigationPathResult(
                                request,
                                new NavigationSearchResult(
                                    request.NavigationVersion,
                                    NavigationFailureReason.Canceled,
                                    Path: null),
                                TimeSpan.Zero));
                        return;
                    }

                    _completed.Enqueue(Compute(request));
                });
        }
        else
        {
            _completed.Enqueue(Compute(request));
        }
    }

    private NavigationPathResult Compute(
        in NavigationPathRequest request)
    {
        long started = Stopwatch.GetTimestamp();
        NavigationSearchResult search;

        if (request.NavigationVersion != World.Version)
        {
            search = new NavigationSearchResult(
                request.NavigationVersion,
                NavigationFailureReason.StaleNavigationVersion,
                Path: null);
        }
        else
        {
            search = _pathfinder.FindPath(
                request.Start,
                request.Destination,
                request.Capabilities);
        }

        return new NavigationPathResult(
            request,
            search,
            Stopwatch.GetElapsedTime(started));
    }

    private void ApplyCompletedResults(SimulationContext context)
    {
        while (_completed.TryDequeue(
                   out NavigationPathResult result))
        {
            EntityId group = result.Request.Requester;

            if (!context.Entities.IsAlive(group) ||
                !context.Entities.TryGetComponent(
                    group,
                    out MovementGroupPendingPath pending) ||
                pending.Request.RequestId != result.Request.RequestId)
            {
                continue;
            }

            context.Entities.RemoveComponent<MovementGroupPendingPath>(group);

            if (result.Request.NavigationVersion != World.Version ||
                result.Search.Version != World.Version)
            {
                if (context.Entities.TryGetComponent(
                        group,
                        out MovementGroupState staleState))
                {
                    context.Entities.SetComponent(
                        group,
                        staleState with
                        {
                            Status = MovementGroupStatus.AwaitingRoute
                        });
                }

                continue;
            }

            if (!result.Succeeded ||
                result.Search.Path is null)
            {
                FailGroup(
                    context,
                    group,
                    result.Search.FailureReason);
                continue;
            }

            LastCompletedPath = result.Search.Path;

            context.Entities.AddComponent(
                group,
                new MovementGroupRoute(
                    result.Search.Path,
                    NextWaypointIndex: 0));

            if (context.Entities.TryGetComponent(
                    group,
                    out MovementGroupState state))
            {
                context.Entities.SetComponent(
                    group,
                    state with
                    {
                        Status = MovementGroupStatus.Moving
                    });
            }

            if (context.Entities.HasComponent<
                    MovementGroupFailureState>(group))
            {
                context.Entities.RemoveComponent<
                    MovementGroupFailureState>(group);
            }
        }
    }

    private void FailGroup(
        SimulationContext context,
        EntityId group,
        NavigationFailureReason reason)
    {
        _failedGroupCount++;

        if (context.Entities.TryGetComponent(
                group,
                out MovementGroupState state))
        {
            context.Entities.SetComponent(
                group,
                state with
                {
                    Status = MovementGroupStatus.Failed,
                    UpdatedAtTick = context.Tick
                });
        }

        var failure = new MovementGroupFailureState(
            reason,
            World.Version,
            context.Tick);

        if (context.Entities.HasComponent<
                MovementGroupFailureState>(group))
        {
            context.Entities.SetComponent(group, failure);
        }
        else
        {
            context.Entities.AddComponent(group, failure);
        }
    }

    private void CompleteGroup(
        SimulationContext context,
        EntityId group,
        IReadOnlyList<MemberRuntime> members)
    {
        for (int index = 0; index < members.Count; index++)
        {
            EntityId entity = members[index].Entity;

            if (!context.Entities.IsAlive(entity))
            {
                continue;
            }

            if (context.Entities.HasComponent<MovementGroupMember>(entity))
            {
                context.Entities.RemoveComponent<MovementGroupMember>(entity);
            }

            if (context.Entities.HasComponent<FormationMovementConstraint>(entity))
            {
                context.Entities.RemoveComponent<FormationMovementConstraint>(entity);
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out MovementOrder localOrder) &&
                localOrder.Kind == MovementOrderKind.FormationLocal)
            {
                context.Entities.RemoveComponent<MovementOrder>(entity);
            }
        }

        _completedGroupCount++;
        _groupsToDestroy.Add(group);
    }

    private static void ClearMemberLocalOrdersAndConstraints(
        SimulationContext context,
        IReadOnlyList<MemberRuntime> members)
    {
        for (int index = 0; index < members.Count; index++)
        {
            EntityId entity = members[index].Entity;

            if (!context.Entities.IsAlive(entity))
            {
                continue;
            }

            if (context.Entities.HasComponent<FormationMovementConstraint>(entity))
            {
                context.Entities.RemoveComponent<FormationMovementConstraint>(entity);
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out MovementOrder order) &&
                order.Kind == MovementOrderKind.FormationLocal)
            {
                context.Entities.RemoveComponent<MovementOrder>(entity);
            }
        }
    }

    private void CleanupInvalidMembers(SimulationContext context)
    {
        for (int index = 0; index < _invalidMembers.Count; index++)
        {
            EntityId entity = _invalidMembers[index];

            if (!context.Entities.IsAlive(entity))
            {
                continue;
            }

            if (context.Entities.HasComponent<MovementGroupMember>(entity))
            {
                context.Entities.RemoveComponent<MovementGroupMember>(entity);
            }

            if (context.Entities.HasComponent<FormationMovementConstraint>(entity))
            {
                context.Entities.RemoveComponent<FormationMovementConstraint>(entity);
            }

            if (context.Entities.TryGetComponent(
                    entity,
                    out MovementOrder order) &&
                order.Kind == MovementOrderKind.FormationLocal)
            {
                context.Entities.RemoveComponent<MovementOrder>(entity);
            }
        }
    }

    private static GroupMetrics CalculateGroupMetrics(
        IReadOnlyList<MemberRuntime> members)
    {
        Vector3 sum = Vector3.Zero;
        Vector3 minimum = new(
            float.PositiveInfinity,
            float.PositiveInfinity,
            float.PositiveInfinity);
        Vector3 maximum = new(
            float.NegativeInfinity,
            float.NegativeInfinity,
            float.NegativeInfinity);
        float maximumRadius = 0.0f;
        float minimumSpeed = float.PositiveInfinity;
        NavigationCapabilities representative =
            members[0].NavigationAgent.Capabilities;

        for (int index = 0; index < members.Count; index++)
        {
            MemberRuntime member = members[index];
            sum += member.Position;
            minimum = Vector3.Min(minimum, member.Position);
            maximum = Vector3.Max(maximum, member.Position);
            maximumRadius = MathF.Max(
                maximumRadius,
                member.Movement.Radius);
            minimumSpeed = MathF.Min(
                minimumSpeed,
                member.Movement.MaximumSpeed);

            NavigationCapabilities candidate =
                member.NavigationAgent.Capabilities;
            if (candidate.MaximumSlopeDegrees <
                representative.MaximumSlopeDegrees)
            {
                representative = candidate;
            }
        }

        return new GroupMetrics(
            sum / members.Count,
            minimum,
            maximum,
            maximumRadius,
            minimumSpeed,
            representative);
    }

    private int AdvanceWaypoint(
        NavigationPath path,
        int waypointIndex,
        Vector3 centroid,
        float maximumRadius)
    {
        float radius = MathF.Max(
            _options.WaypointRadiusMeters,
            maximumRadius * 2.0f);

        while (waypointIndex < path.Waypoints.Count - 1 &&
               HorizontalDistance(
                   centroid,
                   path.Waypoints[waypointIndex]) <= radius)
        {
            waypointIndex++;
        }

        return waypointIndex;
    }

    private Vector3 ResolveRouteStart(
        Vector3 centroid,
        in NavigationCapabilities capabilities,
        IReadOnlyList<MemberRuntime> members)
    {
        NavigationGrid grid = World.Grid;

        if (IsTraversableWorldPosition(
                grid,
                centroid,
                capabilities))
        {
            return centroid;
        }

        float bestDistanceSquared = float.PositiveInfinity;
        Vector3 best = members[0].Position;

        for (int index = 0; index < members.Count; index++)
        {
            Vector3 position = members[index].Position;
            if (!IsTraversableWorldPosition(
                    grid,
                    position,
                    capabilities))
            {
                continue;
            }

            Vector2 delta = new(
                position.X - centroid.X,
                position.Z - centroid.Z);
            float distanceSquared = delta.LengthSquared();

            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = position;
            }
        }

        return best;
    }

    private float EstimateCorridorWidth(
        Vector3 waypoint,
        Vector3 forward,
        in NavigationCapabilities capabilities)
    {
        NavigationGrid grid = World.Grid;
        if (!grid.TryWorldToCell(
                waypoint,
                out NavigationCellCoordinate center) ||
            !grid.IsTraversable(center, capabilities))
        {
            return grid.Settings.CellSizeMeters;
        }

        bool scanX = MathF.Abs(forward.Z) >= MathF.Abs(forward.X);
        int openNegative = 0;
        int openPositive = 0;
        bool negativeBlocked = false;
        bool positiveBlocked = false;

        for (int distance = 1;
             distance <= _options.CorridorScanCells;
             distance++)
        {
            NavigationCellCoordinate negative = scanX
                ? new NavigationCellCoordinate(
                    center.X - distance,
                    center.Z)
                : new NavigationCellCoordinate(
                    center.X,
                    center.Z - distance);
            NavigationCellCoordinate positive = scanX
                ? new NavigationCellCoordinate(
                    center.X + distance,
                    center.Z)
                : new NavigationCellCoordinate(
                    center.X,
                    center.Z + distance);

            if (!negativeBlocked)
            {
                if (grid.IsTraversable(negative, capabilities))
                {
                    openNegative++;
                }
                else
                {
                    negativeBlocked = true;
                }
            }

            if (!positiveBlocked)
            {
                if (grid.IsTraversable(positive, capabilities))
                {
                    openPositive++;
                }
                else
                {
                    positiveBlocked = true;
                }
            }
        }

        if (!negativeBlocked || !positiveBlocked)
        {
            return float.PositiveInfinity;
        }

        int openCells = 1 + openNegative + openPositive;
        return openCells * grid.Settings.CellSizeMeters;
    }

    private void GenerateLocalSlots(
        FormationTemplate formation,
        int count,
        float spacing,
        int splitCohortCount)
    {
        FormationTemplate effectiveFormation =
            splitCohortCount > 1
                ? FormationTemplate.Column
                : formation;

        switch (effectiveFormation)
        {
            case FormationTemplate.Line:
                GenerateLineSlots(count, spacing);
                break;
            case FormationTemplate.Column:
                GenerateColumnSlots(
                    count,
                    spacing,
                    splitCohortCount);
                break;
            case FormationTemplate.Wedge:
                GenerateWedgeSlots(count, spacing);
                break;
            case FormationTemplate.Compact:
                GenerateCompactSlots(count, spacing);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(formation),
                    formation,
                    null);
        }

        RecenterSlots(count);
    }

    private void GenerateLineSlots(int count, float spacing)
    {
        float center = (count - 1) * 0.5f;

        for (int index = 0; index < count; index++)
        {
            _localSlotBuffer[index] =
                new Vector2(
                    (index - center) * spacing,
                    0.0f);
        }
    }

    private void GenerateColumnSlots(
        int count,
        float spacing,
        int splitCohortCount)
    {
        float gap =
            splitCohortCount > 1
                ? spacing * _options.SplitCohortGapMultiplier
                : 0.0f;

        for (int index = 0; index < count; index++)
        {
            int cohort = index / _options.SplitCohortSize;
            _localSlotBuffer[index] =
                new Vector2(
                    0.0f,
                    -(index * spacing + cohort * gap));
        }
    }

    private void GenerateWedgeSlots(int count, float spacing)
    {
        if (count == 0)
        {
            return;
        }

        _localSlotBuffer[0] = new Vector2(0.0f, spacing);

        for (int index = 1; index < count; index++)
        {
            int pairIndex = index - 1;
            int row = pairIndex / 2 + 1;
            float side = (pairIndex & 1) == 0 ? -1.0f : 1.0f;

            _localSlotBuffer[index] =
                new Vector2(
                    side * row * spacing,
                    -row * spacing);
        }
    }

    private void GenerateCompactSlots(int count, float spacing)
    {
        int columns = checked((int)MathF.Ceiling(MathF.Sqrt(count)));

        for (int index = 0; index < count; index++)
        {
            int column = index % columns;
            int row = index / columns;

            _localSlotBuffer[index] =
                new Vector2(
                    column * spacing,
                    -row * spacing);
        }
    }

    private void RecenterSlots(int count)
    {
        if (count == 0)
        {
            return;
        }

        float minimumX = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity;
        float minimumZ = float.PositiveInfinity;
        float maximumZ = float.NegativeInfinity;

        for (int index = 0; index < count; index++)
        {
            Vector2 slot = _localSlotBuffer[index];
            minimumX = MathF.Min(minimumX, slot.X);
            maximumX = MathF.Max(maximumX, slot.X);
            minimumZ = MathF.Min(minimumZ, slot.Y);
            maximumZ = MathF.Max(maximumZ, slot.Y);
        }

        Vector2 center = new(
            (minimumX + maximumX) * 0.5f,
            (minimumZ + maximumZ) * 0.5f);

        for (int index = 0; index < count; index++)
        {
            _localSlotBuffer[index] -= center;
        }
    }

    private float CalculateDesiredWidth(
        int count,
        float maximumRadius)
    {
        if (count == 0)
        {
            return 0.0f;
        }

        float minimumX = float.PositiveInfinity;
        float maximumX = float.NegativeInfinity;

        for (int index = 0; index < count; index++)
        {
            float x = _localSlotBuffer[index].X;
            minimumX = MathF.Min(minimumX, x);
            maximumX = MathF.Max(maximumX, x);
        }

        return maximumX - minimumX + maximumRadius * 2.0f;
    }

    private void ApplyLateralCompression(
        int count,
        float scale)
    {
        for (int index = 0; index < count; index++)
        {
            Vector2 slot = _localSlotBuffer[index];
            _localSlotBuffer[index] =
                new Vector2(slot.X * scale, slot.Y);
        }
    }

    private void BuildWorldSlots(
        int count,
        Vector3 anchor,
        Vector3 forward,
        Vector3 right,
        in NavigationCapabilities capabilities)
    {
        for (int index = 0; index < count; index++)
        {
            Vector2 local = _localSlotBuffer[index];
            Vector3 target =
                anchor +
                right * local.X +
                forward * local.Y;

            _worldSlotBuffer[index] =
                ProjectSlotToTraversable(
                    target,
                    anchor,
                    capabilities);
        }
    }

    private Vector3 ProjectSlotToTraversable(
        Vector3 target,
        Vector3 anchor,
        in NavigationCapabilities capabilities)
    {
        NavigationGrid grid = World.Grid;

        if (IsTraversableWorldPosition(
                grid,
                target,
                capabilities))
        {
            return target;
        }

        _blockedSlotProjectionCount++;

        const int projectionSteps = 6;
        for (int step = 1; step <= projectionSteps; step++)
        {
            float amount = step / (float)projectionSteps;
            Vector3 candidate = Vector3.Lerp(
                target,
                anchor,
                amount);

            if (IsTraversableWorldPosition(
                    grid,
                    candidate,
                    capabilities))
            {
                return candidate;
            }
        }

        return anchor;
    }

    private void EnsureStableSlotAssignments(
        SimulationContext context,
        EntityId group,
        List<MemberRuntime> members,
        Vector3 centroid,
        Vector3 forward,
        Vector3 right)
    {
        Array.Clear(_claimedSlotBuffer, 0, members.Count);

        for (int index = 0; index < members.Count; index++)
        {
            MemberRuntime member = members[index];
            int slot = member.SlotIndex;

            if ((uint)slot < (uint)members.Count &&
                !_claimedSlotBuffer[slot])
            {
                _claimedSlotBuffer[slot] = true;
                continue;
            }

            members[index] = member with
            {
                SlotIndex = MovementGroupMember.UnassignedSlot
            };
        }

        for (int memberIndex = 0;
             memberIndex < members.Count;
             memberIndex++)
        {
            MemberRuntime member = members[memberIndex];
            if (member.SlotIndex != MovementGroupMember.UnassignedSlot)
            {
                continue;
            }

            int bestSlot = -1;
            float bestDistanceSquared = float.PositiveInfinity;

            for (int slotIndex = 0;
                 slotIndex < members.Count;
                 slotIndex++)
            {
                if (_claimedSlotBuffer[slotIndex])
                {
                    continue;
                }

                Vector2 local = _localSlotBuffer[slotIndex];
                Vector3 assignmentPosition =
                    centroid +
                    right * local.X +
                    forward * local.Y;
                float distanceSquared =
                    HorizontalDistanceSquared(
                        member.Position,
                        assignmentPosition);

                if (distanceSquared < bestDistanceSquared ||
                    (distanceSquared == bestDistanceSquared &&
                     slotIndex < bestSlot))
                {
                    bestDistanceSquared = distanceSquared;
                    bestSlot = slotIndex;
                }
            }

            if (bestSlot < 0)
            {
                throw new InvalidOperationException(
                    "Formation slot assignment exhausted available slots.");
            }

            _claimedSlotBuffer[bestSlot] = true;
            member = member with
            {
                SlotIndex = bestSlot
            };
            members[memberIndex] = member;

            context.Entities.SetComponent(
                member.Entity,
                new MovementGroupMember(
                    group,
                    bestSlot));
            _slotReassignmentCount++;
        }
    }

    private float CalculateMaximumSlotError(
        IReadOnlyList<MemberRuntime> members)
    {
        float maximum = 0.0f;

        for (int index = 0; index < members.Count; index++)
        {
            MemberRuntime member = members[index];
            if ((uint)member.SlotIndex >= (uint)members.Count)
            {
                continue;
            }

            maximum = MathF.Max(
                maximum,
                HorizontalDistance(
                    member.Position,
                    _worldSlotBuffer[member.SlotIndex]));
        }

        return maximum;
    }

    private static void SetFormationConstraint(
        SimulationContext context,
        EntityId entity,
        float maximumSpeed)
    {
        var constraint =
            new FormationMovementConstraint(maximumSpeed);

        if (context.Entities.HasComponent<
                FormationMovementConstraint>(entity))
        {
            context.Entities.SetComponent(entity, constraint);
        }
        else
        {
            context.Entities.AddComponent(entity, constraint);
        }
    }

    private static void SetFormationLocalOrder(
        SimulationContext context,
        EntityId entity,
        in MovementGroupOrder groupOrder,
        Vector3 target)
    {
        SimulationTick acceptedAtTick = context.Tick;

        if (context.Entities.TryGetComponent(
                entity,
                out MovementOrder existing) &&
            existing.Kind == MovementOrderKind.FormationLocal &&
            HorizontalDistanceSquared(
                existing.WorldTarget,
                target) <= SlotTargetUpdateDistanceSquared)
        {
            acceptedAtTick = existing.AcceptedAtTick;
        }

        var order = new MovementOrder(
            groupOrder.Issuer,
            target,
            groupOrder.SubmittedAtTick,
            acceptedAtTick,
            MovementOrderKind.FormationLocal);

        if (context.Entities.HasComponent<MovementOrder>(entity))
        {
            context.Entities.SetComponent(entity, order);
        }
        else
        {
            context.Entities.AddComponent(entity, order);
        }
    }

    private void AddPendingDebugGroup(
        EntityId group,
        in MovementGroupOrder order,
        in MovementGroupState state)
    {
        if (!DebugCaptureEnabled)
        {
            return;
        }

        _debugGroups.Add(
            new FormationMovementDebugGroup(
                group,
                order.Formation,
                state.Centroid,
                state.BoundsMinimum,
                state.BoundsMaximum,
                state.Forward,
                order.Destination,
                state.MemberCount,
                state.CompressionScale,
                state.SplitCohortCount));
    }

    private void AddDebugGroup(
        EntityId group,
        in MovementGroupOrder order,
        in MovementGroupState state,
        Vector3 activeWaypoint,
        NavigationPath path)
    {
        _debugGroups.Add(
            new FormationMovementDebugGroup(
                group,
                order.Formation,
                state.Centroid,
                state.BoundsMinimum,
                state.BoundsMaximum,
                state.Forward,
                activeWaypoint,
                state.MemberCount,
                state.CompressionScale,
                state.SplitCohortCount));

        for (int index = 0; index < path.Waypoints.Count; index++)
        {
            _debugRoutePoints.Add(
                new FormationMovementDebugRoutePoint(
                    group,
                    index,
                    path.Waypoints[index]));
        }
    }

    private void EnsureSlotCapacity(int count)
    {
        if (_localSlotBuffer.Length >= count)
        {
            return;
        }

        int capacity = Math.Max(16, _localSlotBuffer.Length);
        while (capacity < count)
        {
            capacity = checked(capacity * 2);
        }

        _localSlotBuffer = new Vector2[capacity];
        _worldSlotBuffer = new Vector3[capacity];
        _claimedSlotBuffer = new bool[capacity];
    }

    private static Vector3 ResolveForward(
        Vector3 previous,
        Vector3 desired)
    {
        Vector3 horizontal = new(desired.X, 0.0f, desired.Z);
        if (horizontal.LengthSquared() <= MinimumDirectionLengthSquared)
        {
            Vector3 previousHorizontal =
                new(previous.X, 0.0f, previous.Z);
            return previousHorizontal.LengthSquared() <=
                   MinimumDirectionLengthSquared
                ? Vector3.UnitZ
                : Vector3.Normalize(previousHorizontal);
        }

        return Vector3.Normalize(horizontal);
    }

    private static bool IsTraversableWorldPosition(
        NavigationGrid grid,
        Vector3 position,
        in NavigationCapabilities capabilities)
    {
        return grid.TryWorldToCell(
                   position,
                   out NavigationCellCoordinate cell) &&
               grid.IsTraversable(cell, capabilities);
    }

    private static float HorizontalDistance(
        Vector3 left,
        Vector3 right) =>
        MathF.Sqrt(HorizontalDistanceSquared(left, right));

    private static float HorizontalDistanceSquared(
        Vector3 left,
        Vector3 right)
    {
        float deltaX = left.X - right.X;
        float deltaZ = left.Z - right.Z;
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private readonly record struct MemberRuntime(
        EntityId Entity,
        Vector3 Position,
        GroundMovement Movement,
        NavigationAgent NavigationAgent,
        int SlotIndex);

    private readonly record struct GroupMetrics(
        Vector3 Centroid,
        Vector3 BoundsMinimum,
        Vector3 BoundsMaximum,
        float MaximumRadius,
        float MinimumSpeed,
        NavigationCapabilities RepresentativeCapabilities);
}
