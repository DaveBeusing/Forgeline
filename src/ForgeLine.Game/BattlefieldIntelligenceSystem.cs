using System.Diagnostics;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public readonly record struct IntelligenceSensorDebugEntry(
    EntityId Entity,
    FactionId Faction,
    Vector3 Position,
    float RangeMeters,
    bool IsRadar,
    float IdentificationRangeMeters);

public readonly record struct BattlefieldIntelligenceMetrics(
    int VisualSensorsThisTick,
    int RadarSensorsThisTick,
    int SensorScansThisTick,
    int CandidatesThisTick,
    int DetectedContactsThisTick,
    int IdentifiedContactsThisTick,
    int VisibleCellsThisTick,
    int ExploredCells,
    TimeSpan SensorUpdateDuration,
    ulong TotalSensorScans,
    ulong TotalCandidates,
    ulong TotalDetectedContacts,
    ulong TotalIdentifiedContacts);

public sealed class BattlefieldIntelligenceSystem : ISimulationSystem
{
    private readonly FactionIntelligenceStore _intelligence;
    private readonly TechnologyDefinitionCatalog _technologies;
    private readonly SpatialGridIndex? _spatialIndex;
    private readonly VisualOcclusionPolicy? _visualOcclusion;
    public const int MaximumVisualTracesPerTick = 2048;
    public const int MaximumVisualTracesPerSensor = 128;
    private int _visualTracesRemaining;
    private int _sensorSlots, _sensorOrdinal;
    private readonly List<EntityId> _bridgeCandidates = new(32);
    private readonly SpatialQueryBuffer _queryBuffer = new(512);
    private readonly Dictionary<EntityId, VisualCoverage> _visualCoverage =
        new();
    private readonly HashSet<EntityId> _activeVisualSensors = new();
    private readonly HashSet<FactionId> _activeFactions = new();
    private readonly List<EntityId> _staleVisualSensors = new();
    private readonly List<IntelligenceSensorDebugEntry> _debugSensors =
        new();

    private int _visualSensorsThisTick;
    private int _radarSensorsThisTick;
    private int _sensorScansThisTick;
    private int _candidatesThisTick;
    private int _detectedContactsThisTick;
    private int _identifiedContactsThisTick;
    private TimeSpan _sensorUpdateDuration;

    private ulong _totalSensorScans;
    private ulong _totalCandidates;
    private ulong _totalDetectedContacts;
    private ulong _totalIdentifiedContacts;

    public BattlefieldIntelligenceSystem(
        FactionIntelligenceStore intelligence,
        SpatialGridIndex? spatialIndex = null,
        TechnologyDefinitionCatalog? technologies = null,
        VisualOcclusionPolicy? visualOcclusion = null)
    {
        _intelligence = intelligence ??
            throw new ArgumentNullException(nameof(intelligence));
        _spatialIndex = spatialIndex;
        _visualOcclusion = visualOcclusion;
        _technologies = technologies ?? DirectorateTechnologyDefinitions.CreateCatalog();
    }

    public SimulationPhase Phase => SimulationPhase.Sensors;

    public bool TimingEnabled { get; set; }

    public bool DebugCaptureEnabled { get; set; }

    public IReadOnlyList<IntelligenceSensorDebugEntry> DebugSensors =>
        _debugSensors;

    public FactionIntelligenceStore Intelligence =>
        _intelligence;

    public BattlefieldIntelligenceMetrics Metrics
    {
        get
        {
            int visibleCells = 0;
            int exploredCells = 0;

            foreach (FactionId faction in _activeFactions)
            {
                visibleCells +=
                    _intelligence.GetVisibleCellCount(faction);
                exploredCells +=
                    _intelligence.GetExploredCellCount(faction);
            }

            return new BattlefieldIntelligenceMetrics(
                _visualSensorsThisTick,
                _radarSensorsThisTick,
                _sensorScansThisTick,
                _candidatesThisTick,
                _detectedContactsThisTick,
                _identifiedContactsThisTick,
                visibleCells,
                exploredCells,
                _sensorUpdateDuration,
                _totalSensorScans,
                _totalCandidates,
                _totalDetectedContacts,
                _totalIdentifiedContacts);
        }
    }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        long started =
            TimingEnabled
                ? Stopwatch.GetTimestamp()
                : 0;

        ResetTick();
        _visualTracesRemaining = MaximumVisualTracesPerTick;
        _sensorOrdinal = 0;
        _bridgeCandidates.Clear();
        foreach (EntityId bridge in context.Entities.Query<BridgeCombat, WorldTransform>())
        {
            if (_bridgeCandidates.Count == 32) break;
            _bridgeCandidates.Add(bridge);
        }
        _sensorSlots = (int)Math.Max(1, Math.Min(int.MaxValue,
            (long)context.Entities.GetComponentCount<VisualSensorState>() + context.Entities.GetComponentCount<RadarSensorState>()));
        _intelligence.BeginTick(context.Tick);

        ProcessVisualSensors(context);
        RemoveStaleVisualCoverage();
        ReapplyVisualCoverage();
        ProcessRadarSensors(context);

        if (TimingEnabled)
        {
            _sensorUpdateDuration =
                Stopwatch.GetElapsedTime(started);
        }
    }

    private void ProcessVisualSensors(
        SimulationContext context)
    {
        foreach (EntityId sensorEntity in
                 context.Entities.Query<
                     VisualSensorState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            int traceBudget = NextSensorBudget(context.Tick);
            VisualSensorState sensor =
                context.Entities.GetComponent<VisualSensorState>(
                    sensorEntity);
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    sensorEntity);

            _visualSensorsThisTick++;
            _activeFactions.Add(sensor.Faction);
            _activeVisualSensors.Add(sensorEntity);

            if (DebugCaptureEnabled)
            {
                _debugSensors.Add(
                    new IntelligenceSensorDebugEntry(
                        sensorEntity,
                        sensor.Faction,
                        transform.Position,
                        sensor.RangeMeters,
                        IsRadar: false,
                        IdentificationRangeMeters:
                            sensor.RangeMeters));
            }

            bool due =
                IsSensorDue(
                    context.Tick,
                    sensorEntity,
                    sensor.UpdateIntervalTicks);

            if (due ||
                !_visualCoverage.ContainsKey(sensorEntity))
            {
                _visualCoverage[sensorEntity] =
                    new VisualCoverage(
                        sensor.Faction,
                        transform.Position,
                        sensor.RangeMeters);
            }

            if (!due)
            {
                continue;
            }

            RecordScan();
            ScanVisualSensor(
                context,
                sensorEntity,
                sensor,
                transform.Position, traceBudget);
        }
    }

    private void ProcessRadarSensors(
        SimulationContext context)
    {
        foreach (EntityId sensorEntity in
                 context.Entities.Query<
                     RadarSensorState,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            int traceBudget = NextSensorBudget(context.Tick);
            if (!RadarOperation.IsOperational(context.Entities, sensorEntity))
            {
                continue;
            }

            RadarSensorState sensor =
                context.Entities.GetComponent<RadarSensorState>(
                    sensorEntity);

            _radarSensorsThisTick++;
            _activeFactions.Add(sensor.Faction);

            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    sensorEntity);

            if (DebugCaptureEnabled)
            {
                _debugSensors.Add(
                    new IntelligenceSensorDebugEntry(
                        sensorEntity,
                        sensor.Faction,
                        transform.Position,
                        sensor.DetectionRangeMeters,
                        IsRadar: true,
                        RadarOperation.IdentificationRange(context.Entities, sensorEntity, sensor, _technologies)));
            }

            if (!IsSensorDue(
                    context.Tick,
                    sensorEntity,
                    sensor.UpdateIntervalTicks))
            {
                continue;
            }

            RecordScan();
            ScanRadarSensor(
                context,
                sensorEntity,
                sensor,
                transform.Position, traceBudget);
        }
    }

    private void ScanVisualSensor(
        SimulationContext context,
        EntityId sensorEntity,
        VisualSensorState sensor,
        Vector3 sensorPosition, int sensorBudget)
    {
        ForEachCandidate(
            context,
            sensorPosition,
            sensor.RangeMeters,
            candidate =>
            {
                if (!TryGetEnemySignature(
                        context.Entities,
                        sensorEntity,
                        sensor.Faction,
                        candidate,
                        visual: true,
                        out IntelligenceSignature signature,
                        out WorldTransform targetTransform))
                {
                    return;
                }

                if (!VisualClear(sensorPosition, targetTransform.Position, ref sensorBudget)) return;
                _intelligence.Observe(
                    sensor.Faction,
                    candidate,
                    signature,
                    targetTransform.Position,
                    IntelligenceState.Identified,
                    context.Tick);

                _identifiedContactsThisTick++;
                _totalIdentifiedContacts++;
            }, rotation: _visualOcclusion is null ? 0 :
                context.Tick.Value / (ulong)sensor.UpdateIntervalTicks + sensorEntity.Index);
    }

    private void ScanRadarSensor(
        SimulationContext context,
        EntityId sensorEntity,
        RadarSensorState sensor,
        Vector3 sensorPosition, int sensorBudget)
    {
        float identificationRange = RadarOperation.IdentificationRange(context.Entities, sensorEntity, sensor, _technologies);
        float identificationRangeSquared = identificationRange * identificationRange;

        ForEachCandidate(
            context,
            sensorPosition,
            sensor.DetectionRangeMeters,
            candidate =>
            {
                if (!TryGetEnemySignature(
                        context.Entities,
                        sensorEntity,
                        sensor.Faction,
                        candidate,
                        visual: false,
                        out IntelligenceSignature signature,
                        out WorldTransform targetTransform))
                {
                    return;
                }

                Vector3 delta =
                    targetTransform.Position -
                    sensorPosition;
                IntelligenceState state =
                    identificationRange > 0.0f &&
                    delta.LengthSquared() <=
                    identificationRangeSquared &&
                    VisualClear(sensorPosition, targetTransform.Position, ref sensorBudget)
                        ? IntelligenceState.Identified
                        : IntelligenceState.Detected;

                _intelligence.Observe(
                    sensor.Faction,
                    candidate,
                    signature,
                    targetTransform.Position,
                    state,
                    context.Tick);

                if (state == IntelligenceState.Identified)
                {
                    _identifiedContactsThisTick++;
                    _totalIdentifiedContacts++;
                }
                else
                {
                    _detectedContactsThisTick++;
                    _totalDetectedContacts++;
                }
            }, rotation: _visualOcclusion is null ? 0 :
                context.Tick.Value / (ulong)sensor.UpdateIntervalTicks + sensorEntity.Index);
    }

    // Rotate bounded admission across stable sensor slots without an unbounded history.
    private int NextSensorBudget(SimulationTick tick)
    {
        int ordinal = _sensorOrdinal++;
        int admitted = Math.Min(MaximumVisualTracesPerTick, _sensorSlots);
        long relative = (ordinal - (long)(tick.Value % (ulong)_sensorSlots) + _sensorSlots) % _sensorSlots;
        return relative < admitted ? Math.Min(MaximumVisualTracesPerSensor, MaximumVisualTracesPerTick / admitted) : 0;
    }

    private bool VisualClear(Vector3 source, Vector3 target, ref int sensorBudget)
    {
        if (_visualOcclusion is null) return true;
        if (sensorBudget <= 0 || _visualTracesRemaining <= 0) return false;
        sensorBudget--; _visualTracesRemaining--;
        return _visualOcclusion.Evaluate(source, target) == VisualTraceResult.Clear;
    }

    private void ForEachCandidate(
        SimulationContext context,
        Vector3 center,
        float rangeMeters,
        Action<EntityId> visitor, ulong rotation = 0)
    {
        if (_spatialIndex is not null)
        {
            _spatialIndex.QueryRadius(
                center,
                rangeMeters,
                _queryBuffer,
                order: SpatialQueryOrder.StableEntityId);

            ReadOnlySpan<EntityId> candidates =
                _queryBuffer.Results;

            int total = candidates.Length + _bridgeCandidates.Count;
            int first = total == 0 ? 0 : (int)(rotation % (ulong)total);
            for (int index = 0; index < total; index++)
            {
                int slot = (int)(((long)first + index) % total);
                EntityId candidate = slot < candidates.Length ? candidates[slot] : _bridgeCandidates[slot - candidates.Length];
                if (slot >= candidates.Length)
                {
                    Vector3 delta = context.Entities.GetComponent<WorldTransform>(candidate).Position - center;
                    if (delta.LengthSquared() > rangeMeters * rangeMeters) continue;
                }
                _candidatesThisTick++;
                _totalCandidates++;
                visitor(candidate);
            }

            return;
        }

        foreach (EntityId candidate in
                 context.Entities.Query<
                     IntelligenceSignature,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(
                    candidate);
            Vector3 delta =
                transform.Position -
                center;

            if (delta.LengthSquared() >
                rangeMeters * rangeMeters)
            {
                continue;
            }

            _candidatesThisTick++;
            _totalCandidates++;
            visitor(candidate);
        }
    }

    private static bool TryGetEnemySignature(
        EntityRegistry entities,
        EntityId sensorEntity,
        FactionId sensorFaction,
        EntityId candidate,
        bool visual,
        out IntelligenceSignature signature,
        out WorldTransform transform)
    {
        signature = default;
        transform = default;

        if (candidate == sensorEntity ||
            !entities.IsAlive(candidate) ||
            !entities.TryGetComponent(
                candidate,
                out signature) ||
            signature.Faction == sensorFaction ||
            !entities.TryGetComponent(
                candidate,
                out transform))
        {
            return false;
        }

        return visual
            ? signature.VisualDetectable
            : signature.RadarDetectable;
    }

    private void ReapplyVisualCoverage()
    {
        foreach (VisualCoverage coverage in
                 _visualCoverage.Values)
        {
            _intelligence.MarkVisibleCircle(
                coverage.Faction,
                coverage.Center,
                coverage.RangeMeters);
        }
    }

    private void RemoveStaleVisualCoverage()
    {
        _staleVisualSensors.Clear();

        foreach (EntityId sensor in
                 _visualCoverage.Keys)
        {
            if (!_activeVisualSensors.Contains(sensor))
            {
                _staleVisualSensors.Add(sensor);
            }
        }

        for (int index = 0;
             index < _staleVisualSensors.Count;
             index++)
        {
            _visualCoverage.Remove(
                _staleVisualSensors[index]);
        }
    }

    private void RecordScan()
    {
        _sensorScansThisTick++;
        _totalSensorScans++;
    }

    private static bool IsSensorDue(
        SimulationTick tick,
        EntityId sensor,
        int intervalTicks)
    {
        ulong interval =
            (ulong)intervalTicks;
        ulong offset =
            sensor.Index % interval;

        return (tick.Value + offset) %
               interval == 0;
    }

    private void ResetTick()
    {
        _visualSensorsThisTick = 0;
        _radarSensorsThisTick = 0;
        _sensorScansThisTick = 0;
        _candidatesThisTick = 0;
        _detectedContactsThisTick = 0;
        _identifiedContactsThisTick = 0;
        _sensorUpdateDuration = TimeSpan.Zero;
        _activeVisualSensors.Clear();
        _activeFactions.Clear();
        _debugSensors.Clear();
    }

    private readonly record struct VisualCoverage(
        FactionId Faction,
        Vector3 Center,
        float RangeMeters);
}
