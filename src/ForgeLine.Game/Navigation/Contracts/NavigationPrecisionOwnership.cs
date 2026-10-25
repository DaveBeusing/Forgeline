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
            (visit.LaneEntered || visit.Status is not VehicleDockStatus.Reserved and not VehicleDockStatus.Busy and not VehicleDockStatus.Blocked)) return true;
        if (vehicle.QueueEntry is { } queue)
        {
            if (queue.ArrivedAtService || !float.IsFinite(vehicle.PrecisionApproachRadius) || vehicle.PrecisionApproachRadius <= 0) return true;
            Vector2 delta = new(vehicle.Pose.Position.X - queue.Target.X, vehicle.Pose.Position.Z - queue.Target.Z);
            if (delta.LengthSquared() <= vehicle.PrecisionApproachRadius * vehicle.PrecisionApproachRadius) return true;
        }
        return false;
    }

    internal static bool TryBeginOwnedQueueApproach(in NavigationVehicleObservation vehicle, out MovementOrder local)
    {
        local = default;
        var original = vehicle.RouteOrder ?? vehicle.DirectOrder;
        if (original is not { Kind: MovementOrderKind.Strategic } root || root.Issuer != vehicle.Owner ||
            vehicle.QueueEntry is not { Generation: > 0, Position: 1, ArrivedAtService: false } queue || queue.Owner != vehicle.Owner ||
            root.WorldTarget != queue.Target || !IsPrecise(vehicle) ||
            vehicle.DockVisit is not { Generation: > 0, LaneEntered: false } reserved || reserved.Building != queue.Building ||
            reserved.Pose.ApproachStart != queue.Target || vehicle.BuildingSlot is not { } slot ||
            slot.Truck != vehicle.Entity || slot.Owner != vehicle.Owner || slot.Generation != reserved.Generation || slot.Pose != reserved.Pose)
            return false;
        local = root with { Kind = MovementOrderKind.LocalApproach, TargetHeadingRadians = queue.Heading };
        return true;
    }

    internal static bool Matches(in NavigationVehicleObservation vehicle, in NavigationOrderBinding binding)
    {
        if (vehicle.DockVisit is { } visit)
        {
            if (visit.Generation != binding.DockGeneration) return false;
            if (visit.Generation > 0 && (vehicle.BuildingSlot is not VehicleDockSlot slot || slot.Truck != vehicle.Entity ||
                slot.Generation != visit.Generation || slot.Owner != vehicle.Owner)) return false;
            if (visit.Generation == 0 && (visit.LaneEntered || visit.Status is not VehicleDockStatus.Busy and not VehicleDockStatus.Blocked)) return false;
        }
        else if (binding.DockGeneration != 0) return false;
        if (vehicle.QueueEntry is { } queue)
            return queue.Generation == binding.QueueGeneration && queue.Generation > 0 && queue.Owner == vehicle.Owner;
        return binding.QueueGeneration == 0;
    }
}
