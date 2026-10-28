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
    SetMatchPaused = 9,
    Technology = 10,
    Demolition = 11,
    CancelConstruction = 12,
    OperationsRule = 13,
    ConveyorConstruction = 14,
    DeveloperAction = 15,
    ConveyorRouting = 16,
    BridgeRepair = 17,
    QueuedOrders = 18
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

    public TechnologyId TechnologyId { get; init; }

    public ResourceId ResourceId { get; init; }

    public Vector3 Position { get; init; }

    public BuildingOrientation BuildingOrientation { get; init; }

    public FormationTemplate Formation { get; init; }

    public ProductionPriority ProductionPriority { get; init; }

    public ProductionRequestMode ProductionMode { get; init; }

    public PlayerProductionOperation ProductionOperation { get; init; }

    public PlayerUnitProductionOperation UnitProductionOperation { get; init; }

    public PlayerTechnologyOperation TechnologyOperation { get; init; }

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

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ConveyorBuildRequest ConveyorRequest { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public QueuedOrderEdit QueueEdit { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public QueuedOrderKind QueueKind { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Patrol { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public uint InfrastructureRevision { get; init; }

    public ConveyorRoutingPreference RoutingPreference { get; init; }
    public uint RoutingRevision { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DeveloperCommandPayload? DeveloperPayload { get; init; }

    public ulong OperationsRuleId { get; init; }
    public uint OperationsRuleRevision { get; init; }
    public uint OperationsCooldownTicks { get; init; }
    public OperationsRuleOperation OperationsOperation { get; init; }
    public OperationsRuleTemplate OperationsTemplate { get; init; }
}

public sealed class MatchReplayRecorder : IDisposable
{
    private readonly SimulationCoordinator _simulation;
    private readonly List<RecordedSimulationCommand> _commands = new();
    private readonly HashSet<string> _unsupported =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, IDeveloperRecordedCommand> _developerSources = new();
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
            .Select(command => _developerSources.TryGetValue(command.RecordingOrder, out var source)
                ? command with { DeveloperPayload = source.CapturePayload() } : command)
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
                if (envelope.Command is IDeveloperRecordedCommand developer) _developerSources.Add(_nextRecordingOrder, developer);
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
#if FORGELINE_DEVELOPER_TOOLS
            case DeveloperSimulationCommand command:
                if (envelope.Sequence == 0) { recorded = null; return false; }
                recorded = Base(ReplayCommandKind.DeveloperAction) with { DeveloperPayload = command.CapturePayload() };
                return true;
#endif
            case QueuedOrdersCommand command:
                recorded = Base(ReplayCommandKind.QueuedOrders) with
                {
                    Issuer = command.Issuer, Entities = command.Units.ToArray(),
                    SubmittedAtTick = command.SubmittedAtTick.Value, Position = command.Order.Destination,
                    TargetEntity = command.Order.Target, Formation = command.Order.Formation,
                    QueueKind = command.Order.Kind, QueueEdit = command.Edit, Patrol = command.Patrol
                };
                return true;
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

            case DemolishBuildingsCommand command:
                recorded = Base(ReplayCommandKind.Demolition) with
                {
                    Issuer = command.Issuer,
                    SubmittedAtTick = command.SubmittedAtTick.Value,
                    Entities = command.Targets.ToArray(),
                    CorrelationId = command.CorrelationId.Value
                };
                return true;

            case CancelConstructionCommand command:
                recorded = Base(ReplayCommandKind.CancelConstruction) with
                {
                    Issuer = command.Issuer,
                    SubmittedAtTick = command.SubmittedAtTick.Value,
                    TargetEntity = command.ConstructionSite
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

            case PlayerTechnologyActionCommand command:
                recorded =
                    Base(
                        ReplayCommandKind.Technology) with
                    {
                        Issuer = command.Issuer,
                        SubmittedAtTick =
                            command.SubmittedAtTick.Value,
                        TargetEntity =
                            command.Facility,
                        SecondaryEntity =
                            command.RequestEntity,
                        TechnologyId =
                            command.TechnologyId,
                        TechnologyOperation =
                            command.Operation,
                        Entities =
                            command.SourceInventory.IsValid
                                ? [command.SourceInventory]
                                : []
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
                        Entities =
                            command.Operation == PlayerLogisticsActionOperation.CreateCargoConvoy
                                ? [.. command.ConvoyTrucks.ToArray(), .. command.ConvoyEscorts.ToArray()]
                                : [],
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
                        IntValue = command.Operation == PlayerLogisticsActionOperation.CreateCargoConvoy
                            ? command.ConvoyTrucks.Length : 0,
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

            case RepairBridgeCommand command:
                recorded = Base(ReplayCommandKind.BridgeRepair) with
                { Issuer = command.Issuer, SubmittedAtTick = command.SubmittedAtTick.Value,
                  Entities = [command.Engineer], TargetEntity = command.Bridge,
                  SecondaryEntity = command.MaterialSource, InfrastructureRevision = command.ExpectedRevision };
                return true;

            case ConveyorRoutingCommand command:
                recorded = Base(ReplayCommandKind.ConveyorRouting) with
                { Issuer = command.Issuer, SubmittedAtTick = command.SubmittedAtTick.Value,
                  TargetEntity = command.Target, RoutingPreference = command.Preference,
                  RoutingRevision = command.ExpectedRevision };
                return true;

            case ConveyorConstructionCommand command:
                recorded = Base(ReplayCommandKind.ConveyorConstruction) with
                { Issuer = command.Issuer, SubmittedAtTick = command.SubmittedAtTick.Value,
                  TargetEntity = command.Payer, ConveyorRequest = command.Request };
                return true;

            case OperationsRuleCommand command:
                recorded = Base(ReplayCommandKind.OperationsRule) with
                {
                    Issuer = command.Definition.Owner,
                    SubmittedAtTick = command.SubmittedAtTick.Value,
                    TargetEntity = command.Definition.Facility,
                    ResourceId = command.Definition.Resource,
                    OperationsRuleId = command.Definition.Id,
                    OperationsRuleRevision = command.ExpectedRevision,
                    OperationsCooldownTicks = command.Definition.CooldownTicks,
                    OperationsOperation = command.Operation,
                    OperationsTemplate = command.Definition.Template,
                    LogisticsPriority = command.Definition.Priority,
                    Enabled = command.Enabled,
                    Value1 = command.Definition.Minimum,
                    Value2 = command.Definition.Target,
                    Value3 = command.Definition.Maximum
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
        MatchRuntime scenario)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(scenario);

        var submittedAtTick =
            new SimulationTick(
                command.SubmittedAtTick);

        return command.Kind switch
        {
            ReplayCommandKind.DeveloperAction => DecodeDeveloper(command, scenario),
            ReplayCommandKind.QueuedOrders =>
                new QueuedOrdersCommand(command.Issuer, command.Entities,
                    new(command.QueueKind, command.Position, command.TargetEntity, command.Formation, submittedAtTick),
                    command.QueueEdit, command.Patrol, submittedAtTick, scenario.Intelligence),
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

            ReplayCommandKind.Demolition =>
                new DemolishBuildingsCommand(
                    command.Issuer,
                    command.Entities,
                    submittedAtTick,
                    new PlayerCommandCorrelationId(command.CorrelationId)),

            ReplayCommandKind.CancelConstruction =>
                new CancelConstructionCommand(
                    command.Issuer,
                    command.TargetEntity,
                    submittedAtTick),

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

            ReplayCommandKind.BridgeRepair =>
                new RepairBridgeCommand(scenario.Services.StrategicInfrastructure, command.Issuer,
                    command.Entities.Length == 1 ? command.Entities[0] : EntityId.Invalid,
                    command.TargetEntity, command.SecondaryEntity, command.InfrastructureRevision, submittedAtTick),

            ReplayCommandKind.ConveyorRouting =>
                new ConveyorRoutingCommand(command.Issuer, command.TargetEntity,
                    command.RoutingPreference, command.RoutingRevision, submittedAtTick),

            ReplayCommandKind.ConveyorConstruction =>
                new ConveyorConstructionCommand(scenario.Services.Conveyors, command.Issuer,
                    command.ConveyorRequest, command.TargetEntity, submittedAtTick),

            ReplayCommandKind.OperationsRule =>
                new OperationsRuleCommand(
                    scenario.OperationsRules, command.OperationsOperation,
                    new OperationsRuleDefinition(
                        command.OperationsRuleId, command.Issuer, command.TargetEntity,
                        command.OperationsTemplate, command.ResourceId,
                        command.Value1, command.Value2, command.Value3,
                        command.LogisticsPriority, command.OperationsCooldownTicks),
                    command.OperationsRuleRevision, command.Enabled, submittedAtTick),

            ReplayCommandKind.Technology =>
                DecodeTechnology(
                    command,
                    submittedAtTick,
                    scenario),

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
                    scenario.BattlefieldRuntime.MatchStateEntity,
                    command.Enabled),

            _ =>
                throw new InvalidDataException(
                    $"Replay command kind '{command.Kind}' is not supported.")
        };
    }

    private static ISimulationCommand DecodeDeveloper(RecordedSimulationCommand command, MatchRuntime scenario)
    {
        if (command.DeveloperPayload is null || command.IsControl)
            throw new MatchPersistenceException(MatchPersistenceFailureReason.CorruptDocument, "Invalid developer command payload or control placement.");
#if FORGELINE_DEVELOPER_TOOLS
        if (scenario.Services.DeveloperActions is DeveloperCommandGateway gateway)
            return gateway.ReconstructCommand(command.DeveloperPayload);
#endif
        throw new MatchPersistenceException(MatchPersistenceFailureReason.IncompatibleVersion, "Developer command handlers are absent in this build.");
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

    private static PlayerTechnologyActionCommand DecodeTechnology(
        RecordedSimulationCommand command,
        SimulationTick submittedAtTick,
        MatchRuntime scenario) =>
        command.TechnologyOperation switch
        {
            PlayerTechnologyOperation.Start =>
                PlayerTechnologyActionCommand.Start(
                    command.Issuer,
                    command.TechnologyId,
                    command.TargetEntity,
                    command.Entities.Length > 0
                        ? command.Entities[0]
                        : EntityId.Invalid,
                    submittedAtTick,
                    scenario.Services.TechnologyDefinitions),

            PlayerTechnologyOperation.Cancel =>
                PlayerTechnologyActionCommand.Cancel(
                    command.Issuer,
                    command.SecondaryEntity,
                    submittedAtTick),

            _ =>
                throw new InvalidDataException(
                    $"Technology replay operation '{command.TechnologyOperation}' is invalid.")
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

            PlayerLogisticsActionOperation.CreateCargoConvoy =>
                DecodeCargoConvoy(command, submittedAtTick),

            PlayerLogisticsActionOperation.DissolveCargoConvoy =>
                PlayerLogisticsActionCommand.DissolveCargoConvoy(
                    command.Issuer, command.TargetEntity, submittedAtTick),

            _ =>
                throw new InvalidDataException(
                    $"Logistics replay operation '{command.LogisticsOperation}' is invalid.")
        };

    private static PlayerLogisticsActionCommand DecodeCargoConvoy(
        RecordedSimulationCommand command,
        SimulationTick submittedAtTick)
    {
        if (command.Entities is null || command.IntValue < 0 ||
            command.IntValue > command.Entities.Length)
        {
            throw new InvalidDataException("Convoy replay member boundary is invalid.");
        }

        return PlayerLogisticsActionCommand.CreateCargoConvoy(
            command.Issuer,
            command.Entities.AsSpan(0, command.IntValue),
            command.Entities.AsSpan(command.IntValue),
            submittedAtTick);
    }

    private static PlayerTacticalActionCommand DecodeTactical(
        RecordedSimulationCommand command,
        SimulationTick submittedAtTick,
        MatchRuntime scenario) =>
        command.TacticalOperation switch
        {
            PlayerTacticalActionOperation.HoldFire or PlayerTacticalActionOperation.ReturnFire or PlayerTacticalActionOperation.FireAtWill =>
                PlayerTacticalActionCommand.SetFirePolicy(command.Issuer, command.Entities,
                    command.TacticalOperation switch { PlayerTacticalActionOperation.HoldFire => FirePolicy.HoldFire,
                        PlayerTacticalActionOperation.ReturnFire => FirePolicy.ReturnFire, _ => FirePolicy.FireAtWill },
                    submittedAtTick, scenario.Intelligence, scenario.Services.Weapons, scenario.Services.ArtilleryWeapons),
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
