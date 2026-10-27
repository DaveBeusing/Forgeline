using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Platform;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal sealed class NavigationDiagnosticsController(bool authorized)
{
    internal bool OwnsShortcut(PlatformKey key, bool openingThisFrame) =>
        authorized && (Open || openingThisFrame) && key is PlatformKey.F10 or PlatformKey.F11 or PlatformKey.F12;

    internal bool Available => authorized;
    internal NavigationDiagnosticLayers EnabledLayers => _layers;
    internal void ToggleOpen()
    {
        if (!authorized) return;
        Open = !Open;
        if (!Open) { _layers = NavigationDiagnosticLayers.None; _scroll = 0; Feedback = ""; }
    }
    internal void ToggleLayer(NavigationDiagnosticLayers layer)
    {
        if (authorized && Open) _layers ^= layer;
    }
    internal string ScrollDetails(int direction)
    {
        if (authorized && Open) _scroll = Math.Clamp(_scroll + direction, 0, 18);
        return "Navigation detail offset: " + _scroll;
    }
    internal string ExportReport(PresentationSnapshotBuffer snapshots, SimulationSessionId activeSession,
        IReadOnlyCollection<EntityId> selection, PlayerId viewer, string map, ulong seed)
    {
        EntityId selected = selection.Count == 1 ? selection.First() : default;
        var view = new NavigationDiagnosticsView(authorized && Open, Layers, activeSession, selected);
        if (!authorized || !Open || !selected.IsValid || !snapshots.TryReadLatest(out var latest) ||
            !NavigationDiagnosticsViewModel.TryGetVehicle(latest, view, out _) || latest.NavigationDiagnosis?.Viewer != viewer)
            return Feedback = "Export rejected: current owned selection unavailable.";
        var result = NavigationDiagnosticReportExporter.TryWrite(latest, activeSession, latest.Tick, viewer, selected, map, seed);
        return Feedback = result.Exported ? $"Exported {System.IO.Path.GetFileName(result.Path)}" : $"Export failed: {result.Failure}";
    }

    internal void BindSession(SimulationSessionId activeSession)
    {
        if (_session != activeSession)
        {
            _session = activeSession; _selected = default; Open = false; _layers = NavigationDiagnosticLayers.None; _focusedLayer = 0; _scroll = 0; Feedback = "";
        }
    }
    private SimulationSessionId _session;
    private EntityId _selected;
    internal bool Open { get; private set; }
    private NavigationDiagnosticLayers _layers;
    private int _focusedLayer, _scroll;
    internal bool Layers => _layers != NavigationDiagnosticLayers.None;
    internal string Feedback { get; private set; } = "";
    internal NavigationDiagnosticsView Update(bool canInteract, bool toggle, bool toggleLayers, bool export,
        PresentationSnapshotBuffer snapshots, SimulationSessionId activeSession, IReadOnlyCollection<EntityId> selection,
        PlayerId viewer, string map, ulong seed, bool nextLayer = false, bool clearLayers = false, int scrollDelta = 0)
    {
        BindSession(activeSession);
        if (!authorized || !activeSession.IsSpecified) return default;
        if (canInteract && toggle)
        {
            Open = !Open;
            if (!Open) { _layers = NavigationDiagnosticLayers.None; _focusedLayer = 0; _scroll = 0; Feedback = ""; }
        }
        EntityId selected = selection.Count == 1 ? selection.First() : default;
        if (selected != _selected)
        {
            _selected = selected; _layers = NavigationDiagnosticLayers.None; _scroll = 0; Feedback = "";
        }
        if (canInteract && Open)
        {
            if (nextLayer) _focusedLayer = (_focusedLayer + 1) % 6;
            if (toggleLayers) _layers ^= (NavigationDiagnosticLayers)(1 << _focusedLayer);
            if (clearLayers) _layers = NavigationDiagnosticLayers.None;
            _scroll = Math.Clamp(_scroll + scrollDelta, 0, 18);
        }
        var view = new NavigationDiagnosticsView(canInteract && Open, canInteract && Open && Layers,
            activeSession, selected, Feedback, _layers, _focusedLayer, _scroll);
        if (canInteract && Open && export)
        {
            // Resolve the latest publication and actual reconciled selection at the action,
            // never accept a caller's older matching snapshot as freshness authority.
            if (!selected.IsValid || !snapshots.TryReadLatest(out var latest) ||
                !NavigationDiagnosticsViewModel.TryGetVehicle(latest, view, out _) ||
                latest.NavigationDiagnosis?.Viewer != viewer)
                Feedback = "Export rejected: current owned selection unavailable.";
            else
            {
                var result = NavigationDiagnosticReportExporter.TryWrite(latest, activeSession, latest.Tick,
                    viewer, selected, map, seed);
                Feedback = result.Exported ? $"Exported {System.IO.Path.GetFileName(result.Path)}" : $"Export failed: {result.Failure}";
            }
        }
        return view with { Feedback = Feedback };
    }
}
