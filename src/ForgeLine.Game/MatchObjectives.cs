using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum MatchLifecyclePhase : byte
{
    Initializing = 1,
    Ready = 2,
    Running = 3,
    Paused = 4,
    Ending = 5,
    Completed = 6
}

public enum MatchOutcome : byte
{
    None = 0,
    Victory = 1,
    Draw = 2
}

public enum MatchTerminationReason : byte
{
    None = 0,
    CommandCoreDestroyed = 1,
    Surrender = 2,
    MutualCommandCoreDestruction = 3,
    AllParticipantsEliminated = 4
}

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

public readonly record struct MatchState
{
    public MatchState(
        MatchStatus status,
        PlayerId winner,
        SimulationTick completedAtTick)
    {
        this =
            status switch
            {
                MatchStatus.Loading =>
                    Initializing,
                MatchStatus.Active =>
                    CreateRunning(),
                MatchStatus.Victory =>
                    CreateCompatibilityResult(
                        MatchOutcome.Victory,
                        winner,
                        completedAtTick),
                MatchStatus.Draw =>
                    CreateCompatibilityResult(
                        MatchOutcome.Draw,
                        PlayerId.None,
                        completedAtTick),
                MatchStatus.Ended =>
                    CreateCompatibilityCompleted(
                        winner,
                        completedAtTick),
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(status))
            };
    }

    private MatchState(
        MatchLifecyclePhase lifecycle,
        MatchOutcome outcome,
        MatchTerminationReason terminationReason,
        PlayerId winner,
        PlayerId defeatedPlayer,
        SimulationTick startedAtTick,
        SimulationTick completedAtTick,
        SimulationTick finalizedAtTick,
        SimulationTick lastTransitionAtTick,
        uint transitionCount)
    {
        Lifecycle = lifecycle;
        Outcome = outcome;
        TerminationReason = terminationReason;
        Winner = winner;
        DefeatedPlayer = defeatedPlayer;
        StartedAtTick = startedAtTick;
        CompletedAtTick = completedAtTick;
        FinalizedAtTick = finalizedAtTick;
        LastTransitionAtTick = lastTransitionAtTick;
        TransitionCount = transitionCount;
    }

    public MatchLifecyclePhase Lifecycle { get; init; }

    public MatchOutcome Outcome { get; init; }

    public MatchTerminationReason TerminationReason { get; init; }

    public PlayerId Winner { get; init; }

    public PlayerId DefeatedPlayer { get; init; }

    public SimulationTick StartedAtTick { get; init; }

    public SimulationTick CompletedAtTick { get; init; }

    public SimulationTick FinalizedAtTick { get; init; }

    public SimulationTick LastTransitionAtTick { get; init; }

    public uint TransitionCount { get; init; }

    public MatchStatus Status =>
        Lifecycle switch
        {
            MatchLifecyclePhase.Initializing or
            MatchLifecyclePhase.Ready =>
                MatchStatus.Loading,
            MatchLifecyclePhase.Running or
            MatchLifecyclePhase.Paused =>
                MatchStatus.Active,
            MatchLifecyclePhase.Ending
                when Outcome == MatchOutcome.Victory =>
                MatchStatus.Victory,
            MatchLifecyclePhase.Ending
                when Outcome == MatchOutcome.Draw =>
                MatchStatus.Draw,
            MatchLifecyclePhase.Completed =>
                MatchStatus.Ended,
            _ =>
                throw new InvalidOperationException(
                    $"Match lifecycle '{Lifecycle}' and outcome '{Outcome}' do not form a valid public status.")
        };

    public static MatchState Initializing =>
        new(
            MatchLifecyclePhase.Initializing,
            MatchOutcome.None,
            MatchTerminationReason.None,
            PlayerId.None,
            PlayerId.None,
            SimulationTick.Zero,
            SimulationTick.Zero,
            SimulationTick.Zero,
            SimulationTick.Zero,
            0);

    public static MatchState Loading =>
        Initializing;

    public static MatchState Active =>
        CreateRunning();

    public static MatchState Running =>
        Active;

    public bool HasResult =>
        Outcome != MatchOutcome.None;

    public bool IsTerminal =>
        Lifecycle is
            MatchLifecyclePhase.Ending or
            MatchLifecyclePhase.Completed;

    public bool IsCompleted =>
        Lifecycle == MatchLifecyclePhase.Completed;

    public PlayerMatchStatus ForPlayer(PlayerId player)
    {
        if (!player.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(player));
        }

        return Lifecycle switch
        {
            MatchLifecyclePhase.Initializing or
            MatchLifecyclePhase.Ready =>
                PlayerMatchStatus.Loading,
            MatchLifecyclePhase.Running or
            MatchLifecyclePhase.Paused =>
                PlayerMatchStatus.Active,
            MatchLifecyclePhase.Ending
                when Outcome == MatchOutcome.Victory &&
                     Winner == player =>
                PlayerMatchStatus.Victory,
            MatchLifecyclePhase.Ending
                when Outcome == MatchOutcome.Victory =>
                PlayerMatchStatus.Defeat,
            MatchLifecyclePhase.Ending
                when Outcome == MatchOutcome.Draw =>
                PlayerMatchStatus.Draw,
            MatchLifecyclePhase.Completed =>
                PlayerMatchStatus.Ended,
            _ =>
                throw new InvalidOperationException(
                    $"Match lifecycle '{Lifecycle}' and outcome '{Outcome}' do not form a valid player status.")
        };
    }

    private static MatchState CreateRunning() =>
        Initializing with
        {
            Lifecycle = MatchLifecyclePhase.Running,
            TransitionCount = 2
        };

    private static MatchState CreateCompatibilityResult(
        MatchOutcome outcome,
        PlayerId winner,
        SimulationTick completedAtTick) =>
        Initializing with
        {
            Lifecycle = MatchLifecyclePhase.Ending,
            Outcome = outcome,
            Winner =
                outcome == MatchOutcome.Victory
                    ? winner
                    : PlayerId.None,
            CompletedAtTick = completedAtTick,
            LastTransitionAtTick = completedAtTick,
            TransitionCount = 3
        };

    private static MatchState CreateCompatibilityCompleted(
        PlayerId winner,
        SimulationTick completedAtTick) =>
        Initializing with
        {
            Lifecycle = MatchLifecyclePhase.Completed,
            Outcome =
                winner.IsSpecified
                    ? MatchOutcome.Victory
                    : MatchOutcome.None,
            Winner = winner,
            CompletedAtTick = completedAtTick,
            FinalizedAtTick = completedAtTick,
            LastTransitionAtTick = completedAtTick,
            TransitionCount = 4
        };
}

public readonly record struct MatchLifecycleDiagnosticsSnapshot(
    MatchLifecyclePhase Lifecycle,
    MatchOutcome Outcome,
    MatchTerminationReason TerminationReason,
    PlayerId Winner,
    PlayerId DefeatedPlayer,
    SimulationTick StartedAtTick,
    SimulationTick CompletedAtTick,
    SimulationTick FinalizedAtTick,
    SimulationTick LastTransitionAtTick,
    uint TransitionCount)
{
    public static MatchLifecycleDiagnosticsSnapshot Capture(
        in MatchState state) =>
        new(
            state.Lifecycle,
            state.Outcome,
            state.TerminationReason,
            state.Winner,
            state.DefeatedPlayer,
            state.StartedAtTick,
            state.CompletedAtTick,
            state.FinalizedAtTick,
            state.LastTransitionAtTick,
            state.TransitionCount);
}

public static class MatchLifecycle
{
    public static void MarkReady(
        EntityRegistry entities,
        EntityId matchStateEntity,
        SimulationTick tick = default)
    {
        MatchState state =
            RequireState(
                entities,
                matchStateEntity);

        if (state.Lifecycle !=
            MatchLifecyclePhase.Initializing)
        {
            throw InvalidTransition(
                state.Lifecycle,
                MatchLifecyclePhase.Ready);
        }

        SetState(
            entities,
            matchStateEntity,
            state with
            {
                Lifecycle = MatchLifecyclePhase.Ready,
                LastTransitionAtTick = tick,
                TransitionCount =
                    checked(state.TransitionCount + 1)
            });
    }

    public static void Start(
        EntityRegistry entities,
        EntityId matchStateEntity,
        SimulationTick tick = default)
    {
        MatchState state =
            RequireState(
                entities,
                matchStateEntity);

        if (state.Lifecycle !=
            MatchLifecyclePhase.Ready)
        {
            throw InvalidTransition(
                state.Lifecycle,
                MatchLifecyclePhase.Running);
        }

        SetState(
            entities,
            matchStateEntity,
            state with
            {
                Lifecycle = MatchLifecyclePhase.Running,
                StartedAtTick = tick,
                LastTransitionAtTick = tick,
                TransitionCount =
                    checked(state.TransitionCount + 1)
            });
    }

    public static bool TrySetPaused(
        EntityRegistry entities,
        EntityId matchStateEntity,
        bool paused,
        SimulationTick tick)
    {
        MatchState state =
            RequireState(
                entities,
                matchStateEntity);

        MatchLifecyclePhase desired =
            paused
                ? MatchLifecyclePhase.Paused
                : MatchLifecyclePhase.Running;

        if (state.Lifecycle == desired)
        {
            return true;
        }

        bool valid =
            paused
                ? state.Lifecycle ==
                    MatchLifecyclePhase.Running
                : state.Lifecycle ==
                    MatchLifecyclePhase.Paused;

        if (!valid)
        {
            return false;
        }

        SetState(
            entities,
            matchStateEntity,
            state with
            {
                Lifecycle = desired,
                LastTransitionAtTick = tick,
                TransitionCount =
                    checked(state.TransitionCount + 1)
            });
        return true;
    }

    public static void ResolveVictory(
        EntityRegistry entities,
        EntityId matchStateEntity,
        PlayerId winner,
        PlayerId defeatedPlayer,
        MatchTerminationReason reason,
        SimulationTick tick)
    {
        if (!winner.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(
                nameof(winner));
        }

        if (reason is
            MatchTerminationReason.None or
            MatchTerminationReason.MutualCommandCoreDestruction or
            MatchTerminationReason.AllParticipantsEliminated)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason));
        }

        MatchState state =
            RequireState(
                entities,
                matchStateEntity);

        if (state.Lifecycle !=
            MatchLifecyclePhase.Running)
        {
            throw InvalidTransition(
                state.Lifecycle,
                MatchLifecyclePhase.Ending);
        }

        SetState(
            entities,
            matchStateEntity,
            state with
            {
                Lifecycle = MatchLifecyclePhase.Ending,
                Outcome = MatchOutcome.Victory,
                TerminationReason = reason,
                Winner = winner,
                DefeatedPlayer = defeatedPlayer,
                CompletedAtTick = tick,
                LastTransitionAtTick = tick,
                TransitionCount =
                    checked(state.TransitionCount + 1)
            });
    }

    public static void ResolveDraw(
        EntityRegistry entities,
        EntityId matchStateEntity,
        MatchTerminationReason reason,
        SimulationTick tick)
    {
        if (reason is
            MatchTerminationReason.None or
            MatchTerminationReason.CommandCoreDestroyed or
            MatchTerminationReason.Surrender)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason));
        }

        MatchState state =
            RequireState(
                entities,
                matchStateEntity);

        if (state.Lifecycle !=
            MatchLifecyclePhase.Running)
        {
            throw InvalidTransition(
                state.Lifecycle,
                MatchLifecyclePhase.Ending);
        }

        SetState(
            entities,
            matchStateEntity,
            state with
            {
                Lifecycle = MatchLifecyclePhase.Ending,
                Outcome = MatchOutcome.Draw,
                TerminationReason = reason,
                Winner = PlayerId.None,
                DefeatedPlayer = PlayerId.None,
                CompletedAtTick = tick,
                LastTransitionAtTick = tick,
                TransitionCount =
                    checked(state.TransitionCount + 1)
            });
    }

    public static void Complete(
        EntityRegistry entities,
        EntityId matchStateEntity,
        SimulationTick tick)
    {
        MatchState state =
            RequireState(
                entities,
                matchStateEntity);

        if (state.Lifecycle !=
            MatchLifecyclePhase.Ending)
        {
            throw InvalidTransition(
                state.Lifecycle,
                MatchLifecyclePhase.Completed);
        }

        SetState(
            entities,
            matchStateEntity,
            state with
            {
                Lifecycle = MatchLifecyclePhase.Completed,
                FinalizedAtTick = tick,
                LastTransitionAtTick = tick,
                TransitionCount =
                    checked(state.TransitionCount + 1)
            });
    }

    private static MatchState RequireState(
        EntityRegistry entities,
        EntityId matchStateEntity)
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (!matchStateEntity.IsValid ||
            !entities.IsAlive(matchStateEntity) ||
            !entities.TryGetComponent(
                matchStateEntity,
                out MatchState state))
        {
            throw new InvalidOperationException(
                "The configured match-state entity is unavailable.");
        }

        return state;
    }

    private static void SetState(
        EntityRegistry entities,
        EntityId matchStateEntity,
        in MatchState state) =>
        entities.SetComponent(
            matchStateEntity,
            state);

    private static InvalidOperationException InvalidTransition(
        MatchLifecyclePhase from,
        MatchLifecyclePhase to) =>
        new(
            $"Match lifecycle cannot transition from '{from}' to '{to}'.");
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

public readonly record struct SurrenderedMatchParticipant(
    PlayerId Player,
    SimulationTick SurrenderedAtTick);

public enum SurrenderCommandFailureReason : byte
{
    None = 0,
    MatchNotRunning = 1,
    ParticipantNotFound = 2,
    AlreadySurrendered = 3
}

public sealed class SurrenderCommand : ISimulationCommand
{
    public SurrenderCommand(
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

    public SurrenderCommandFailureReason FailureReason { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;

        if (!context.Entities.TryGetComponent(
                MatchStateEntity,
                out MatchState state) ||
            state.Lifecycle !=
                MatchLifecyclePhase.Running)
        {
            Reject(
                SurrenderCommandFailureReason.MatchNotRunning);
            return;
        }

        foreach (EntityId objectiveEntity in
                 context.Entities.Query<CommandCoreObjective>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            CommandCoreObjective objective =
                context.Entities.GetComponent<CommandCoreObjective>(
                    objectiveEntity);

            if (objective.Owner != Issuer)
            {
                continue;
            }

            if (context.Entities.HasComponent<SurrenderedMatchParticipant>(
                    objectiveEntity))
            {
                Reject(
                    SurrenderCommandFailureReason.AlreadySurrendered);
                return;
            }

            context.Entities.AddComponent(
                objectiveEntity,
                new SurrenderedMatchParticipant(
                    Issuer,
                    context.Tick));
            Accepted = true;
            FailureReason =
                SurrenderCommandFailureReason.None;
            return;
        }

        Reject(
            SurrenderCommandFailureReason.ParticipantNotFound);
    }

    private void Reject(
        SurrenderCommandFailureReason reason)
    {
        Accepted = false;
        FailureReason = reason;
    }
}

public sealed class SetMatchPausedCommand : ISimulationCommand
{
    public SetMatchPausedCommand(
        EntityId matchStateEntity,
        bool paused)
    {
        if (!matchStateEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchStateEntity));
        }

        MatchStateEntity = matchStateEntity;
        Paused = paused;
    }

    public EntityId MatchStateEntity { get; }

    public bool Paused { get; }

    public bool Accepted { get; private set; }

    public SimulationTick ExecutedAtTick { get; private set; }

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ExecutedAtTick = context.Tick;
        Accepted =
            MatchLifecycle.TrySetPaused(
                context.Entities,
                MatchStateEntity,
                Paused,
                context.Tick);
    }
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
            state.Lifecycle !=
                MatchLifecyclePhase.Ending)
        {
            Accepted = false;
            return;
        }

        MatchLifecycle.Complete(
            context.Entities,
            MatchStateEntity,
            context.Tick);
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
            state.Lifecycle !=
                MatchLifecyclePhase.Running)
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
        PlayerId eliminatedPlayer = PlayerId.None;
        bool anySurrendered = false;

        for (int index = 0; index < _objectiveEntities.Count; index++)
        {
            EntityId objectiveEntity =
                _objectiveEntities[index];
            CommandCoreObjective objective =
                context.Entities.GetComponent<CommandCoreObjective>(
                    objectiveEntity);

            bool surrendered =
                context.Entities.HasComponent<SurrenderedMatchParticipant>(
                    objectiveEntity);
            anySurrendered |= surrendered;

            bool alive =
                !surrendered &&
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
            else
            {
                eliminatedPlayer = objective.Owner;
            }
        }

        if (surviving > 1)
        {
            return;
        }

        if (surviving == 1)
        {
            MatchLifecycle.ResolveVictory(
                context.Entities,
                _matchStateEntity,
                survivor,
                eliminatedPlayer,
                anySurrendered
                    ? MatchTerminationReason.Surrender
                    : MatchTerminationReason.CommandCoreDestroyed,
                context.Tick);
            return;
        }

        MatchLifecycle.ResolveDraw(
            context.Entities,
            _matchStateEntity,
            anySurrendered
                ? MatchTerminationReason.AllParticipantsEliminated
                : MatchTerminationReason.MutualCommandCoreDestruction,
            context.Tick);
    }

    public static EntityId CreateMatchStateEntity(
        EntityRegistry entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        EntityId entity =
            entities.CreateEntity();
        entities.AddComponent(
            entity,
            MatchState.Initializing);
        return entity;
    }

    public static void MarkMatchReady(
        EntityRegistry entities,
        EntityId matchStateEntity) =>
        MatchLifecycle.MarkReady(
            entities,
            matchStateEntity);

    public static void ActivateMatch(
        EntityRegistry entities,
        EntityId matchStateEntity) =>
        MatchLifecycle.Start(
            entities,
            matchStateEntity);

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
