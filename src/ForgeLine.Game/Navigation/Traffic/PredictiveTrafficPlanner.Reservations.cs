using System.Numerics;
using ForgeLine.Navigation;

namespace ForgeLine.Game;

internal sealed partial class PredictiveTrafficPlanner
{
    internal static int ServiceReservationCheckWork(int poseCount) => 4 * Math.Clamp(poseCount, 2, PredictionSteps + 1);

    // A foreign dock grants no admission. An existing occupant can only evacuate
    // monotonically away from its centre, without crossing it or reentering.
    // Physical geometry and the actual accepted step remain independent vetoes.
    internal static bool BlocksServiceReservation(in NavigationVehicleObservation vehicle,
        in NavigationTrafficReservation reservation, ReadOnlySpan<NavigationPose> path)
    {
        if (!ReservationApplies(vehicle, reservation)) return false;
        if (path.Length < 2 || path.Length > PredictionSteps + 1) return true;
        var region = new NavigationTrafficPassage(reservation.ReservationId, reservation.Pose, reservation.HalfExtents);
        if (!Touches(region, path, vehicle.Profile)) return false;
        if (reservation.Kind != NavigationReservationKind.Dock ||
            !Touches(region, [path[0], path[0]], vehicle.Profile)) return true;
        for (int step = 1; step < path.Length; step++)
        {
            if (!Touches(region, [path[step - 1], path[step]], vehicle.Profile)) continue;
            if (!IsOutwardDeparture(region, path[step - 1], path[step], vehicle.Profile)) return true;
            Quaternion inverse = Quaternion.Conjugate(region.Pose.Rotation);
            Vector3 first = Vector3.Transform(path[step - 1].Position - region.Pose.Position, inverse);
            Vector3 last = Vector3.Transform(path[step].Position - region.Pose.Position, inverse);
            float radialChange = first.X * (last.X - first.X) / (region.HalfExtents.X * region.HalfExtents.X) +
                first.Z * (last.Z - first.Z) / (region.HalfExtents.Z * region.HalfExtents.Z);
            // At the exact centre no existing outward side can be established.
            if (!(radialChange > 0)) return true;
        }
        return false;
    }
}
