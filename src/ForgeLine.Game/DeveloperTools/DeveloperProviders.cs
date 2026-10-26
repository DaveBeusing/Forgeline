namespace ForgeLine.Game;

public enum DeveloperProviderAvailability : byte { Unavailable, Available }
public readonly record struct DeveloperProviderStatus(DeveloperProviderAvailability Availability, DeveloperFailureReason Reason)
{
    public static DeveloperProviderStatus Missing => new(DeveloperProviderAvailability.Unavailable, DeveloperFailureReason.ProviderUnavailable);
}
public interface IDeveloperProvider { DeveloperProviderStatus Status { get; } }
public readonly record struct DeveloperOperationDescriptor(DeveloperOperation Operation, DeveloperTargetKinds Targets, string Label);
public interface IDeveloperMenuCatalog : IDeveloperProvider { IReadOnlyList<DeveloperOperationDescriptor> Operations { get; } }
public interface IDeveloperSessionController : IDeveloperProvider
{
    DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request);
}
public interface IDeveloperActionDispatcher : IDeveloperProvider
{
    DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request);
}
public sealed class DeveloperParameterReadResult
{
    public DeveloperParameterReadResult(DeveloperProviderStatus status, ReadOnlySpan<DeveloperEffectiveParameter> values)
    {
        if (!Enum.IsDefined(status.Availability) || !Enum.IsDefined(status.Reason) ||
            (status.Availability == DeveloperProviderAvailability.Available) != (status.Reason == DeveloperFailureReason.None) ||
            values.Length > DeveloperInitialProfile.MaximumParameters ||
            status.Availability == DeveloperProviderAvailability.Unavailable && !values.IsEmpty)
            throw new ArgumentException("Invalid parameter observation.");
        foreach (var value in values)
            if (!DeveloperParameterCatalog.TryGet(value.Id, out var descriptor) || !descriptor.Accepts(value.Value, value.Target) ||
                !value.Revision.IsValid) throw new ArgumentException("Invalid effective parameter observation.", nameof(values));
        Status = status; Values = Array.AsReadOnly(values.ToArray());
    }
    public DeveloperProviderStatus Status { get; }
    public IReadOnlyList<DeveloperEffectiveParameter> Values { get; }
}
public interface IDeveloperParameterProvider : IDeveloperProvider
{
    IReadOnlyList<DeveloperParameterDescriptor> Descriptors { get; }
    DeveloperParameterReadResult Capture();
    DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request);
}
public interface IDeveloperPresetService : IDeveloperProvider
{
    DeveloperFailureReason Validate(DeveloperPresetMetadata metadata);
}

/// <summary>Host composition slots. Missing neighbors are explicitly unavailable, never successful no-ops.</summary>
public sealed class DeveloperToolsProviders
{
    public DeveloperToolsProviders(DeveloperToolsCapability capability, IDeveloperMenuCatalog? menu = null,
        IDeveloperSessionController? sessions = null, IDeveloperActionDispatcher? actions = null,
        IDeveloperParameterProvider? parameters = null, IDeveloperPresetService? presets = null)
    {
        ArgumentNullException.ThrowIfNull(capability);
        Capability = capability;
        if (!capability.IsCompiled && (menu is not null || sessions is not null || actions is not null || parameters is not null || presets is not null))
            throw new InvalidOperationException("A public host cannot register developer providers.");
        Menu = menu ?? MissingDeveloperProvider.Instance; Sessions = sessions ?? MissingDeveloperProvider.Instance;
        Actions = actions ?? MissingDeveloperProvider.Instance; Parameters = parameters ?? MissingDeveloperProvider.Instance;
        Presets = presets ?? MissingDeveloperProvider.Instance;
    }
    public DeveloperToolsCapability Capability { get; }
    public IDeveloperMenuCatalog Menu { get; }
    public IDeveloperSessionController Sessions { get; }
    public IDeveloperActionDispatcher Actions { get; }
    public IDeveloperParameterProvider Parameters { get; }
    public IDeveloperPresetService Presets { get; }
}

internal sealed class MissingDeveloperProvider : IDeveloperMenuCatalog, IDeveloperSessionController,
    IDeveloperActionDispatcher, IDeveloperParameterProvider, IDeveloperPresetService
{
    private static readonly HashSet<DeveloperOperation> KnownOperations = Enum.GetValues<DeveloperOperation>().ToHashSet();
    internal static MissingDeveloperProvider Instance { get; } = new();
    private MissingDeveloperProvider() { }
    public DeveloperProviderStatus Status => DeveloperProviderStatus.Missing;
    public IReadOnlyList<DeveloperOperationDescriptor> Operations => Array.Empty<DeveloperOperationDescriptor>();
    public IReadOnlyList<DeveloperParameterDescriptor> Descriptors => DeveloperParameterCatalog.Descriptors;
    public DeveloperParameterReadResult Capture() => new(Status, Array.Empty<DeveloperEffectiveParameter>());
    public DeveloperFailureReason Validate(DeveloperPresetMetadata metadata)
    { ArgumentNullException.ThrowIfNull(metadata); return DeveloperFailureReason.ProviderUnavailable; }
    public DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request)
    {
        ArgumentNullException.ThrowIfNull(permission); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(request);
        DeveloperFailureReason reason = DeveloperRequestGate.Check(permission, context, request, KnownOperations);
        if (reason == DeveloperFailureReason.None) reason = DeveloperFailureReason.ProviderUnavailable;
        return DeveloperResult.Rejected(request, context.Revision, reason);
    }
}

public static class DeveloperToolsComposition
{
    public static DeveloperToolsProviders Create()
    {
#if FORGELINE_DEVELOPER_TOOLS
        return CompiledDeveloperToolsHost.Create();
#else
        return new(DeveloperToolsCapability.Current);
#endif
    }
}
