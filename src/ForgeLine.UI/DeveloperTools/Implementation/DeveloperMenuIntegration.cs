using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.UI;

public enum DeveloperMenuSection : byte { Session, Parameters, Actions, Visualization, Presets }
public enum DeveloperPresetIntent : byte { Import, Export, RestartSameSeed, ReturnToBasis }
public sealed record DeveloperMenuChoice(string Label, DeveloperValue Value);
public sealed record DeveloperMenuActionField(string Label, DeveloperValue Default, DeveloperValue Minimum, DeveloperValue Maximum, DeveloperValue Step, IReadOnlyList<DeveloperMenuChoice>? Choices = null);
public sealed record DeveloperMenuPresetOutcome(Guid Correlation, bool Succeeded, string Message);
public sealed class DeveloperMenuActionForm
{
    public DeveloperMenuActionForm(DeveloperOperation operation, ReadOnlySpan<DeveloperMenuActionField> fields)
    {
        if (operation is not (DeveloperOperation.SpawnUnit or DeveloperOperation.GrantResource) || fields.Length > DeveloperRequest.MaximumValues)
            throw new ArgumentException("Unsupported action form.");
        foreach (var f in fields)
            if (string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 64 || !f.Default.IsValid || f.Minimum.Kind != f.Default.Kind ||
                f.Maximum.Kind != f.Default.Kind || f.Step.Kind != f.Default.Kind || !f.Minimum.IsValid || !f.Maximum.IsValid || !f.Step.IsValid ||
                DeveloperMenuModel.Number(f.Minimum) > DeveloperMenuModel.Number(f.Default) || DeveloperMenuModel.Number(f.Maximum) < DeveloperMenuModel.Number(f.Default) ||
                DeveloperMenuModel.Number(f.Step) <= 0) throw new ArgumentException("Invalid action field.");
        foreach (var f in fields)
            if (f.Choices is { } choices && (choices.Count is < 1 or > 64 || !choices.Any(c => c.Value == f.Default) ||
                choices.Any(c => string.IsNullOrWhiteSpace(c.Label) || c.Label.Length > 64 || c.Value.Kind != f.Default.Kind || !c.Value.IsValid ||
                    DeveloperMenuModel.Number(c.Value) < DeveloperMenuModel.Number(f.Minimum) || DeveloperMenuModel.Number(c.Value) > DeveloperMenuModel.Number(f.Maximum))))
                throw new ArgumentException("Invalid action choices.");
        Operation = operation; Fields = Array.AsReadOnly(fields.ToArray().Select(f => f with
            { Choices = f.Choices is null ? null : Array.AsReadOnly(f.Choices.ToArray()) }).ToArray());
    }
    public DeveloperOperation Operation { get; }
    public IReadOnlyList<DeveloperMenuActionField> Fields { get; }
}
public sealed record DeveloperMenuPreset(string Key, string Label, DeveloperPresetMetadata Metadata, DeveloperFailureReason Compatibility);

/// <summary>Copied observations supplied by the actual host/dispatcher/preset owners. No UI authority.</summary>
public sealed class DeveloperMenuBackendSnapshot
{
    public DeveloperMenuBackendSnapshot(SimulationSessionId session, SimulationTick tick,
        ReadOnlySpan<DeveloperStateRevision> revisions, ReadOnlySpan<DeveloperResult> results,
        ReadOnlySpan<DeveloperMenuPreset> presets, double? speed = null, uint? remainingSteps = null,
        bool loading = false, bool menuPauseTransferred = false, DeveloperMenuPresetOutcome? presetOutcome = null)
    {
        if (!session.IsSpecified || revisions.Length > 4 || results.Length > 64 || presets.Length > 64 ||
            revisions.ToArray().Any(r => !r.IsValid) || revisions.ToArray().Select(r => r.Domain).Distinct().Count() != revisions.Length ||
            results.ToArray().Any(r => r.Session != session) || speed.HasValue && (!double.IsFinite(speed.Value) || speed <= 0))
            throw new ArgumentException("Invalid menu observations.");
        foreach (var p in presets) if (string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 96 || p.Label.Length > 128)
            throw new ArgumentException("Invalid preset entry.");
        if (presetOutcome is { } outcome && (outcome.Correlation == Guid.Empty || outcome.Message.Length > 256))
            throw new ArgumentException("Invalid preset outcome.");
        PresetOutcome = presetOutcome;
        Session = session; Tick = tick; Revisions = Array.AsReadOnly(revisions.ToArray()); Results = Array.AsReadOnly(results.ToArray());
        Presets = Array.AsReadOnly(presets.ToArray()); Speed = speed; RemainingSteps = remainingSteps;
        Loading = loading; MenuPauseTransferred = menuPauseTransferred;
    }
    public DeveloperMenuPresetOutcome? PresetOutcome { get; }
    public SimulationSessionId Session { get; }
    public SimulationTick Tick { get; }
    public IReadOnlyList<DeveloperStateRevision> Revisions { get; }
    public IReadOnlyList<DeveloperResult> Results { get; }
    public IReadOnlyList<DeveloperMenuPreset> Presets { get; }
    public double? Speed { get; }
    public uint? RemainingSteps { get; }
    public bool Loading { get; }
    public bool MenuPauseTransferred { get; }
}

/// <summary>Client integration bridge, not a second simulation request protocol or preset implementation.</summary>
public interface IDeveloperMenuBackend
{
    DeveloperMenuBackendSnapshot? Capture();
    IReadOnlyList<DeveloperMenuActionForm> ActionForms { get; }
    // The session adapter owns separate window/focus and menu pause reasons and cancels steps while blocked.
    void SetWindowBlocked(bool blocked);
    void ReleaseMenuOwnership();
    // True means submitted only. Restart must enter the client's normal transactional session-loading path.
    bool TrySubmitPreset(Guid correlation, DeveloperPresetIntent intent, DeveloperMenuPreset? preset, out string feedback);
}

public sealed class DeveloperMenuCatalog : IDeveloperMenuCatalog
{
    public DeveloperProviderStatus Status => DeveloperToolsCapability.Current.IsCompiled
        ? new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None) : DeveloperProviderStatus.Missing;
    private static readonly DeveloperOperationDescriptor[] SessionOperations =
    {
        new DeveloperOperationDescriptor(DeveloperOperation.Pause, DeveloperTargetKinds.Global, "Pause"),
        new DeveloperOperationDescriptor(DeveloperOperation.Resume, DeveloperTargetKinds.Global, "Resume"),
        new DeveloperOperationDescriptor(DeveloperOperation.SingleStep, DeveloperTargetKinds.Global, "Step"),
        new DeveloperOperationDescriptor(DeveloperOperation.SetHostSpeed, DeveloperTargetKinds.Global, "Host speed")
    };
    public DeveloperMenuCatalog(IDeveloperMenuCatalog? supplied = null)
    {
        Operations = Array.AsReadOnly(SessionOperations.Concat(supplied?.Status.Availability == DeveloperProviderAvailability.Available
            ? supplied.Operations.Take(32) : Array.Empty<DeveloperOperationDescriptor>()).DistinctBy(d => d.Operation).ToArray());
    }
    public IReadOnlyList<DeveloperOperationDescriptor> Operations { get; }
}
