using System.Text.Json.Serialization;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum MatchDebriefOutcome : byte { Incomplete = 0, Victory = 1, Defeat = 2, Draw = 3 }
public enum MatchDebriefReason : byte { Unknown = 0, CommandCoreDestroyed = 1, Surrender = 2, MutualCommandCoreDestruction = 3, AllParticipantsEliminated = 4 }
public enum MatchDebriefCategory : byte { All = 0, Industry = 1, Supply = 2, Reconnaissance = 3, Infrastructure = 4, Forces = 5, Result = 6 }
public enum MatchDebriefEventKind : byte
{
    Extraction = 1, CargoLoaded = 2, CargoDelivered = 3, ConveyorDelivered = 4, ProductionCompleted = 5,
    ScoutProduced = 6, SupplyTruckProduced = 7, FieldFuelTransferred = 8, FieldAmmunitionTransferred = 9,
    SupplyLoadCompleted = 10, ResearchCompleted = 11, WorkBlocked = 12, WorkResumed = 13, MatchEnded = 14
}
[Flags]
public enum MatchDebriefEvidence : byte { First = 1, Latest = 2, PublicResult = 4 }

/// <summary>Subject is a bounded catalog label; SubjectId is report-local, never an entity identity.</summary>
public sealed record MatchDebriefEvent(ulong Tick, MatchDebriefEventKind Kind, int SubjectId,
    string Subject, double? Quantity, MatchDebriefEvidence Evidence)
{
    [JsonIgnore] public MatchDebriefCategory Category => Kind switch
    {
        MatchDebriefEventKind.CargoLoaded or MatchDebriefEventKind.CargoDelivered or
        MatchDebriefEventKind.SupplyTruckProduced or MatchDebriefEventKind.FieldFuelTransferred or
        MatchDebriefEventKind.FieldAmmunitionTransferred or MatchDebriefEventKind.SupplyLoadCompleted => MatchDebriefCategory.Supply,
        MatchDebriefEventKind.ScoutProduced => MatchDebriefCategory.Reconnaissance,
        MatchDebriefEventKind.ConveyorDelivered => MatchDebriefCategory.Infrastructure,
        MatchDebriefEventKind.MatchEnded => MatchDebriefCategory.Result,
        _ => MatchDebriefCategory.Industry
    };
}

/// <summary>Player-scoped first/latest evidence, not a complete history or a causal analysis.</summary>
public sealed class MatchDebriefSummary
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumEvents = 128;
    public const int MaximumSubjectLength = 48;
    public const int MaximumScenarioLength = 128;

    [JsonConstructor]
    public MatchDebriefSummary(int schemaVersion, ulong sessionId, ulong player, string scenario, int tickRate,
        ulong startedAtTick, ulong observedThroughTick, ulong? resultAtTick, MatchDebriefOutcome outcome,
        MatchDebriefReason reason, bool truncated, IReadOnlyList<MatchDebriefEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (schemaVersion != CurrentSchemaVersion || sessionId == 0 || player == 0 || tickRate is < 1 or > 1000 ||
            startedAtTick > observedThroughTick || !ValidText(scenario, MaximumScenarioLength) ||
            !Enum.IsDefined(outcome) || !Enum.IsDefined(reason) || events.Count > MaximumEvents ||
            (outcome == MatchDebriefOutcome.Incomplete ? resultAtTick.HasValue || reason != MatchDebriefReason.Unknown :
                !resultAtTick.HasValue || resultAtTick.Value < startedAtTick || resultAtTick.Value > observedThroughTick))
            throw new ArgumentException("Invalid or unsupported player debrief.");
        ulong previous = startedAtTick;
        int resultEvents = 0;
        foreach (var item in events)
        {
            if (item is null || !Enum.IsDefined(item.Kind) || item.Tick < previous || item.Tick > (resultAtTick ?? observedThroughTick) ||
                !ValidText(item.Subject, MaximumSubjectLength) || item.SubjectId is < 0 or > MaximumEvents ||
                item.Quantity is { } quantity && (!double.IsFinite(quantity) || quantity <= 0 ||
                    item.Kind is not (MatchDebriefEventKind.Extraction or MatchDebriefEventKind.CargoLoaded or MatchDebriefEventKind.CargoDelivered or
                        MatchDebriefEventKind.ConveyorDelivered or MatchDebriefEventKind.FieldFuelTransferred or MatchDebriefEventKind.FieldAmmunitionTransferred)))
                throw new ArgumentException("Invalid debrief event.");
            if (item.Kind == MatchDebriefEventKind.MatchEnded)
            {
                resultEvents++;
                if (item.Tick != resultAtTick || item.Evidence != MatchDebriefEvidence.PublicResult || item.SubjectId != 0 || item.Quantity.HasValue)
                    throw new ArgumentException("Invalid public result evidence.");
            }
            else if (item.SubjectId == 0 || (byte)item.Evidence is < 1 or > 3)
                throw new ArgumentException("Invalid owned observation evidence.");
            previous = item.Tick;
        }
        if (resultEvents != (outcome == MatchDebriefOutcome.Incomplete ? 0 : 1))
            throw new ArgumentException("Debrief result evidence is inconsistent.");
        SchemaVersion = schemaVersion; SessionId = sessionId; Player = player; Scenario = scenario; TickRate = tickRate;
        StartedAtTick = startedAtTick; ObservedThroughTick = observedThroughTick; ResultAtTick = resultAtTick;
        Outcome = outcome; Reason = reason; Truncated = truncated; Events = Array.AsReadOnly(events.ToArray());
    }
    public int SchemaVersion { get; }
    public ulong SessionId { get; }
    public ulong Player { get; }
    public string Scenario { get; }
    public int TickRate { get; }
    public ulong StartedAtTick { get; }
    public ulong ObservedThroughTick { get; }
    public ulong? ResultAtTick { get; }
    public MatchDebriefOutcome Outcome { get; }
    public MatchDebriefReason Reason { get; }
    public bool Truncated { get; }
    public IReadOnlyList<MatchDebriefEvent> Events { get; }
    internal static bool ValidText(string? text, int maximum) => !string.IsNullOrWhiteSpace(text) &&
        text.Length <= maximum && !text.Any(char.IsControl);
}

public static class MatchDebriefCapture
{
    private const int MaximumSourceEvents = 128;
    public static MatchDebriefSummary Capture(MatchRuntime runtime, PlayerId player)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!player.IsSpecified || !runtime.MatchConfiguration.Participants.Any(p => p.Player == player))
            throw new ArgumentException("The debrief player must be a configured participant.", nameof(player));
        var tick = runtime.Simulation.CurrentTick;
        return FromPublished(runtime.Simulation.SessionId, player, runtime.RuntimeSettings.Composition.Key,
            runtime.Simulation.Clock.TicksPerSecond, tick, runtime.GetMatchState(),
            runtime.Progress.CaptureFirst(player, tick), runtime.Progress.Capture(player, tick), item => Subject(runtime, item));
    }

    internal static MatchDebriefSummary FromPublished(SimulationSessionId session, PlayerId player, string scenario,
        int tickRate, SimulationTick through, MatchState state, IReadOnlyList<GameplayProgressEvent> first,
        IReadOnlyList<GameplayProgressEvent> latest, Func<GameplayProgressEvent, string> subject)
    {
        bool hasResult = state.IsTerminal && state.HasResult && state.Outcome is MatchOutcome.Victory or MatchOutcome.Draw;
        var retained = new Dictionary<ulong, (GameplayProgressEvent Event, MatchDebriefEventKind Kind, MatchDebriefEvidence Evidence)>();
        ulong end = hasResult ? state.CompletedAtTick.Value : through.Value;
        Add(first, MatchDebriefEvidence.First); Add(latest, MatchDebriefEvidence.Latest);
        var ordered = retained.Values.OrderBy(x => x.Event.Tick).ThenBy(x => x.Event.Identity).ToArray();
        int limit = MatchDebriefSummary.MaximumEvents - (hasResult ? 1 : 0);
        var targets = new Dictionary<EntityId, int>();
        var rows = new List<MatchDebriefEvent>(Math.Min(ordered.Length, limit) + 1);
        foreach (var item in ordered.Take(limit))
        {
            if (!targets.TryGetValue(item.Event.Target, out int target))
                targets.Add(item.Event.Target, target = targets.Count + 1);
            bool quantity = item.Event.Resource.IsSpecified && item.Kind is
                MatchDebriefEventKind.Extraction or MatchDebriefEventKind.CargoLoaded or MatchDebriefEventKind.CargoDelivered or
                MatchDebriefEventKind.ConveyorDelivered or MatchDebriefEventKind.FieldFuelTransferred or MatchDebriefEventKind.FieldAmmunitionTransferred;
            rows.Add(new(item.Event.Tick.Value, item.Kind, target, Clean(subject(item.Event), "OWN OBSERVATION"),
                quantity ? item.Event.Quantity : null, item.Evidence));
        }
        var outcome = !hasResult ? MatchDebriefOutcome.Incomplete : state.Outcome == MatchOutcome.Draw ? MatchDebriefOutcome.Draw :
            state.Winner == player ? MatchDebriefOutcome.Victory : MatchDebriefOutcome.Defeat;
        var reason = !hasResult ? MatchDebriefReason.Unknown : state.TerminationReason switch
        {
            MatchTerminationReason.CommandCoreDestroyed => MatchDebriefReason.CommandCoreDestroyed,
            MatchTerminationReason.Surrender => MatchDebriefReason.Surrender,
            MatchTerminationReason.MutualCommandCoreDestruction => MatchDebriefReason.MutualCommandCoreDestruction,
            MatchTerminationReason.AllParticipantsEliminated => MatchDebriefReason.AllParticipantsEliminated,
            _ => MatchDebriefReason.Unknown
        };
        if (hasResult) rows.Add(new(end, MatchDebriefEventKind.MatchEnded, 0, "PUBLIC MATCH RESULT", null, MatchDebriefEvidence.PublicResult));
        return new(MatchDebriefSummary.CurrentSchemaVersion, session.Value, player.Value, scenario, tickRate,
            state.StartedAtTick.Value, through.Value, hasResult ? end : null, outcome, reason, ordered.Length > limit, rows);

        void Add(IReadOnlyList<GameplayProgressEvent> source, MatchDebriefEvidence evidence)
        {
            for (int i = 0; i < Math.Min(source.Count, MaximumSourceEvents); i++)
            {
                var item = source[i];
                if (item.Player != player || item.SessionId != session || item.Identity == 0 ||
                    item.Tick.Value < state.StartedAtTick.Value || item.Tick.Value > end ||
                    !item.Target.IsValid || !double.IsFinite(item.Quantity) || item.Quantity <= 0 || Kind(item.Kind) is not { } kind) continue;
                if (retained.TryGetValue(item.Identity, out var prior)) retained[item.Identity] = (prior.Event, prior.Kind, prior.Evidence | evidence);
                else retained.Add(item.Identity, (item, kind, evidence));
            }
        }
    }

    private static MatchDebriefEventKind? Kind(GameplayProgressKind kind) => kind switch
    {
        GameplayProgressKind.Extraction => MatchDebriefEventKind.Extraction,
        GameplayProgressKind.CargoLoaded => MatchDebriefEventKind.CargoLoaded,
        GameplayProgressKind.CargoUnloaded => MatchDebriefEventKind.CargoDelivered,
        GameplayProgressKind.ConveyorDelivered => MatchDebriefEventKind.ConveyorDelivered,
        GameplayProgressKind.ProductionCompleted => MatchDebriefEventKind.ProductionCompleted,
        GameplayProgressKind.ScoutProduced => MatchDebriefEventKind.ScoutProduced,
        GameplayProgressKind.SupplyTruckProduced => MatchDebriefEventKind.SupplyTruckProduced,
        GameplayProgressKind.FieldFuelTransferred => MatchDebriefEventKind.FieldFuelTransferred,
        GameplayProgressKind.FieldAmmunitionTransferred => MatchDebriefEventKind.FieldAmmunitionTransferred,
        GameplayProgressKind.SupplyLoadCompleted => MatchDebriefEventKind.SupplyLoadCompleted,
        GameplayProgressKind.ResearchCompleted => MatchDebriefEventKind.ResearchCompleted,
        GameplayProgressKind.DisruptionObserved => MatchDebriefEventKind.WorkBlocked,
        GameplayProgressKind.DisruptionResolved => MatchDebriefEventKind.WorkResumed,
        _ => null
    };

    private static string Subject(MatchRuntime runtime, GameplayProgressEvent item)
    {
        if (item.Resource.IsSpecified && runtime.Services.Resources.TryGet(item.Resource, out var resource)) return resource.DisplayName;
        if (item.Kind == GameplayProgressKind.ProductionCompleted && runtime.Services.BuildingDefinitions.TryGet(new(item.DefinitionId), out var building)) return building.DisplayName;
        if (item.Kind == GameplayProgressKind.ScoutProduced) return "SCOUT";
        if (item.Kind == GameplayProgressKind.SupplyTruckProduced) return "SUPPLY TRUCK";
        if (item.Kind == GameplayProgressKind.ResearchCompleted && runtime.Services.TechnologyDefinitions.TryGet(new(item.DefinitionId), out var research)) return research.DisplayName;
        return item.Kind is GameplayProgressKind.DisruptionObserved or GameplayProgressKind.DisruptionResolved ? "PRODUCTION FACILITY" : "OWN OBSERVATION";
    }
    private static string Clean(string value, string fallback)
    {
        string clean = new(value.Where(c => !char.IsControl(c)).Take(MatchDebriefSummary.MaximumSubjectLength).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }
}
