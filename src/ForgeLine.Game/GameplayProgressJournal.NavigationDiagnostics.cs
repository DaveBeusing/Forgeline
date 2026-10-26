using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct NavigationDeliveryWindow(SimulationSessionId Session, PlayerId Player,
    SimulationTick StartExclusive, SimulationTick EndInclusive, ulong CompletedDeliveries);

public sealed partial class GameplayProgressJournal
{
    private readonly Dictionary<PlayerId, NavigationDeliveryWindow> _navigationDeliveryWindows = new();
    public void SetNavigationDiagnosticWindow(PlayerId viewer, bool enabled, SimulationTick completedTick)
    {
        if (!viewer.IsSpecified) return;
        if (!enabled) { _navigationDeliveryWindows.Remove(viewer); return; }
        if (!_navigationDeliveryWindows.ContainsKey(viewer) && _navigationDeliveryWindows.Count < 16)
            _navigationDeliveryWindows.Add(viewer, new(_session, viewer, completedTick, completedTick, 0));
    }

    public NavigationDeliveryWindow? CaptureNavigationDeliveryWindow(PlayerId viewer, SimulationTick completedTick)
    {
        if (!_observed || completedTick != _completed ||
            !_navigationDeliveryWindows.TryGetValue(viewer, out var window) || completedTick < window.StartExclusive) return null;
        return window with { EndInclusive = completedTick };
    }

    // Called only at the actual cargo-order completion boundary after unloading.
    // Window state is optional observation, excluded from saves and simulation decisions.
    internal void ObserveCompletedCargoDelivery(PlayerId owner, EntityId truck, EntityId destination,
        double delivered, SimulationTick tick)
    {
        if (!_navigationDeliveryWindows.TryGetValue(owner, out var window) || tick <= window.StartExclusive ||
            window.CompletedDeliveries == ulong.MaxValue || !double.IsFinite(delivered) || delivered <= 0 ||
            !_entities.IsAlive(truck) || !_entities.IsAlive(destination) ||
            !_entities.TryGetComponent(truck, out CargoTransport transport) || transport.Owner != owner ||
            !_entities.TryGetComponent(destination, out ControllableEntity target) || target.Owner != owner) return;
        _navigationDeliveryWindows[owner] = window with { EndInclusive = tick, CompletedDeliveries = window.CompletedDeliveries + 1 };
    }
}
