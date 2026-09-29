using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct PlayerCommandCorrelationId(ulong Value)
{
    public static PlayerCommandCorrelationId None => default;

    public bool IsSpecified => Value != 0;
}

public enum PlayerCommandKind : byte
{
    Movement = 1,
    Construction = 2,
    EndMatch = 3,
    Production = 4,
    UnitProduction = 5
}

public enum PlayerCommandSubmissionFailure : byte
{
    None = 0,
    BoundaryFull = 1
}

public readonly record struct PlayerCommandSubmissionReceipt(
    SimulationSessionId SessionId,
    PlayerCommandCorrelationId CorrelationId,
    PlayerCommandKind Kind,
    SimulationCommandSource Source,
    SimulationTick SubmittedAtTick,
    SimulationTick TargetTick,
    ulong Sequence,
    bool Accepted,
    PlayerCommandSubmissionFailure Failure);

public readonly record struct PlayerCommandResultReadModel(
    SimulationSessionId SessionId,
    PlayerCommandCorrelationId CorrelationId,
    PlayerCommandKind Kind,
    PlayerCommandFeedbackState State,
    int AcceptedTargets,
    int RejectedTargets,
    BuildCommandRejectionReason BuildRejection,
    BuildingPlacementFailureReason PlacementFailure,
    SimulationTick ResolvedAtTick);

public sealed class PlayerCommandResultBuffer
{
    private readonly object _gate = new();
    private readonly Queue<PlayerCommandResultReadModel> _results;
    private readonly int _capacity;

    public PlayerCommandResultBuffer(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _results = new Queue<PlayerCommandResultReadModel>(capacity);
    }

    public int Capacity => _capacity;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _results.Count;
            }
        }
    }

    internal bool TryPublish(in PlayerCommandResultReadModel result)
    {
        lock (_gate)
        {
            if (_results.Count >= _capacity)
            {
                return false;
            }

            _results.Enqueue(result);
            return true;
        }
    }

    public bool TryRead(out PlayerCommandResultReadModel result)
    {
        lock (_gate)
        {
            if (_results.Count == 0)
            {
                result = default;
                return false;
            }

            result = _results.Dequeue();
            return true;
        }
    }
}

public sealed class PlayerCommandGateway : ISimulationTickObserver
{
    private readonly SimulationCoordinator _simulation;
    private readonly BuildingCommandProcessingSystem _buildingCommands;
    private readonly EntityId _matchStateEntity;
    private readonly PlayerCommandResultBuffer _results;
    private readonly List<PendingCommand> _pending = new();
    private readonly int _maximumOutstanding;
    private ulong _nextCorrelationId = 1;

    public PlayerCommandGateway(
        SimulationCoordinator simulation,
        BuildingCommandProcessingSystem buildingCommands,
        EntityId matchStateEntity,
        int maximumOutstanding = 128)
    {
        _simulation =
            simulation ??
            throw new ArgumentNullException(nameof(simulation));
        _buildingCommands =
            buildingCommands ??
            throw new ArgumentNullException(nameof(buildingCommands));

        if (!matchStateEntity.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(matchStateEntity));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumOutstanding,
            1);

        _matchStateEntity = matchStateEntity;
        _maximumOutstanding = maximumOutstanding;
        _results =
            new PlayerCommandResultBuffer(
                maximumOutstanding);
    }

    public SimulationSessionId SessionId =>
        _simulation.SessionId;

    public PlayerCommandResultBuffer Results =>
        _results;

    public PlayerCommandFeedback LatestFeedback { get; private set; } =
        PlayerCommandFeedback.None;

    public int PendingCount =>
        _pending.Count;

    public int OutstandingCount =>
        _pending.Count + _results.Count;

    public PlayerCommandSubmissionReceipt SubmitMovement(
        PlayerId issuer,
        ReadOnlySpan<EntityId> targets,
        Vector3 worldTarget,
        SimulationTick observedTick,
        FormationTemplate formation)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.Movement,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        var command =
            new MoveEntitiesCommand(
                issuer,
                targets,
                worldTarget,
                observedTick,
                formation);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForMovement(
                correlation,
                envelope,
                command));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.Movement,
            source,
            observedTick,
            envelope);
    }

    public PlayerCommandSubmissionReceipt SubmitBuild(
        PlayerId issuer,
        BuildingId buildingId,
        Vector3 position,
        BuildingOrientation orientation,
        EntityId sourceInventory,
        SimulationTick observedTick)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.Construction,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        var command =
            new BuildCommand(
                issuer,
                buildingId,
                position,
                orientation,
                sourceInventory,
                observedTick,
                correlation);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForBuild(
                correlation,
                envelope));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.Construction,
            source,
            observedTick,
            envelope);
    }


    public PlayerCommandSubmissionReceipt SubmitProduction(
        PlayerId issuer,
        EntityId facility,
        RecipeId recipeId,
        SimulationTick observedTick,
        ProductionPriority priority = ProductionPriority.Normal,
        ProductionRequestMode mode = ProductionRequestMode.OneShot,
        ResourceId desiredStockResourceId = default,
        double desiredStockQuantity = 0.0)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.Production,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        PlayerProductionActionCommand command =
            PlayerProductionActionCommand.Queue(
                issuer,
                facility,
                recipeId,
                observedTick,
                priority,
                mode,
                desiredStockResourceId,
                desiredStockQuantity);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForProduction(
                correlation,
                envelope,
                command));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.Production,
            source,
            observedTick,
            envelope);
    }

    public PlayerCommandSubmissionReceipt SubmitProductionPaused(
        PlayerId issuer,
        EntityId requestEntity,
        bool paused,
        SimulationTick observedTick)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.Production,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        PlayerProductionActionCommand command =
            PlayerProductionActionCommand.SetPaused(
                issuer,
                requestEntity,
                paused,
                observedTick);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForProduction(
                correlation,
                envelope,
                command));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.Production,
            source,
            observedTick,
            envelope);
    }

    public PlayerCommandSubmissionReceipt SubmitProductionCancel(
        PlayerId issuer,
        EntityId requestEntity,
        SimulationTick observedTick)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.Production,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        PlayerProductionActionCommand command =
            PlayerProductionActionCommand.Cancel(
                issuer,
                requestEntity,
                observedTick);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForProduction(
                correlation,
                envelope,
                command));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.Production,
            source,
            observedTick,
            envelope);
    }

    public PlayerCommandSubmissionReceipt SubmitUnitProduction(
        PlayerId issuer,
        EntityId facility,
        UnitId unitId,
        SimulationTick observedTick,
        ProductionPriority priority = ProductionPriority.Normal)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.UnitProduction,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        PlayerUnitProductionActionCommand command =
            PlayerUnitProductionActionCommand.Queue(
                issuer,
                facility,
                unitId,
                observedTick,
                priority);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForUnitProduction(
                correlation,
                envelope,
                command));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.UnitProduction,
            source,
            observedTick,
            envelope);
    }

    public PlayerCommandSubmissionReceipt SubmitUnitProductionCancel(
        PlayerId issuer,
        EntityId requestEntity,
        SimulationTick observedTick)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.UnitProduction,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        PlayerUnitProductionActionCommand command =
            PlayerUnitProductionActionCommand.Cancel(
                issuer,
                requestEntity,
                observedTick);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForUnitProduction(
                correlation,
                envelope,
                command));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.UnitProduction,
            source,
            observedTick,
            envelope);
    }

    public PlayerCommandSubmissionReceipt SubmitEndMatch(
        PlayerId issuer,
        SimulationTick observedTick)
    {
        if (!TryBeginSubmission(
                PlayerCommandKind.EndMatch,
                issuer,
                observedTick,
                out PlayerCommandCorrelationId correlation,
                out SimulationTick targetTick,
                out SimulationCommandSource source,
                out PlayerCommandSubmissionReceipt rejected))
        {
            return rejected;
        }

        var command =
            new EndMatchCommand(
                issuer,
                _matchStateEntity,
                observedTick);

        SimulationCommandEnvelope envelope =
            _simulation.SubmitCommand(
                command,
                targetTick,
                source);

        _pending.Add(
            PendingCommand.ForEndMatch(
                correlation,
                envelope,
                command));

        return AcceptedReceipt(
            correlation,
            PlayerCommandKind.EndMatch,
            source,
            observedTick,
            envelope);
    }

    public void OnTickCompleted(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Tick == SimulationTick.Zero)
        {
            return;
        }

        for (int index = 0;
             index < _pending.Count;)
        {
            PendingCommand pending =
                _pending[index];

            if (pending.Result is null)
            {
                PlayerCommandResultReadModel? resolved =
                    TryResolve(
                        pending,
                        context.Tick);

                if (resolved is null)
                {
                    index++;
                    continue;
                }

                pending =
                    pending with
                    {
                        Result = resolved
                    };
                _pending[index] = pending;
                LatestFeedback =
                    ToFeedback(
                        resolved.Value);
            }

            if (!_results.TryPublish(
                    pending.Result!.Value))
            {
                break;
            }

            _pending.RemoveAt(index);
        }
    }

    private PlayerCommandResultReadModel? TryResolve(
        in PendingCommand pending,
        SimulationTick completedTick)
    {
        switch (pending.Kind)
        {
            case PlayerCommandKind.Movement:
            {
                MoveEntitiesCommand command =
                    pending.MovementCommand!;

                if (command.ExecutedAtTick == SimulationTick.Zero)
                {
                    return null;
                }

                int accepted =
                    command.AcceptedTargetCount;
                int rejected =
                    command.RejectedTargetCount;

                return new PlayerCommandResultReadModel(
                    SessionId,
                    pending.CorrelationId,
                    pending.Kind,
                    ResolveState(
                        accepted,
                        rejected),
                    accepted,
                    rejected,
                    BuildCommandRejectionReason.None,
                    BuildingPlacementFailureReason.None,
                    command.ExecutedAtTick);
            }

            case PlayerCommandKind.Construction:
            {
                if (!_buildingCommands.TryTakeResult(
                        pending.CorrelationId,
                        out BuildCommandResult buildResult))
                {
                    return null;
                }

                return new PlayerCommandResultReadModel(
                    SessionId,
                    pending.CorrelationId,
                    pending.Kind,
                    buildResult.Accepted
                        ? PlayerCommandFeedbackState.Accepted
                        : PlayerCommandFeedbackState.Rejected,
                    buildResult.Accepted
                        ? 1
                        : 0,
                    buildResult.Accepted
                        ? 0
                        : 1,
                    buildResult.RejectionReason,
                    buildResult.PlacementFailure,
                    buildResult.ResolvedAtTick);
            }

            case PlayerCommandKind.Production:
            {
                PlayerProductionActionCommand command =
                    pending.ProductionCommand!;

                if (command.ExecutedAtTick == SimulationTick.Zero)
                {
                    return null;
                }

                return CreateActionResult(
                    pending,
                    command.Accepted,
                    command.ExecutedAtTick);
            }

            case PlayerCommandKind.UnitProduction:
            {
                PlayerUnitProductionActionCommand command =
                    pending.UnitProductionCommand!;

                if (command.ExecutedAtTick == SimulationTick.Zero)
                {
                    return null;
                }

                return CreateActionResult(
                    pending,
                    command.Accepted,
                    command.ExecutedAtTick);
            }

            case PlayerCommandKind.EndMatch:
            {
                EndMatchCommand command =
                    pending.EndMatchCommand!;

                if (command.ExecutedAtTick == SimulationTick.Zero)
                {
                    return null;
                }

                return new PlayerCommandResultReadModel(
                    SessionId,
                    pending.CorrelationId,
                    pending.Kind,
                    command.Accepted
                        ? PlayerCommandFeedbackState.Accepted
                        : PlayerCommandFeedbackState.Rejected,
                    command.Accepted
                        ? 1
                        : 0,
                    command.Accepted
                        ? 0
                        : 1,
                    BuildCommandRejectionReason.None,
                    BuildingPlacementFailureReason.None,
                    command.ExecutedAtTick);
            }

            default:
                throw new InvalidOperationException(
                    $"Unsupported player command kind '{pending.Kind}'.");
        }
    }

    private PlayerCommandResultReadModel CreateActionResult(
        in PendingCommand pending,
        bool accepted,
        SimulationTick executedAtTick) =>
        new(
            SessionId,
            pending.CorrelationId,
            pending.Kind,
            accepted
                ? PlayerCommandFeedbackState.Accepted
                : PlayerCommandFeedbackState.Rejected,
            accepted ? 1 : 0,
            accepted ? 0 : 1,
            BuildCommandRejectionReason.None,
            BuildingPlacementFailureReason.None,
            executedAtTick);

    private bool TryBeginSubmission(
        PlayerCommandKind kind,
        PlayerId issuer,
        SimulationTick observedTick,
        out PlayerCommandCorrelationId correlation,
        out SimulationTick targetTick,
        out SimulationCommandSource source,
        out PlayerCommandSubmissionReceipt rejected)
    {
        if (!issuer.IsSpecified)
        {
            throw new ArgumentOutOfRangeException(nameof(issuer));
        }

        correlation =
            AllocateCorrelationId();
        targetTick =
            _simulation.CurrentTick.Next();
        source =
            new SimulationCommandSource(
                issuer.Value);

        if (OutstandingCount >=
            _maximumOutstanding)
        {
            rejected =
                new PlayerCommandSubmissionReceipt(
                    SessionId,
                    correlation,
                    kind,
                    source,
                    observedTick,
                    targetTick,
                    0,
                    Accepted: false,
                    PlayerCommandSubmissionFailure.BoundaryFull);
            return false;
        }

        rejected = default;
        return true;
    }

    private PlayerCommandCorrelationId AllocateCorrelationId()
    {
        ulong value =
            _nextCorrelationId++;

        if (value == 0)
        {
            throw new OverflowException(
                "Player command correlation identifier space has been exhausted.");
        }

        return new PlayerCommandCorrelationId(
            value);
    }

    private PlayerCommandSubmissionReceipt AcceptedReceipt(
        PlayerCommandCorrelationId correlation,
        PlayerCommandKind kind,
        SimulationCommandSource source,
        SimulationTick submittedAtTick,
        in SimulationCommandEnvelope envelope) =>
        new(
            SessionId,
            correlation,
            kind,
            source,
            submittedAtTick,
            envelope.TargetTick,
            envelope.Sequence,
            Accepted: true,
            PlayerCommandSubmissionFailure.None);

    private static PlayerCommandFeedbackState ResolveState(
        int accepted,
        int rejected) =>
        accepted > 0 && rejected > 0
            ? PlayerCommandFeedbackState.Partial
            : accepted > 0
                ? PlayerCommandFeedbackState.Accepted
                : rejected > 0
                    ? PlayerCommandFeedbackState.Rejected
                    : PlayerCommandFeedbackState.None;

    private static PlayerCommandFeedback ToFeedback(
        in PlayerCommandResultReadModel result) =>
        new(
            result.Kind switch
            {
                PlayerCommandKind.Movement =>
                    PlayerCommandFeedbackKind.Movement,
                PlayerCommandKind.Construction =>
                    PlayerCommandFeedbackKind.Construction,
                PlayerCommandKind.Production =>
                    PlayerCommandFeedbackKind.Production,
                PlayerCommandKind.UnitProduction =>
                    PlayerCommandFeedbackKind.UnitProduction,
                _ =>
                    PlayerCommandFeedbackKind.None
            },
            result.State,
            result.AcceptedTargets,
            result.RejectedTargets,
            result.BuildRejection,
            result.PlacementFailure,
            result.ResolvedAtTick);

    private readonly record struct PendingCommand(
        PlayerCommandCorrelationId CorrelationId,
        PlayerCommandKind Kind,
        SimulationCommandEnvelope Envelope,
        MoveEntitiesCommand? MovementCommand,
        EndMatchCommand? EndMatchCommand,
        PlayerProductionActionCommand? ProductionCommand,
        PlayerUnitProductionActionCommand? UnitProductionCommand,
        PlayerCommandResultReadModel? Result)
    {
        public static PendingCommand ForMovement(
            PlayerCommandCorrelationId correlation,
            in SimulationCommandEnvelope envelope,
            MoveEntitiesCommand command) =>
            new(
                correlation,
                PlayerCommandKind.Movement,
                envelope,
                command,
                null,
                null,
                null,
                null);

        public static PendingCommand ForBuild(
            PlayerCommandCorrelationId correlation,
            in SimulationCommandEnvelope envelope) =>
            new(
                correlation,
                PlayerCommandKind.Construction,
                envelope,
                null,
                null,
                null,
                null,
                null);

        public static PendingCommand ForEndMatch(
            PlayerCommandCorrelationId correlation,
            in SimulationCommandEnvelope envelope,
            EndMatchCommand command) =>
            new(
                correlation,
                PlayerCommandKind.EndMatch,
                envelope,
                null,
                command,
                null,
                null,
                null);

        public static PendingCommand ForProduction(
            PlayerCommandCorrelationId correlation,
            in SimulationCommandEnvelope envelope,
            PlayerProductionActionCommand command) =>
            new(
                correlation,
                PlayerCommandKind.Production,
                envelope,
                null,
                null,
                command,
                null,
                null);

        public static PendingCommand ForUnitProduction(
            PlayerCommandCorrelationId correlation,
            in SimulationCommandEnvelope envelope,
            PlayerUnitProductionActionCommand command) =>
            new(
                correlation,
                PlayerCommandKind.UnitProduction,
                envelope,
                null,
                null,
                null,
                command,
                null);
    }
}
