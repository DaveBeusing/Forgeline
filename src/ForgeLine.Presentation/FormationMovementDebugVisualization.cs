using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class FormationMovementDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        FormationMovementDebugSnapshot snapshot,
        int maximumSlots = 128)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSlots);

        Vector4 centroidColor =
            new(1.0f, 0.45f, 0.1f, 1.0f);
        Vector4 boundsColor =
            new(1.0f, 0.65f, 0.2f, 0.75f);
        Vector4 slotColor =
            new(0.25f, 1.0f, 0.55f, 0.9f);
        Vector4 assignmentColor =
            new(0.25f, 0.75f, 1.0f, 0.65f);
        Vector4 routeColor =
            new(0.9f, 0.25f, 1.0f, 0.9f);

        foreach (FormationMovementDebugGroup group in snapshot.Groups)
        {
            Vector3 boundsMinimum = group.BoundsMinimum;
            Vector3 boundsMaximum = group.BoundsMaximum;
            float centerY = group.Centroid.Y;

            boundsMinimum.Y = MathF.Min(
                boundsMinimum.Y,
                centerY - 0.25f);
            boundsMaximum.Y = MathF.Max(
                boundsMaximum.Y,
                centerY + 0.25f);

            debugDraw.Box(
                new AxisAlignedBounds(
                    boundsMinimum,
                    boundsMaximum),
                boundsColor);
            debugDraw.Point(
                group.Centroid + Vector3.UnitY * 0.5f,
                2.0f,
                centroidColor);
            debugDraw.Line(
                group.Centroid + Vector3.UnitY * 0.6f,
                group.ActiveWaypoint + Vector3.UnitY * 0.6f,
                centroidColor);
            debugDraw.Line(
                group.Centroid + Vector3.UnitY,
                group.Centroid +
                    group.Forward * 8.0f +
                    Vector3.UnitY,
                centroidColor);

            debugDraw.Label(
                group.Centroid + Vector3.UnitY * 3.0f,
                $"G{group.Group.Index} {group.Formation} " +
                $"n={group.MemberCount} c={group.CompressionScale:F2} " +
                $"s={group.SplitCohortCount}",
                centroidColor);
        }

        int drawnSlots = Math.Min(
            snapshot.Slots.Count,
            maximumSlots);

        for (int index = 0; index < drawnSlots; index++)
        {
            FormationMovementDebugSlot slot = snapshot.Slots[index];
            Vector3 target =
                slot.SlotTarget + Vector3.UnitY * 0.45f;
            Vector3 member =
                slot.MemberPosition + Vector3.UnitY * 0.45f;

            debugDraw.Point(
                target,
                1.25f,
                slotColor);
            debugDraw.Line(
                member,
                target,
                assignmentColor);
        }

        EntityId previousGroup = EntityId.Invalid;
        Vector3 previousPoint = default;
        bool hasPrevious = false;

        foreach (FormationMovementDebugRoutePoint point in snapshot.RoutePoints)
        {
            Vector3 current =
                point.Position + Vector3.UnitY * 0.8f;

            debugDraw.Point(
                current,
                1.0f,
                routeColor);

            if (hasPrevious &&
                previousGroup == point.Group)
            {
                debugDraw.Line(
                    previousPoint,
                    current,
                    routeColor);
            }

            previousGroup = point.Group;
            previousPoint = current;
            hasPrevious = true;
        }
    }
}
