using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum ReplayCommandKind : byte
{
    Movement = 1,
    Build = 2,
    Production = 3,
    UnitProduction = 4,
    Logistics = 5,
    Tactical = 6,
    EndMatch = 7,
    Surrender = 8,
    SetMatchPaused = 9
}

public sealed record RecordedSimulationCommand
{
    public required ReplayCommandKind Kind { get; init; }

    public required ulong TargetTick { get; init; }

    public required ulong Sequence { get; init; }

    public required ulong Source { get; init; }

    public ulong RecordingOrder { get; init; }

    public bool IsControl { get; init; }

    public PlayerId Issuer { get; init; }

    public ulong SubmittedAtTick { get; init; }

    public EntityId[] Entities { get; init; } = [];

    public EntityId TargetEntity { get; init; }

    public EntityId SecondaryEntity { get; init; }

    public BuildingId BuildingId { get; init; }

    public RecipeId RecipeId { get; init; }

    public UnitId UnitId { get; init; }

    public ResourceId ResourceId { get; init; }

    public Vector3 Position { get; init; }

    public BuildingOrientation BuildingOrientation { get; init; }

    public FormationTemplate Formation { get; init; }

    public ProductionPriority ProductionPriority { get; init; }

    public ProductionRequestMode ProductionMode { get; init; }

    public PlayerProductionOperation ProductionOperation { get; init; }

    public PlayerUnitProductionOperation UnitProductionOperation { get; init; }

    public PlayerLogisticsActionOperation LogisticsOperation { get; init; }

    public LogisticsStockPriority LogisticsPriority { get; init; }

    public BattlefieldSupplyPriority SupplyPriority { get; init; }

    public PlayerTacticalActionOperation TacticalOperation { get; init; }

    public IntelligenceContactKey ContactKey { get; init; }

    public bool Enabled { get; init; }

    public bool PreserveCombatIntent { get; init; }

    public double Value1 { get; init; }

    public double Value2 { get; init; }

    public double Value3 { get; init; }

    public int IntValue { get; init; }

    public ulong CorrelationId { get; init; }
}

public sealed class MatchReplayRecorder : IDisposable
{
    private readonly SimulationCoordinator _simulation;
    private readonly List<RecordedSimulationCommand> _commands = new();
    private readonly HashSet<string> _unsupported =
        new(StringComparer.Ordinal);
    private ulong _nextRecordingOrder = 1;
    private bool _disposed;

    public MatchReplayRecorder(
        SimulationCoordinator simulation)
    {
        _simulation =
            simulation ??
            throw new ArgumentNullException(nameof(simulation));

        if (_simulation.CurrentTick !=
            SimulationTick.Zero)
        {
            throw new InvalidOperationException(
                "Replay recording must begin before the first simulation tick.");
        }

        _simulation.CommandSubmitted +=
            OnCommandSubmitted;
        _simulation.ControlCommandExecuting +=
            OnControlCommandExecuting;
    }

    public bool IsComplete =>
        _unsupported.Count == 0;

    public IReadOnlyList<string> UnsupportedCommandTypes =>
        _unsupported
            .OrderBy(
                static value =>
                    value,
                StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<RecordedSimulationCommand> CaptureCommands() =>
        _commands
            .OrderBy(
                static command =>
                    command.RecordingOrder)
            .ToArray();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _simulation.CommandSubmitted -=
            OnCommandSubmitted;
        _simulation.ControlCommandExecuting -=
            OnControlCommandExecuting;
    }

    private void OnCommandSubmitted(
        SimulationCommandEnvelope envelope)
    {
        try
        {
            if (ReplayCommandCodec.TryEncode(
                    envelope,
                    out RecordedSimulationCommand? command))
            {
                _commands.Add(
                    command! with
                    {
                        RecordingOrder =
                            _nextRecordingOrder++,
                        IsControl = false
                    });
                return;
            }

            _unsupported.Add(
                envelope.CommandType.FullName ??
                envelope.CommandType.Name);
        }
        catch
        {
            _unsupported.Add(
                envelope.CommandType.FullName ??
                envelope.CommandType.Name);
        }
    }

    private void OnControlCommandExecuting(
        ISimulationCommand command,
        SimulationTick tick)
    {
        try
        {
            var envelope =
                new SimulationCommandEnvelope(
                    tick,
                    Sequence: 0,
                    SimulationCommandSource.None,
                    command);

            if (ReplayCommandCodec.TryEncode(
                    envelope,
                    out RecordedSimulationCommand? recorded))
            {
                _commands.Add(
                    recorded! with
                    {
                        RecordingOrder =
                            _nextRecordingOrder++,
                        IsControl = true
                    });
                return;
            }

            _unsupported.Add(
                command.GetType().FullName ??
                command.GetType().Name);
        }
        catch
        {
            _unsupported.Add(
                command.GetType().FullName ??
                command.GetType().Name);
        }
    }
}

public static class ReplayCommandCodec
{
    public static bool TryEncode(
        SimulationCommandEnvelope envelope,
        out RecordedSimulationCommand? recorded)
    {
        RecordedSimulationCommand Base(
            ReplayCommandKind kind) =>
            new()
            {
                Kind = kind,
                TargetTick =
                    envelope.TargetTick.Value,
                Sequence =
                    envelope.Sequence,
                Source =
                    envelope.Source.Value
            };

        switch (envelope.Command)
        {
            case MoveEntitiesCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.Movement) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        Entities =
                            command.Targets.ToArray(),
                        Position =
                            command.WorldTarget,
                        Formation =
                            command.Formation,
                        PreserveCombatIntent =
                            command.PreserveCombatIntent
                    };
                return true;

            case BuildCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.Build) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        SecondaryEntity =
                            command.SourceInventory,
                        BuildingId =
                            command.BuildingId,
                        Position =
                            command.Position,
                        BuildingOrientation =
                            command.Orientation,
                        CorrelationId =
                            command.CorrelationId.Value
                    };
                return true;

            case PlayerProductionActionCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.Production) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        TargetEntity =
                            command.Facility,
                        SecondaryEntity =
                            command.RequestEntity,
                        RecipeId =
                            command.RecipeId,
                        ProductionPriority =
                            command.Priority,
                        ProductionMode =
                            command.Mode,
                        ProductionOperation =
                            command.Operation,
                        ResourceId =
                            command.DesiredStockResourceId,
                        Enabled =
                            command.Paused,
                        Value1 =
                            command.DesiredStockQuantity
                    };
                return true;

            case PlayerUnitProductionActionCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.UnitProduction) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        TargetEntity =
                            command.Facility,
                        SecondaryEntity =
                            command.RequestEntity,
                        UnitId =
                            command.UnitId,
                        ProductionPriority =
                            command.Priority,
                        UnitProductionOperation =
                            command.Operation,
                        Position =
                            command.RallyPoint
                    };
                return true;

            case PlayerLogisticsActionCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.Logistics) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        TargetEntity =
                            command.Target,
                        SecondaryEntity =
                            command.PolicyEntity,
                        ResourceId =
                            command.ResourceId,
                        LogisticsOperation =
                            command.Operation,
                        LogisticsPriority =
                            command.Priority,
                        SupplyPriority =
                            command.SupplyPriority,
                        Enabled =
                            command.Enabled,
                        Value1 =
                            command.DesiredMinimum,
                        Value2 =
                            command.DesiredTarget,
                        Value3 =
                            command.DesiredMaximum,
                        IntValue = 0,
                        CorrelationId = 0,
                        PreserveCombatIntent = false,
                        Position = default
                    } with
                    {
                        Value1 =
                            command.Operation ==
                            PlayerLogisticsActionOperation
                                .SetAutomaticResupplyPolicy
                                ? command.AmmunitionThreshold
                                : command.DesiredMinimum,
                        Value2 =
                            command.Operation ==
                            PlayerLogisticsActionOperation
                                .SetAutomaticResupplyPolicy
                                ? command.FuelThreshold
                                : command.DesiredTarget
                    };
                return true;

            case PlayerTacticalActionCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.Tactical) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        Entities =
                            command.Units.ToArray(),
                        TargetEntity =
                            command.Target,
                        Position =
                            command.WorldTarget,
                        ContactKey =
                            command.ContactKey,
                        Formation =
                            command.Formation,
                        IntValue =
                            command.RequestedRounds,
                        TacticalOperation =
                            command.Operation
                    };
                return true;

            case EndMatchCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.EndMatch) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        TargetEntity =
                            command.MatchStateEntity
                    };
                return true;

            case SurrenderCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.Surrender) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        TargetEntity =
                            command.MatchStateEntity
                    };
                return true;

            case SetMatchPausedCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.SetMatchPaused) with
                    {
                        TargetEntity =
                            command.MatchStateEntity,
                        Enabled =
                            command.Paused
                    };
                return true;

            default:
                recorded = null;
                return false;
        }
    }

    public static ISimulationCommand Decode(
        RecordedSimulationCommand command,
        VerticalSliceScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(scenario);

        var submittedAtTick =
            new SimulationTick(
                command.SubmittedAtTick);

        return command.Kind switch
        {
            ReplayCommandKind.Movement =>
                new MoveEntitiesCommand(
                    command.Issuer,
                    command.Entities,
                    command.Position,
                    submittedAtTick,
                    command.Formation,
                    command.PreserveCombatIntent),

            ReplayCommandKind.Build =>
                new BuildCommand(
                    command.Issuer,
                    command.BuildingId,
                    command.Position,
                    command.BuildingOrientation,
                    command.SecondaryEntity,
                    submittedAtTick,
                    new PlayerCommandCorrelationId(
                        command.CorrelationId)),

            ReplayCommandKind.Production =>
                DecodeProduction(
                    command,
                    submittedAtTick),

            ReplayCommandKind.UnitProduction =>
                DecodeUnitProduction(
                    command,
                    submittedAtTick),

            ReplayCommandKind.Logistics =>
                DecodeLogistics(
                    command,
                    submittedAtTick),

            ReplayCommandKind.Tactical =>
                DecodeTactical(
                    command,
                    submittedAtTick,
                    scenario),

            ReplayCommandKind.EndMatch =>
                new EndMatchCommand(
                    command.Issuer,
                    command.TargetEntity,
                    submittedAtTick),

            ReplayCommandKind.Surrender =>
                new SurrenderCommand(
                    command.Issuer,
                    command.TargetEntity,
                    submittedAtTick),

            ReplayCommandKind.SetMatchPaused =>
                new SetMatchPausedCommand(
                    command.TargetEntity,
                    command.Enabled),

            _ =>
                throw new InvalidDataException(
                    $"Replay command kind '{command.Kind}' is not supported.")
        };
    }

    private static PlayerProductionActionCommand DecodeProduction(
        RecordedSimulationCommand command,
        SimulationTick submittedAtTick) =>
        command.ProductionOperation switch
        {
            PlayerProductionOperation.Queue =>
                PlayerProductionActionCommand.Queue(
                    command.Issuer,
                    command.TargetEntity,
                    command.RecipeId,
                    submittedAtTick,
                    command.ProductionPriority,
                    command.ProductionMode,
                    command.ResourceId,
                    command.Value1),

            PlayerProductionOperation.SetPaused =>
                PlayerProductionActionCommand.SetPaused(
                    command.Issuer,
                    command.SecondaryEntity,
                    command.Enabled,
                    submittedAtTick),

            PlayerProductionOperation.Cancel =>
                PlayerProductionActionCommand.Cancel(
                    command.Issuer,
                    command.SecondaryEntity,
                    submittedAtTick),

            _ =>
                throw new InvalidDataException(
                    $"Production replay operation '{command.ProductionOperation}' is invalid.")
        };

    private static PlayerUnitProductionActionCommand DecodeUnitProduction(
        RecordedSimulationCommand command,
        SimulationTick submittedAtTick) =>
        command.UnitProductionOperation switch
        {
            PlayerUnitProductionOperation.Queue =>
                PlayerUnitProductionActionCommand.Queue(
                    command.Issuer,
                    command.TargetEntity,
                    command.UnitId,
                    submittedAtTick,
                    command.ProductionPriority),

            PlayerUnitProductionOperation.Cancel =>
                PlayerUnitProductionActionCommand.Cancel(
                    command.Issuer,
                    command.SecondaryEntity,
                    submittedAtTick),

            PlayerUnitProductionOperation.SetRallyPoint =>
                PlayerUnitProductionActionCommand.SetRallyPoint(
                    command.Issuer,
                    command.TargetEntity,
                    command.Position,
                    submittedAtTick),

            PlayerUnitProductionOperation.SetPriority =>
                PlayerUnitProductionActionCommand.SetPriority(
                    command.Issuer,
                    command.SecondaryEntity,
                    command.ProductionPriority,
                    submittedAtTick),

            _ =>
                throw new InvalidDataException(
                    $"Unit-production replay operation '{command.UnitProductionOperation}' is invalid.")
        };

    private static PlayerLogisticsActionCommand DecodeLogistics(
        RecordedSimulationCommand command,
        SimulationTick submittedAtTick) =>
        command.LogisticsOperation switch
        {
            PlayerLogisticsActionOperation.SetStockPolicy =>
                PlayerLogisticsActionCommand.SetStockPolicy(
                    command.Issuer,
                    command.TargetEntity,
                    command.ResourceId,
                    command.Value1,
                    command.Value2,
                    command.Value3,
                    command.LogisticsPriority,
                    command.Enabled,
                    submittedAtTick),

            PlayerLogisticsActionOperation.RemoveStockPolicy =>
                PlayerLogisticsActionCommand.RemoveStockPolicy(
                    command.Issuer,
                    command.SecondaryEntity,
                    submittedAtTick),

            PlayerLogisticsActionOperation.SetAutomaticResupplyPolicy =>
                PlayerLogisticsActionCommand.SetAutomaticResupplyPolicy(
                    command.Issuer,
                    command.TargetEntity,
                    command.Value1,
                    command.Value2,
                    command.Enabled,
                    submittedAtTick),

            PlayerLogisticsActionOperation.RequestResupply =>
                PlayerLogisticsActionCommand.RequestResupply(
                    command.Issuer,
                    command.TargetEntity,
                    submittedAtTick),

            PlayerLogisticsActionOperation.SetSupplyPriority =>
                PlayerLogisticsActionCommand.SetSupplyPriority(
                    command.Issuer,
                    command.TargetEntity,
                    command.SupplyPriority,
                    submittedAtTick),

            _ =>
                throw new InvalidDataException(
                    $"Logistics replay operation '{command.LogisticsOperation}' is invalid.")
        };

    private static PlayerTacticalActionCommand DecodeTactical(
        RecordedSimulationCommand command,
        SimulationTick submittedAtTick,
        VerticalSliceScenario scenario) =>
        command.TacticalOperation switch
        {
            PlayerTacticalActionOperation.Attack =>
                PlayerTacticalActionCommand.Attack(
                    command.Issuer,
                    command.Entities,
                    command.TargetEntity,
                    submittedAtTick,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.AttackMove =>
                PlayerTacticalActionCommand.AttackMove(
                    command.Issuer,
                    command.Entities,
                    command.Position,
                    submittedAtTick,
                    command.Formation,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.Stop =>
                PlayerTacticalActionCommand.Stop(
                    command.Issuer,
                    command.Entities,
                    submittedAtTick,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.HoldPosition =>
                PlayerTacticalActionCommand.HoldPosition(
                    command.Issuer,
                    command.Entities,
                    submittedAtTick,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.Retreat =>
                PlayerTacticalActionCommand.Retreat(
                    command.Issuer,
                    command.Entities,
                    command.Position,
                    submittedAtTick,
                    command.Formation,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.RetreatToRecovery =>
                PlayerTacticalActionCommand.RetreatToRecovery(
                    command.Issuer,
                    command.Entities,
                    submittedAtTick,
                    command.Formation,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.FireMissionCoordinate =>
                PlayerTacticalActionCommand.FireMissionCoordinate(
                    command.Issuer,
                    command.Entities,
                    command.Position,
                    command.IntValue,
                    submittedAtTick,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.FireMissionContact =>
                PlayerTacticalActionCommand.FireMissionContact(
                    command.Issuer,
                    command.Entities,
                    command.ContactKey,
                    command.IntValue,
                    submittedAtTick,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            PlayerTacticalActionOperation.CancelFireMission =>
                PlayerTacticalActionCommand.CancelFireMission(
                    command.Issuer,
                    command.Entities,
                    submittedAtTick,
                    scenario.Intelligence,
                    scenario.Services.Weapons,
                    scenario.Services.ArtilleryWeapons),

            _ =>
                throw new InvalidDataException(
                    $"Tactical replay operation '{command.TacticalOperation}' is invalid.")
        };
}
