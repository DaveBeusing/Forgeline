using System.Numerics;

namespace ForgeLine.Game;

/// <summary>Coarse travel retains service generations without taking the service owner's local motion.</summary>
internal static class NavigationPrecisionOwnership
{
    internal static bool IsPrecise(in NavigationVehicleObservation vehicle)
    {
        if (vehicle.DirectOrder is MovementOrder direct &&
            (direct.Kind != MovementOrderKind.Strategic || direct.TargetHeadingRadians.HasValue)) return true;
        if (vehicle.DockVisit is { } visit &&
            (visit.LaneEntered || visit.Status is not VehicleDockStatus.Reserved and not VehicleDockStatus.Busy)) return true;
        if (vehicle.QueueEntry is { } queue)
        {
            if (queue.ArrivedAtService || !float.IsFinite(vehicle.PrecisionApproachRadius) || vehicle.PrecisionApproachRadius <= 0) return true;
            Vector2 delta = new(vehicle.Pose.Position.X - queue.Target.X, vehicle.Pose.Position.Z - queue.Target.Z);
            if (delta.LengthSquared() <= vehicle.PrecisionApproachRadius * vehicle.PrecisionApproachRadius) return true;
        }
        return false;
    }

    internal static bool Matches(in NavigationVehicleObservation vehicle, in NavigationOrderBinding binding)
    {
        if (vehicle.DockVisit is { } visit)
        {
            if (visit.Generation != binding.DockGeneration) return false;
            if (visit.Generation > 0 && (vehicle.BuildingSlot is not VehicleDockSlot slot || slot.Truck != vehicle.Entity ||
                slot.Generation != visit.Generation || slot.Owner != vehicle.Owner)) return false;
            if (visit.Generation == 0 && (visit.LaneEntered || visit.Status != VehicleDockStatus.Busy)) return false;
        }
        else if (binding.DockGeneration != 0) return false;
        if (vehicle.QueueEntry is { } queue)
            return queue.Generation == binding.QueueGeneration && queue.Generation > 0 && queue.Owner == vehicle.Owner;
        return binding.QueueGeneration == 0;
    }
}
