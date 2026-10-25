using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

/// <summary>Default is explicitly disconnected for all six integration slots.</summary>
public readonly record struct NavigationModuleConnections(
    NavigationModuleAvailability Traffic, NavigationModuleAvailability Motion,
    NavigationModuleAvailability Routing, NavigationModuleAvailability Scheduling,
    NavigationModuleAvailability RegionalUpdates, NavigationModuleAvailability Diagnostics);

public readonly record struct NavigationDiagnosticEvent(
    SimulationTick Tick, EntityId Entity, ulong RequestId, NavigationBlockReason Reason);
public readonly record struct NavigationVehicleDiagnostic(
    PlayerId Owner, EntityId Entity, ulong CommandGeneration, Vector3 OriginalTarget,
    GroundMovementStatus MovementStatus, NavigationModuleAvailability SearchAvailability,
    NavigationSearchStatus SearchStatus, NavigationBlockReason BlockReason,
    SimulationTick LastProgressTick, int QueuePosition, bool OwnsDock, EntityId? VisibleBlocker);

/// <summary>Viewer-scoped copies only; visibility filtering occurs before construction.</summary>
public sealed class NavigationDiagnosticFrame
{
    public NavigationDiagnosticFrame(PlayerId viewer, SimulationTick tick, NavigationModuleConnections modules,
        ReadOnlySpan<NavigationVehicleDiagnostic> vehicles, ReadOnlySpan<NavigationDiagnosticEvent> events,
        NavigationIntegrationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (!viewer.IsSpecified || vehicles.Length > limits.MaximumNeighbors || events.Length > limits.MaximumDiagnosticEvents)
            throw new ArgumentException("Diagnostics require a viewer and bounded copied observations.");
        var allowed = new HashSet<EntityId>();
        foreach (NavigationVehicleDiagnostic vehicle in vehicles)
            if (vehicle.Owner != viewer || !vehicle.Entity.IsValid || !allowed.Add(vehicle.Entity))
                throw new ArgumentException("Only distinct owned vehicle observations may enter this frame.", nameof(vehicles));
        foreach (NavigationDiagnosticEvent item in events)
            if (!allowed.Contains(item.Entity) || item.Tick > tick)
                throw new ArgumentException("Diagnostic events must refer to this frame's authorized vehicles.", nameof(events));
        Viewer = viewer;
        Tick = tick;
        Modules = modules;
        Vehicles = Array.AsReadOnly(vehicles.ToArray());
        Events = Array.AsReadOnly(events.ToArray());
    }

    public PlayerId Viewer { get; }
    public SimulationTick Tick { get; }
    public NavigationModuleConnections Modules { get; }
    public IReadOnlyList<NavigationVehicleDiagnostic> Vehicles { get; }
    public IReadOnlyList<NavigationDiagnosticEvent> Events { get; }
}

/// <summary>Optional observer; false means full/unavailable and must not change simulation decisions.</summary>
public interface INavigationDiagnosticsSink
{
    bool TryPublish(NavigationDiagnosticFrame frame);
}
