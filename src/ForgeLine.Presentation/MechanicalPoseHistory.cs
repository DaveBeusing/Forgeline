using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Extraction-owned cosmetic history, bounded and pruned to visible entities every tick.</summary>
internal sealed class MechanicalPoseHistory
{
    internal const int Capacity = 65_536;
    private Dictionary<EntityId, Entry> _previous = [];
    private Dictionary<EntityId, Entry> _current = [];
    private SimulationSessionId _session;
    private ulong _tick;
    private bool _initialized;
    private double _seconds;
    private double _step;

    public void Begin(SimulationSessionId session, ulong tick, double tickSeconds)
    {
        // UI-only refreshes can extract the same completed tick again. Keep
        // its already captured phase instead of treating it as a discontinuity.
        if (_initialized && session == _session && tick == _tick) return;
        bool contiguous = _initialized && session == _session && tick == _tick + 1;
        (_previous, _current) = (_current, _previous);
        _current.Clear();
        if (!contiguous) _previous.Clear();
        _initialized = true;
        _session = session;
        _tick = tick;
        _step = contiguous ? tickSeconds : 0;
        _seconds = tick * tickSeconds;
    }

    public MechanicalPose Capture(EntityId entity, Vector3 position, Quaternion rotation,
        bool active, bool shot, float elevation, bool openDoor)
    {
        // A completed tick owns one cosmetic pose per full entity identity.
        // In particular, a second extraction must not lose a one-shot recoil.
        if (_current.TryGetValue(entity, out Entry captured)) return captured.Pose;
        if (_current.Count >= Capacity) return default;
        bool found = _previous.TryGetValue(entity, out Entry previous);
        MechanicalPose pose = found ? previous.Pose : new MechanicalPose(true, 0, 0, _seconds, -1, elevation, 0);
        double travel = 0;
        if (found)
        {
            Vector3 delta = position - previous.Position;
            // Teleports reset visual travel instead of producing extreme wheel spin.
            if (delta.LengthSquared() < 400)
                travel = Vector3.Dot(delta, Vector3.Transform(Vector3.UnitZ, rotation));
        }
        float doorTarget = openDoor ? 1 : 0;
        float door = pose.Door + Math.Clamp(doorTarget - pose.Door, -(float)_step, (float)_step);
        pose = pose with
        {
            TravelMeters = pose.TravelMeters + travel,
            ActiveSeconds = pose.ActiveSeconds + (active ? _step : 0),
            Seconds = _seconds,
            LastShotSeconds = shot ? _seconds : pose.LastShotSeconds,
            Elevation = elevation,
            Door = door
        };
        _current.Add(entity, new Entry(position, pose));
        return pose;
    }

    public bool HasMoved(EntityId entity) => _current.TryGetValue(entity, out var current) &&
        _previous.TryGetValue(entity, out var previous) &&
        Math.Abs(current.Pose.TravelMeters - previous.Pose.TravelMeters) > .02;

    private readonly record struct Entry(Vector3 Position, MechanicalPose Pose);
}
