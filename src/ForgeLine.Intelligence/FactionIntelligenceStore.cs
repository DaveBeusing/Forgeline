using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Intelligence;

public sealed class FactionIntelligenceStore
{
    private readonly IntelligenceGridSettings _settings;
    private readonly Dictionary<FactionId, FactionState> _factions =
        new();

    public FactionIntelligenceStore(
        IntelligenceGridSettings? settings = null)
    {
        _settings =
            settings ??
            new IntelligenceGridSettings();
        _settings.Validate();
    }

    public IntelligenceGridSettings Settings =>
        _settings;

    public SimulationTick CurrentTick { get; private set; }

    public void BeginTick(SimulationTick tick)
    {
        CurrentTick = tick;

        foreach (FactionState state in _factions.Values)
        {
            state.Visible.Clear();
        }
    }

    public VisibilityCellCoordinate WorldToCell(
        Vector3 position)
    {
        ValidatePosition(position);

        return new VisibilityCellCoordinate(
            checked(
                (int)MathF.Floor(
                    position.X /
                    _settings.CellSizeMeters)),
            checked(
                (int)MathF.Floor(
                    position.Z /
                    _settings.CellSizeMeters)));
    }

    public Vector3 CellCenter(
        VisibilityCellCoordinate cell,
        float y = 0.0f)
    {
        if (!float.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        float half =
            _settings.CellSizeMeters * 0.5f;

        return new Vector3(
            cell.X * _settings.CellSizeMeters + half,
            y,
            cell.Z * _settings.CellSizeMeters + half);
    }

    public int MarkVisibleCircle(
        FactionId faction,
        Vector3 center,
        float radiusMeters)
    {
        RequireFaction(faction);
        ValidatePosition(center);

        if (!float.IsFinite(radiusMeters) ||
            radiusMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMeters));
        }

        FactionState state =
            GetOrCreate(faction);

        float cellSize =
            _settings.CellSizeMeters;
        int minimumX =
            checked(
                (int)MathF.Floor(
                    (center.X - radiusMeters) /
                    cellSize));
        int maximumX =
            checked(
                (int)MathF.Floor(
                    (center.X + radiusMeters) /
                    cellSize));
        int minimumZ =
            checked(
                (int)MathF.Floor(
                    (center.Z - radiusMeters) /
                    cellSize));
        int maximumZ =
            checked(
                (int)MathF.Floor(
                    (center.Z + radiusMeters) /
                    cellSize));
        float radiusSquared =
            radiusMeters * radiusMeters;
        int added = 0;

        for (int z = minimumZ;
             z <= maximumZ;
             z++)
        {
            for (int x = minimumX;
                 x <= maximumX;
                 x++)
            {
                var cell =
                    new VisibilityCellCoordinate(x, z);

                if (!CircleIntersectsCell(
                        center,
                        radiusSquared,
                        cell))
                {
                    continue;
                }

                if (state.Visible.Add(cell))
                {
                    added++;
                }

                state.Explored.Add(cell);
            }
        }

        return added;
    }

    public void Observe(
        FactionId observingFaction,
        EntityId entity,
        in IntelligenceSignature signature,
        Vector3 position,
        IntelligenceState state,
        SimulationTick tick)
    {
        RequireFaction(observingFaction);

        if (!entity.IsValid)
        {
            throw new ArgumentException(
                "Observed contacts require a valid entity.",
                nameof(entity));
        }

        if (state is not
            IntelligenceState.Detected and not
            IntelligenceState.Identified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state));
        }

        ValidatePosition(position);

        FactionState faction =
            GetOrCreate(observingFaction);
        IntelligenceContactKey key =
            IntelligenceContactKey.FromEntity(entity);
        uint identityKey =
            state == IntelligenceState.Identified
                ? signature.IdentityKey
                : 0;

        if (faction.Contacts.TryGetValue(
                entity,
                out ContactRecord existing) &&
            existing.LastSeenTick == tick &&
            existing.State == IntelligenceState.Identified)
        {
            return;
        }

        faction.Contacts[entity] =
            new ContactRecord(
                key,
                position,
                state,
                tick,
                identityKey);
    }

    public IntelligenceState GetTerrainState(
        FactionId faction,
        VisibilityCellCoordinate cell)
    {
        RequireFaction(faction);

        if (!_factions.TryGetValue(
                faction,
                out FactionState? state))
        {
            return IntelligenceState.Unexplored;
        }

        if (state.Visible.Contains(cell))
        {
            return IntelligenceState.Visible;
        }

        return state.Explored.Contains(cell)
            ? IntelligenceState.Explored
            : IntelligenceState.Unexplored;
    }

    public bool IsEntityCurrentlyDetected(
        FactionId faction,
        EntityId entity)
    {
        return TryGetRecord(
                faction,
                entity,
                out ContactRecord record) &&
            record.LastSeenTick == CurrentTick;
    }

    public bool IsEntityCurrentlyIdentified(
        FactionId faction,
        EntityId entity)
    {
        return TryGetRecord(
                faction,
                entity,
                out ContactRecord record) &&
            record.LastSeenTick == CurrentTick &&
            record.State == IntelligenceState.Identified;
    }

    public bool TryGetContact(
        FactionId faction,
        IntelligenceContactKey key,
        out IntelligenceContact contact)
    {
        RequireFaction(faction);

        if (_factions.TryGetValue(
                faction,
                out FactionState? state))
        {
            foreach (ContactRecord record in
                     state.Contacts.Values)
            {
                if (record.ContactKey != key)
                {
                    continue;
                }

                contact =
                    ToPublicContact(record);
                return true;
            }
        }

        contact = default;
        return false;
    }

    public FactionIntelligenceSnapshot Capture(
        FactionId faction)
    {
        RequireFaction(faction);

        if (!_factions.TryGetValue(
                faction,
                out FactionState? state))
        {
            return new FactionIntelligenceSnapshot(
                faction,
                CurrentTick,
                [],
                []);
        }

        VisibilityCellSnapshot[] cells =
            state.Explored
                .OrderBy(static cell => cell)
                .Select(
                    cell =>
                        new VisibilityCellSnapshot(
                            cell,
                            state.Visible.Contains(cell)
                                ? IntelligenceState.Visible
                                : IntelligenceState.Explored))
                .ToArray();

        IntelligenceContact[] contacts =
            state.Contacts.Values
                .OrderBy(static contact => contact.ContactKey)
                .Select(ToPublicContact)
                .ToArray();

        return new FactionIntelligenceSnapshot(
            faction,
            CurrentTick,
            cells,
            contacts);
    }

    public int GetExploredCellCount(FactionId faction)
    {
        RequireFaction(faction);
        return _factions.TryGetValue(
                faction,
                out FactionState? state)
            ? state.Explored.Count
            : 0;
    }

    public int GetVisibleCellCount(FactionId faction)
    {
        RequireFaction(faction);
        return _factions.TryGetValue(
                faction,
                out FactionState? state)
            ? state.Visible.Count
            : 0;
    }

    public int GetContactCount(FactionId faction)
    {
        RequireFaction(faction);
        return _factions.TryGetValue(
                faction,
                out FactionState? state)
            ? state.Contacts.Count
            : 0;
    }

    private bool TryGetRecord(
        FactionId faction,
        EntityId entity,
        out ContactRecord record)
    {
        RequireFaction(faction);

        if (_factions.TryGetValue(
                faction,
                out FactionState? state) &&
            state.Contacts.TryGetValue(
                entity,
                out ContactRecord found))
        {
            record = found;
            return true;
        }

        record = default;
        return false;
    }

    private FactionState GetOrCreate(
        FactionId faction)
    {
        if (_factions.TryGetValue(
                faction,
                out FactionState? state))
        {
            return state;
        }

        state = new FactionState();
        _factions.Add(faction, state);
        return state;
    }

    private IntelligenceContact ToPublicContact(
        ContactRecord record) =>
        new(
            record.ContactKey,
            record.LastKnownPosition,
            record.State,
            record.LastSeenTick,
            record.IdentityKey,
            record.LastSeenTick == CurrentTick);

    private bool CircleIntersectsCell(
        Vector3 center,
        float radiusSquared,
        VisibilityCellCoordinate cell)
    {
        float cellSize =
            _settings.CellSizeMeters;
        float minimumX =
            cell.X * cellSize;
        float maximumX =
            minimumX + cellSize;
        float minimumZ =
            cell.Z * cellSize;
        float maximumZ =
            minimumZ + cellSize;

        float closestX =
            Math.Clamp(
                center.X,
                minimumX,
                maximumX);
        float closestZ =
            Math.Clamp(
                center.Z,
                minimumZ,
                maximumZ);
        float deltaX =
            center.X - closestX;
        float deltaZ =
            center.Z - closestZ;

        return deltaX * deltaX +
               deltaZ * deltaZ <=
               radiusSquared;
    }

    private static void RequireFaction(
        FactionId faction)
    {
        if (!faction.IsSpecified)
        {
            throw new ArgumentException(
                "Intelligence state requires a valid faction.",
                nameof(faction));
        }
    }

    private static void ValidatePosition(
        Vector3 position)
    {
        if (!float.IsFinite(position.X) ||
            !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position));
        }
    }

    private sealed class FactionState
    {
        public HashSet<VisibilityCellCoordinate> Explored { get; } =
            new();

        public HashSet<VisibilityCellCoordinate> Visible { get; } =
            new();

        public Dictionary<EntityId, ContactRecord> Contacts { get; } =
            new();
    }

    private readonly record struct ContactRecord(
        IntelligenceContactKey ContactKey,
        Vector3 LastKnownPosition,
        IntelligenceState State,
        SimulationTick LastSeenTick,
        uint IdentityKey);
}
