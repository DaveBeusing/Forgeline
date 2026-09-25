using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum MatchStatus : byte
{
    Running = 1,
    Victory = 2,
    Draw = 3
}

public readonly record struct MatchState(
    MatchStatus Status,
    PlayerId Winner,
    SimulationTick CompletedAtTick)
{
    public static MatchState Running =>
        new(
            MatchStatus.Running,
            PlayerId.None,
            SimulationTick.Zero);
}

public readonly record struct CommandCoreObjective
{
    public CommandCoreObjective(
        string key,
        PlayerId owner,
        EntityId commandCore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!owner.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(owner));
        }

        if (!commandCore.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(commandCore));
        }

        Key = key;
        Owner = owner;
        CommandCore = commandCore;
    }

    public string Key { get; }

    public PlayerId Owner { get; }

    public EntityId CommandCore { get; }
}

public sealed class MatchObjectiveSystem : ISimulationSystem
{
    private readonly EntityId _matchStateEntity;
    private readonly List<EntityId> _objectiveEntities = new();

    public MatchObjectiveSystem(EntityId matchStateEntity)
    {
        if (!matchStateEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStateEntity));
        }

        _matchStateEntity = matchStateEntity;
    }

    public SimulationPhase Phase =>
        SimulationPhase.SnapshotEvents;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Entities.IsAlive(_matchStateEntity) ||
            !context.Entities.TryGetComponent(
                _matchStateEntity,
                out MatchState state) ||
            state.Status != MatchStatus.Running)
        {
            return;
        }

        _objectiveEntities.Clear();

        foreach (EntityId entity in
                 context.Entities.Query<CommandCoreObjective>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            _objectiveEntities.Add(entity);
        }

        if (_objectiveEntities.Count < 2)
        {
            return;
        }

        int surviving = 0;
        PlayerId survivor = PlayerId.None;

        for (int index = 0; index < _objectiveEntities.Count; index++)
        {
            CommandCoreObjective objective =
                context.Entities.GetComponent<CommandCoreObjective>(
                    _objectiveEntities[index]);

            bool alive =
                context.Entities.IsAlive(
                    objective.CommandCore) &&
                context.Entities.TryGetComponent(
                    objective.CommandCore,
                    out CompletedBuilding building) &&
                building.BuildingId ==
                BuildingIds.CommandCore &&
                context.Entities.HasComponent<CommandFacility>(
                    objective.CommandCore);

            if (alive)
            {
                surviving++;
                survivor = objective.Owner;
            }
        }

        if (surviving > 1)
        {
            return;
        }

        MatchState completed =
            surviving == 1
                ? new MatchState(
                    MatchStatus.Victory,
                    survivor,
                    context.Tick)
                : new MatchState(
                    MatchStatus.Draw,
                    PlayerId.None,
                    context.Tick);

        context.Entities.SetComponent(
            _matchStateEntity,
            completed);
    }

    public static EntityId CreateMatchStateEntity(
        EntityRegistry entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        EntityId entity =
            entities.CreateEntity();
        entities.AddComponent(
            entity,
            MatchState.Running);
        return entity;
    }

    public static EntityId AttachCommandCoreObjective(
        EntityRegistry entities,
        BattlefieldObjectiveDefinition definition,
        EntityId commandCore)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (!entities.IsAlive(commandCore) ||
            !entities.TryGetComponent(
                commandCore,
                out CompletedBuilding building) ||
            building.BuildingId != BuildingIds.CommandCore ||
            building.Owner != definition.Owner ||
            !entities.HasComponent<CommandFacility>(commandCore))
        {
            throw new InvalidOperationException(
                $"Entity {commandCore} is not the completed Command Core for player {definition.Owner}.");
        }

        EntityId objective =
            entities.CreateEntity();
        entities.AddComponent(
            objective,
            new CommandCoreObjective(
                definition.Key,
                definition.Owner,
                commandCore));
        return objective;
    }
}
