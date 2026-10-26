using ForgeLine.Game;
using ForgeLine.UI;

namespace ForgeLine.Client;

/// <summary>Composes existing providers with the real host boundary and owned session loader.</summary>
internal sealed class ClientDeveloperMenuBackend : IDeveloperMenuBackend, IDeveloperSessionController,
    IDeveloperActionDispatcher, IDeveloperParameterProvider, IDeveloperMenuCatalog, IDisposable
{
    private readonly MatchRuntime _runtime;
    private readonly ClientSimulationHost _host;
    private readonly ClientDeveloperPresetSessionAdapter _presets;
    private readonly Func<MatchRuntimeSettings, bool> _restart;
    private readonly Queue<DeveloperResult> _sessionResults = new();
    private DeveloperSessionAccess _access;
    private bool _open, _loading, _disposed;
    private Guid? _transfer;
    private IReadOnlyList<DeveloperMenuPreset> _entries = Array.Empty<DeveloperMenuPreset>();
    private DeveloperMenuPresetOutcome? _presetOutcome;
    private string? _presetAvailability;

    internal ClientDeveloperMenuBackend(MatchRuntime runtime, ClientSimulationHost host, DeveloperSessionAccess access,
        Func<MatchRuntimeSettings, bool> restart, ClientDeveloperPresetSessionAdapter? presets = null)
    {
        _runtime = runtime; _host = host; _access = access; _restart = restart;
        _presets = presets ?? new();
        Providers = new(DeveloperToolsCapability.Current, this, this, this, this, _presets);
        Operations = runtime.Services.DeveloperActions is DeveloperCommandGateway gateway ? gateway.Operations : Array.Empty<DeveloperOperationDescriptor>();
        ActionForms = BuildForms(runtime);
    }

    internal DeveloperToolsProviders Providers { get; }
    public DeveloperProviderStatus Status => _access.IsActive && !_disposed
        ? new(DeveloperProviderAvailability.Available, DeveloperFailureReason.None)
        : new(DeveloperProviderAvailability.Unavailable, DeveloperFailureReason.SessionNotActivated);
    public IReadOnlyList<DeveloperOperationDescriptor> Operations { get; }
    public IReadOnlyList<DeveloperMenuActionForm> ActionForms { get; }
    public IReadOnlyList<DeveloperParameterDescriptor> Descriptors => _runtime.Services.DeveloperParameters.Descriptors;
    DeveloperParameterReadResult IDeveloperParameterProvider.Capture() => _runtime.Services.DeveloperParameters.Capture();
    public IReadOnlyList<DeveloperResult> CaptureResults() => _host.CaptureRuntimeDeveloperResults();

    internal void SetActivated(bool active)
    {
        if (_access.IsActive == active) return;
        _access = DeveloperToolsCapability.Current.BindSession(_host.SessionId, active);
        _host.TrySetDeveloperAccess(_access);
        if (!active) ReleaseMenuOwnership();
    }

    public DeveloperResult Submit(DeveloperSessionAccess permission, DeveloperExecutionContext context, DeveloperRequest request)
    {
        if (!permission.Capability.IsCompiled || !ReferenceEquals(permission.Capability, DeveloperToolsCapability.Current))
            return DeveloperResult.Rejected(request, request.ExpectedRevision, DeveloperFailureReason.CapabilityUnavailable);
        if (permission.Session != _host.SessionId || context.Session != _host.SessionId || request.Session != _host.SessionId)
            return DeveloperResult.Rejected(request, request.ExpectedRevision, DeveloperFailureReason.StaleSession);
        if (!permission.IsActive || ! _access.IsActive || _disposed || _loading)
            return DeveloperResult.Rejected(request, request.ExpectedRevision, DeveloperFailureReason.SessionNotActivated);
        return request.ExpectedRevision.Domain == DeveloperRevisionDomain.Session
            ? _host.SubmitDeveloper(_access, context, request).Result : _host.SubmitRuntimeDeveloper(_access, request);
    }

    public void AcquireMenuOwnership()
    {
        _open = true;
        _host.MenuPause.TrySetPaused(true);
        RefreshPresets();
    }

    public DeveloperMenuBackendSnapshot Capture()
    {
        while (_host.TryReadDeveloperCompletion(out var completion))
        {
            if (_transfer == completion.Result.Correlation) _transfer = null;
            if (_sessionResults.Count == 64) _sessionResults.Dequeue();
            _sessionResults.Enqueue(completion.Result);
        }
        var state = _host.CaptureControlStatus();
        if (_transfer is null && _access.IsActive && !_disposed &&
            (_open && !state.MenuPauseTransferred && (state.PauseReasons & ClientSimulationPauseReason.Menu) != 0 ||
             !_open && state.MenuPauseTransferred))
        {
            var receipt = _open ? _host.MenuPause.TransferToDeveloper(_access, state.Revision) :
                _host.MenuPause.RestoreFromDeveloper(_access, state.Revision);
            if (receipt.Result.Status == DeveloperRequestStatus.Accepted) _transfer = receipt.Result.Correlation;
        }
        var results = _sessionResults.Concat(_host.CaptureRuntimeDeveloperResults()).TakeLast(64).ToArray();
        return new(state.Session, state.CompletedTick,
            [state.Revision, _host.RuntimeDeveloperRevision(DeveloperRevisionDomain.Parameters), _host.RuntimeDeveloperRevision(DeveloperRevisionDomain.Simulation)],
            results, _entries.ToArray(), state.RequestedSpeed, state.RemainingSteps, _loading,
            state.MenuPauseTransferred, _presetOutcome,
            _runtime.Services.DeveloperParameters is DeveloperGameplayTuningProvider tuning ? tuning.CaptureContribution().Values
                .Select(v => new DeveloperMenuParameterState(v.Id, v.Target, v.Basis, v.Initial, v.Pending)).ToArray() : [], _presetAvailability);
    }

    public void SetWindowBlocked(bool blocked) => _host.WindowPause.TrySetPaused(blocked);
    public void ReleaseMenuOwnership() => _open = false;
    public bool TrySubmitPreset(Guid correlation, DeveloperPresetIntent intent, DeveloperMenuPreset? preset, out string feedback)
    {
        if (_loading || !_access.IsActive || _disposed || correlation == Guid.Empty)
        { feedback = "Preset operation unavailable."; return false; }
        try
        {
            ClientSessionRequest? request = null;
            switch (intent)
            {
                case DeveloperPresetIntent.Import:
                    if (preset is null) throw new InvalidOperationException("Select a local preset first.");
                    _presets.ImportSelected(preset.Key);
                    feedback = "Selected preset imported and validated; ready for restart.";
                    break;
                case DeveloperPresetIntent.Export:
                    _presets.ExportOriginalProfile(_runtime, "original-profile", "Original start profile");
                    feedback = "Original start profile exported.";
                    break;
                case DeveloperPresetIntent.SaveCurrentValues:
                    _presets.SaveCurrentValuesAsNewPreset(_runtime, "Current parameter values");
                    feedback = "Current values saved as a new preset.";
                    break;
                case DeveloperPresetIntent.RestartSelected:
                    if (preset is null) throw new InvalidOperationException("Select a compatible preset first.");
                    request = _presets.CreatePresetRequest(preset.Key, _runtime);
                    feedback = "Selected preset restart queued.";
                    break;
                case DeveloperPresetIntent.RestartSameSeed:
                    request = _presets.CreateRestartSameConfigurationRequest(_runtime);
                    feedback = "Original configuration restart queued.";
                    break;
                case DeveloperPresetIntent.ReturnToBasis:
                    request = _presets.CreateReturnToBaselineRequest(_runtime);
                    feedback = "Baseline restart queued.";
                    break;
                default: throw new ArgumentException("Unknown preset operation.", nameof(intent));
            }
            if (request.HasValue)
            {
                if (!_restart(request.Value.RuntimeSettings!)) throw new InvalidOperationException("Session loader already has a pending request.");
                _loading = true;
                _presetOutcome = null; // Completion belongs to the transactional loader, not admission.
            }
            else
            { RefreshPresets(); _presetOutcome = new(correlation, true, feedback); }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            feedback = error.Message.Length > 200 ? error.Message[..200] : error.Message;
            _presetOutcome = new(correlation, false, feedback); return true;
        }
    }

    private void RefreshPresets()
    {
        try { _entries = _presets.CaptureMenuPresets(); _presetAvailability = _presets.ImportFailure; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { _presetAvailability = "Preset store: " + (error.Message.Length > 180 ? error.Message[..180] : error.Message); }
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<DeveloperMenuActionForm> BuildForms(MatchRuntime runtime)
    {
        static DeveloperMenuActionField Real(string label, double value, double minimum = 0, double maximum = 1_000_000, double step = 1) =>
            new(label, DeveloperValue.FromDouble(value), DeveloperValue.FromDouble(minimum), DeveloperValue.FromDouble(maximum), DeveloperValue.FromDouble(step));
        static DeveloperMenuActionField Choice(string label, DeveloperMenuChoice[] values) =>
            new(label, values[0].Value, DeveloperValue.FromUInt32(values.Min(v => v.Value.AsUInt32())),
                DeveloperValue.FromUInt32(values.Max(v => v.Value.AsUInt32())), DeveloperValue.FromUInt32(1), Array.AsReadOnly(values));
        var resources = runtime.Services.Resources.Definitions.Take(64).Select(d => new DeveloperMenuChoice(d.DisplayName, DeveloperValue.FromUInt32(d.Id.Value))).ToArray();
        var units = runtime.Services.UnitDefinitions.Definitions.Take(64).Select(d => new DeveloperMenuChoice(d.DisplayName, DeveloperValue.FromUInt32(d.Id.Value))).ToArray();
        var weapons = runtime.Services.Weapons.Definitions.Take(64).Select(d => new DeveloperMenuChoice("Weapon " + d.Id.Value, DeveloperValue.FromUInt32(d.Id.Value))).ToArray();
        return Array.AsReadOnly(new[]
        {
            new DeveloperMenuActionForm(DeveloperOperation.GrantResource, [Choice("Resource", resources), Choice("Inventory", [new("Building stock", DeveloperValue.FromUInt32(1)), new("Cargo", DeveloperValue.FromUInt32(2))]), Real("Quantity", 1, 1)]),
            new DeveloperMenuActionForm(DeveloperOperation.SpawnUnit, [Choice("Unit", units), Real("World X", 0, -1_000_000, step: 4), Real("World Y", 0, -1_000_000, step: 4), Real("World Z", 0, -1_000_000, step: 4)]),
            new DeveloperMenuActionForm(DeveloperOperation.RepairEntity, [Real("Health points", 1, 1)]),
            new DeveloperMenuActionForm(DeveloperOperation.RefillUnit, [Real("Fuel", 1), Real("Ammunition", 1)]),
            new DeveloperMenuActionForm(DeveloperOperation.DamageEntity, [Real("Damage", 1, 1), Choice("Weapon", weapons), Real("Incoming X", 0, -1, 1), Real("Incoming Z", 1, -1, 1)])
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        ReleaseMenuOwnership(); SetActivated(false); _disposed = true;
    }
}
