#if FORGELINE_DEVELOPER_TOOLS
using ForgeLine.Game;
using ForgeLine.UI;

namespace ForgeLine.Client;

/// <summary>File/preset bridge. It returns a normal owned-session request and never swaps a live runtime.</summary>
internal sealed class ClientDeveloperPresetSessionAdapter : IDeveloperPresetService
{
    private readonly DeveloperPresetStore _store;
    private readonly Func<string, MatchComposition> _resolveComposition;

    internal ClientDeveloperPresetSessionAdapter(DeveloperPresetStore? store = null,
        Func<string, MatchComposition>? resolveComposition = null)
    {
        _resolveComposition = resolveComposition ?? ScenarioRegistry.Default.Resolve;
        _store = store ?? new DeveloperPresetStore(_resolveComposition);
    }

    public DeveloperProviderStatus Status => DeveloperToolsCapability.Current.IsCompiled
        ? new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None) : DeveloperProviderStatus.Missing;

    public DeveloperFailureReason Validate(DeveloperPresetMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!DeveloperToolsCapability.Current.IsCompiled) return DeveloperFailureReason.CapabilityUnavailable;
        try
        {
            var composition = _resolveComposition(metadata.CompositionKey);
            if (composition.Identity != metadata.BaseIdentity) return DeveloperFailureReason.InvalidTarget;
            var basis = CentralDivideScenario.CreateHeadless(metadata.Profile, metadata.Seed, composition: composition) with
            { Participants = Array.AsReadOnly(metadata.Participants.ToArray()) };
            return DeveloperRestartBuilder.TryBuild(basis, metadata, [], out _, out var failure)
                ? DeveloperFailureReason.None : failure;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return DeveloperFailureReason.InvalidTarget; }
    }

    internal IReadOnlyList<DeveloperMenuPreset> CaptureMenuPresets()
    {
        EnsureDeveloperBuild();
        return Array.AsReadOnly(_store.List().Select(document => new DeveloperMenuPreset(
            document.Key, document.DisplayName, document.Profile.Metadata, Validate(document.Profile.Metadata))).ToArray());
    }

    internal DeveloperPresetDocument Import(ReadOnlySpan<byte> utf8)
    { EnsureDeveloperBuild(); return _store.Import(utf8); }

    internal void Export(DeveloperPresetDocument document)
    { EnsureDeveloperBuild(); _store.Export(document); }

    /// <summary>Exports the match's original start profile, never its later audit/history or live parameter state.</summary>
    internal DeveloperPresetDocument ExportOriginalProfile(MatchRuntime runtime, string key, string displayName)
    {
        EnsureDeveloperBuild();
        ArgumentNullException.ThrowIfNull(runtime);
        var initial = CaptureTuning(runtime).InitialOverrides;
        var document = DeveloperPresetCodec.Create(key, displayName, initial);
        _store.Export(document);
        return document;
    }

    /// <summary>Creates a distinct future-start profile from effective/pending values without changing this match.</summary>
    internal DeveloperPresetDocument SaveCurrentValuesAsNewPreset(MatchRuntime runtime, string displayName)
    {
        EnsureDeveloperBuild();
        ArgumentNullException.ThrowIfNull(runtime);
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > DeveloperPresetDocument.MaximumDisplayNameLength)
            throw new ArgumentException("Preset display name is empty or too long.", nameof(displayName));
        var contribution = CaptureTuning(runtime);
        var metadata = contribution.InitialOverrides.Metadata;
        var overrides = contribution.Values
            .Where(row => (row.Pending ?? row.Effective) != row.Basis)
            .Select(row => new DeveloperEffectiveParameter(row.Id, row.Target, row.Pending ?? row.Effective,
                new(DeveloperRevisionDomain.Parameters, 0)))
            .ToArray();
        var profile = new DeveloperInitialProfile(new DeveloperPresetMetadata(DeveloperPresetMetadata.CurrentSchema,
            metadata.CompositionKey, metadata.BaseIdentity, metadata.Seed, metadata.Profile,
            metadata.Participants.ToArray(), overrides.Length != 0), overrides);
        string key = "preset-" + Guid.NewGuid().ToString("N");
        var document = DeveloperPresetCodec.Create(key, displayName.Trim(), profile);
        _store.Export(document);
        return document;
    }

    internal ClientSessionRequest CreatePresetRequest(string key, MatchRuntime? currentRuntime = null)
    {
        EnsureDeveloperBuild();
        DeveloperPresetDocument document = _store.Load(key);
        DeveloperRestartPlan plan = DeveloperPresetCodec.ValidateAndBuildPlan(document.Profile, _resolveComposition);
        MatchRuntimeSettings settings = currentRuntime is null ? plan.Settings : PreserveClientOptions(currentRuntime.RuntimeSettings, plan.Settings);
        return ClientSessionRequest.NewGame(settings with { InitialDeveloperProfile = plan.InitialOverrides });
    }

    internal ClientSessionRequest CreateRestartSameConfigurationRequest(MatchRuntime runtime)
    {
        EnsureDeveloperBuild();
        ArgumentNullException.ThrowIfNull(runtime);
        var initial = CaptureTuning(runtime).InitialOverrides;
        DeveloperRestartPlan plan = DeveloperPresetCodec.ValidateAndBuildPlan(initial, _resolveComposition);
        return ClientSessionRequest.NewGame(PreserveClientOptions(runtime.RuntimeSettings, plan.Settings) with
            { InitialDeveloperProfile = plan.InitialOverrides });
    }

    internal ClientSessionRequest CreateReturnToBaselineRequest(MatchRuntime runtime)
    {
        EnsureDeveloperBuild();
        ArgumentNullException.ThrowIfNull(runtime);
        var current = CaptureTuning(runtime).InitialOverrides.Metadata;
        MatchComposition composition = _resolveComposition(current.CompositionKey);
        var basis = CentralDivideScenario.CreateHeadless(current.Profile, current.Seed, composition: composition) with
        { Participants = Array.AsReadOnly(current.Participants.ToArray()) };
        if (!DeveloperRestartBuilder.TryBuild(basis, new DeveloperPresetMetadata(DeveloperPresetMetadata.CurrentSchema,
                current.CompositionKey, current.BaseIdentity, current.Seed, current.Profile, current.Participants.ToArray(), false),
                [], out var plan, out var failure))
            throw new InvalidOperationException($"Baseline configuration failed validation ({failure}).");
        return ClientSessionRequest.NewGame(PreserveClientOptions(runtime.RuntimeSettings, plan!.Settings) with
            { InitialDeveloperProfile = plan.InitialOverrides });
    }

    internal static IReadOnlyList<byte> ExportBytes(DeveloperPresetDocument document,
        Func<string, MatchComposition>? resolveComposition = null) =>
        Array.AsReadOnly(DeveloperPresetCodec.Serialize(document, resolveComposition));

    private static DeveloperTuningContribution CaptureTuning(MatchRuntime runtime)
    {
        if (runtime.Services.DeveloperParameters is not DeveloperGameplayTuningProvider provider)
            throw new InvalidOperationException("The active session has no registered gameplay tuning provider.");
        return provider.CaptureContribution();
    }

    private static MatchRuntimeSettings PreserveClientOptions(MatchRuntimeSettings current, MatchRuntimeSettings next) => next with
    {
        EnableDiagnostics = current.EnableDiagnostics,
        EnablePhaseTiming = current.EnablePhaseTiming,
        EnableDebugCapture = current.EnableDebugCapture,
        EnableSpatialQueryTiming = current.EnableSpatialQueryTiming,
        InitialEntityCapacity = current.InitialEntityCapacity
    };

    private static void EnsureDeveloperBuild()
    {
        if (!DeveloperToolsCapability.Current.IsCompiled) throw new InvalidOperationException("Preset operations require a Developer build.");
    }
}
#endif
