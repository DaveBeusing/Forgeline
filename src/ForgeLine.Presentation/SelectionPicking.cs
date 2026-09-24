using System.Numerics;
using ForgeLine.Core;

namespace ForgeLine.Presentation;

public static class SelectionPicking
{
    private const float DirectionEpsilon = 1e-6f;

    public static bool TryPick(
        RtsCamera camera,
        RenderWorld world,
        in SelectionFilter filter,
        Vector2 screenPoint,
        int viewportWidth,
        int viewportHeight,
        float interpolationAlpha,
        out EntityId entity)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);

        CameraRay ray = camera.ScreenPointToWorldRay(
            screenPoint,
            viewportWidth,
            viewportHeight);

        float nearestDistance = float.PositiveInfinity;
        EntityId nearest = EntityId.Invalid;

        for (int index = 0; index < world.InstanceCount; index++)
        {
            RenderInstance instance =
                world.GetInterpolatedInstance(index, interpolationAlpha);

            if (!IsSelectable(
                    instance,
                    camera,
                    filter,
                    viewportWidth,
                    viewportHeight))
            {
                continue;
            }

            Vector3 extents = Vector3.Max(
                Vector3.Abs(instance.Transform.Scale) * 0.5f,
                new Vector3(0.05f));

            if (!TryIntersectBounds(
                    ray,
                    instance.Transform.Position - extents,
                    instance.Transform.Position + extents,
                    out float distance) ||
                distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearest = instance.Entity;
        }

        entity = nearest;
        return entity.IsValid;
    }

    public static EntityId[] PickBox(
        RtsCamera camera,
        RenderWorld world,
        in SelectionFilter filter,
        Vector2 firstCorner,
        Vector2 secondCorner,
        int viewportWidth,
        int viewportHeight,
        float interpolationAlpha)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(world);

        Vector2 minimum = Vector2.Min(firstCorner, secondCorner);
        Vector2 maximum = Vector2.Max(firstCorner, secondCorner);
        var result = new List<EntityId>();

        for (int index = 0; index < world.InstanceCount; index++)
        {
            RenderInstance instance =
                world.GetInterpolatedInstance(index, interpolationAlpha);

            if (!IsSelectable(
                    instance,
                    camera,
                    filter,
                    viewportWidth,
                    viewportHeight))
            {
                continue;
            }

            ScreenProjection projection = camera.WorldToScreen(
                instance.Transform.Position,
                viewportWidth,
                viewportHeight);

            Vector2 point = projection.Position;
            if (point.X < minimum.X ||
                point.X > maximum.X ||
                point.Y < minimum.Y ||
                point.Y > maximum.Y)
            {
                continue;
            }

            result.Add(instance.Entity);
        }

        result.Sort();
        return result.ToArray();
    }

    private static bool IsSelectable(
        in RenderInstance instance,
        RtsCamera camera,
        in SelectionFilter filter,
        int viewportWidth,
        int viewportHeight)
    {
        if ((instance.Visibility & RenderVisibilityMask.World) == 0 ||
            !instance.Mesh.IsValid ||
            !instance.Material.IsValid ||
            !filter.Allows(instance.Selectable))
        {
            return false;
        }

        ScreenProjection projection = camera.WorldToScreen(
            instance.Transform.Position,
            viewportWidth,
            viewportHeight);

        return projection.IsVisible;
    }

    private static bool TryIntersectBounds(
        in CameraRay ray,
        Vector3 minimum,
        Vector3 maximum,
        out float distance)
    {
        float near = 0.0f;
        float far = float.PositiveInfinity;

        if (!IntersectAxis(
                ray.Origin.X,
                ray.Direction.X,
                minimum.X,
                maximum.X,
                ref near,
                ref far) ||
            !IntersectAxis(
                ray.Origin.Y,
                ray.Direction.Y,
                minimum.Y,
                maximum.Y,
                ref near,
                ref far) ||
            !IntersectAxis(
                ray.Origin.Z,
                ray.Direction.Z,
                minimum.Z,
                maximum.Z,
                ref near,
                ref far))
        {
            distance = default;
            return false;
        }

        distance = near;
        return float.IsFinite(distance) && distance >= 0.0f;
    }

    private static bool IntersectAxis(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float near,
        ref float far)
    {
        if (MathF.Abs(direction) <= DirectionEpsilon)
        {
            return origin >= minimum && origin <= maximum;
        }

        float inverse = 1.0f / direction;
        float first = (minimum - origin) * inverse;
        float second = (maximum - origin) * inverse;

        if (first > second)
        {
            (first, second) = (second, first);
        }

        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far;
    }
}
