using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum MatchStatus : byte
{
    Loading = 1,
    Active = 2,
    Running = Active,
    Victory = 3,
    Draw = 4,
    Ended = 5
}

public enum PlayerMatchStatus : byte
{
    Loading = 1,
    Active = 2,
    Victory = 3,
    Defeat = 4,
    Draw = 5,
    Ended = 6
}

public readonly record struct MatchState(
    MatchStatus Status,
    PlayerId Winner,
    SimulationTick CompletedAtTick)
{
    public static MatchState Loading =>
        new(
            MatchStatus.Loading,
            PlayerId.None,
            SimulationTick.Zero);

    public static MatchState Active =>
        new(
            MatchStatus.Active,
            PlayerId.None,
            SimulationTick.Zero);

    public static MatchState Running => Active;

    public bool IsTerminal =>
        Status is
            MatchStatus.Victory or
            MatchStatus.Draw or
            MatchStatus.Ended;

    public PlayerMatchStatus ForPlayer(PlayerId player)
    {
        if (!player.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        return Status switch
        {
            MatchStatus.Loading =>
                PlayerMatchStatus.Loading,
            MatchStatus.Active =>
                PlayerMatchStatus.Active,
            MatchStatus.Victory when Winner == player =>
                PlayerMatchStatus.Victory,
            MatchStatus.Victory =>
                PlayerMatchStatus.Defeat,
            MatchStatus.Draw =>
                PlayerMatchStatus.Draw,
            MatchStatus.Ended =>
                PlayerMatchStatus.Ended,
            _ =>
                throw new InvalidOperationException(
                    $"Unsupported match status '{Status}'.")
        };
    }
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

public sealed class EndMatchCommand : ISimulationCommand
{
    public EndMatchCommand(
        PlayerId issuer,
        EntityId matchStateEntity,
        SimulationTick submittedAtTick)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        if (!matchStateEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStateEntity));
        }

        Issuer = issuer;
        MatchStateEntity = matchStateEntity;
        SubmittedAtTick = submittedAtTick;
    }

    public PlayerId Issuer { get; }

    public EntityId MatchStateEntity { get; }

    public SimulationTick SubmittedAtTick { get; }

    public bool Accepted { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.TryGetComponent(
                MatchStateEntity,
                out MatchState state) ||
            state.Status is not
                MatchStatus.Victory and not
                MatchStatus.Draw)
        {
            Accepted = false;
            return;
        }

        context.Entities.SetComponent(
            MatchStateEntity,
            state with
            {
                Status = MatchStatus.Ended
            });
        Accepted = true;
    }
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
            state.Status != MatchStatus.Active)
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
            MatchState.Loading);
        return entity;
    }

    public static void ActivateMatch(
        EntityRegistry entities,
        EntityId matchStateEntity)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (!entities.TryGetComponent(
                matchStateEntity,
                out MatchState state) ||
            state.Status != MatchStatus.Loading)
        {
            throw new InvalidOperationException(
                "Only a loading match can transition to active.");
        }

        entities.SetComponent(
            matchStateEntity,
            MatchState.Active);
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

        if (definition.Owner.Value > uint.MaxValue)
        {
            throw new InvalidOperationException(
                $"Player {definition.Owner} cannot be represented as a combat faction.");
        }

        FactionId faction =
            new((uint)definition.Owner.Value);

        if (!entities.HasComponent<Combatant>(commandCore))
        {
            entities.AddComponent(
                commandCore,
                new Combatant(faction));
        }

        if (!entities.HasComponent<Targetable>(commandCore))
        {
            entities.AddComponent(
                commandCore,
                new Targetable(TargetClass.Structure));
        }

        if (!entities.HasComponent<HealthState>(commandCore))
        {
            entities.AddComponent(
                commandCore,
                HealthState.Full(1_500.0));
        }

        if (!entities.HasComponent<CombatHitbox>(commandCore))
        {
            entities.AddComponent(
                commandCore,
                CombatHitbox.Default);
        }

        if (!entities.HasComponent<TargetPriority>(commandCore))
        {
            entities.AddComponent(
                commandCore,
                new TargetPriority(100));
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
