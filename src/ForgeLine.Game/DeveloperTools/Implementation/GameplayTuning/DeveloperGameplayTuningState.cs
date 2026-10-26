using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum DeveloperRestartStatus : byte { None, WaitingForNewSession }
public readonly record struct DeveloperTuningObservation(DeveloperParameterId Id, DeveloperTarget Target,
    DeveloperValue Basis, DeveloperValue Initial, DeveloperValue Effective, DeveloperValue? Pending, DeveloperApplyMode ApplyMode);
public readonly record struct DeveloperTuningChange(DeveloperParameterId Id, DeveloperTarget Target,
    DeveloperValue Before, DeveloperValue After, DeveloperValue? PendingBefore, DeveloperValue? PendingAfter,
    bool IsReset, SimulationTick RecordedTick, DeveloperStateRevision Revision);
public readonly record struct DeveloperConveyorTuning(double Multiplier, DeveloperStateRevision Revision, SimulationTick AppliedTick);

/// <summary>Bounded copied contribution. The central owner encodes it and records every change in global command order.</summary>
public sealed class DeveloperTuningContribution
{
    internal DeveloperTuningContribution(DeveloperInitialProfile initial, DeveloperStateRevision revision, SimulationTick recordedTick,
        DeveloperConveyorTuning conveyor, bool modified, ReadOnlySpan<DeveloperTuningObservation> values)
    {
        if (values.Length > DeveloperInitialProfile.MaximumParameters) throw new ArgumentOutOfRangeException(nameof(values));
        InitialOverrides = initial; Revision = revision; RecordedTick = recordedTick; Conveyor = conveyor; DeveloperModified = modified;
        Values = Array.AsReadOnly(values.ToArray());
    }
    public DeveloperInitialProfile InitialOverrides { get; }
    public DeveloperStateRevision Revision { get; }
    public SimulationTick RecordedTick { get; }
    public DeveloperConveyorTuning Conveyor { get; }
    public bool DeveloperModified { get; }
    public IReadOnlyList<DeveloperTuningObservation> Values { get; }
    public DeveloperRestartStatus RestartStatus => Values.Any(v => v.Pending.HasValue) ? DeveloperRestartStatus.WaitingForNewSession : DeveloperRestartStatus.None;
}

public sealed class DeveloperTuningApplication
{
    internal DeveloperTuningApplication(DeveloperResult result, DeveloperTuningContribution contribution, ReadOnlySpan<DeveloperTuningChange> changes, bool isReplay = false)
    { Result = result; Contribution = contribution; Changes = Array.AsReadOnly(changes.ToArray()); IsReplay = isReplay; }
    public bool IsReplay { get; }
    public DeveloperResult Result { get; }
    public DeveloperTuningContribution Contribution { get; }
    public IReadOnlyList<DeveloperTuningChange> Changes { get; }
}
