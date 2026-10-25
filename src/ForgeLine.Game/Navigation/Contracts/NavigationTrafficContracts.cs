using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum NavigationReservationKind : byte { Unspecified = 0, Passage = 1, Dock = 2, Queue = 3 }
public readonly record struct NavigationTrafficReservation(
    ulong ReservationId, EntityId Owner, ulong Generation, ulong CommandGeneration, NavigationReservationKind Kind,
    NavigationPose Pose, Vector3 HalfExtents, SimulationTick CreatedAt,
    SimulationTick ReconsiderAt, bool PhysicallyOccupied);

/// <summary>Internal physical observations; never pass this unfiltered frame to presentation.</summary>
public sealed class NavigationTrafficFrame
{
    public NavigationTrafficFrame(SimulationTick tick, NavigationCollisionFrame collision,
        bool isComplete, ReadOnlySpan<NavigationVehicleObservation> vehicles,
        ReadOnlySpan<NavigationTrafficReservation> reservations, NavigationIntegrationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(collision);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        if (collision.Tick != tick.Value || vehicles.Length > limits.MaximumNeighbors ||
            reservations.Length > limits.MaximumReservations)
            throw new ArgumentException("Traffic observations must share a tick and obey capture bounds.");
        var identities = new HashSet<EntityId>();
        foreach (NavigationVehicleObservation vehicle in vehicles)
            if (!vehicle.Entity.IsValid || !vehicle.Pose.IsValid || !vehicle.Profile.IsValid || !identities.Add(vehicle.Entity))
                throw new ArgumentException("Traffic vehicles require distinct complete identities and valid geometry.", nameof(vehicles));
        foreach (NavigationTrafficReservation reservation in reservations)
            if (reservation.ReservationId == 0 || !reservation.Owner.IsValid || reservation.Generation == 0 ||
                !reservation.Pose.IsValid || !Enum.IsDefined(reservation.Kind) || reservation.Kind == NavigationReservationKind.Unspecified ||
                !float.IsFinite(reservation.HalfExtents.LengthSquared()) || reservation.HalfExtents.X <= 0 ||
                reservation.HalfExtents.Y <= 0 || reservation.HalfExtents.Z <= 0)
                throw new ArgumentException("Reservation snapshots must retain identity, generation and physical geometry.", nameof(reservations));
        Tick = tick;
        Collision = collision;
        IsComplete = isComplete && collision.IsComplete;
        Vehicles = Array.AsReadOnly(vehicles.ToArray());
        Reservations = Array.AsReadOnly(reservations.ToArray());
    }

    public SimulationTick Tick { get; }
    public NavigationCollisionFrame Collision { get; }
    public bool IsComplete { get; }
    public IReadOnlyList<NavigationVehicleObservation> Vehicles { get; }
    public IReadOnlyList<NavigationTrafficReservation> Reservations { get; }
}

public enum NavigationTrafficAction : byte { Hold = 0, Proceed = 1, Yield = 2, Recovery = 3 }
public readonly record struct NavigationTrafficDirective(
    NavigationModuleAvailability Availability, NavigationOrderBinding Binding, SimulationTick Tick,
    NavigationTrafficAction Action, float MaximumSpeed, Vector3? SuggestedTarget, NavigationBlockReason Reason);

public interface INavigationTrafficPlanner<TCheckpoint> : INavigationStateTransfer<TCheckpoint> where TCheckpoint : class
{
    NavigationModuleAvailability Availability { get; }
    NavigationTrafficDirective Plan(NavigationTrafficFrame frame, in NavigationOrderBinding binding,
        INavigationGeometryQuery geometry, int workAllowance, out int workConsumed);
}
