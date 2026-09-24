using System.Numerics;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class NavigationDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        NavigationWorld world,
        in NavigationCapabilities capabilities,
        NavigationPath? path,
        Vector3 focus,
        int cellRadius = 8,
        int sectorRadius = 2,
        int maximumPortals = 128)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegative(cellRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(sectorRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPortals);

        NavigationGrid grid = world.Grid;
        if (!grid.TryWorldToCell(
                focus,
                out NavigationCellCoordinate focusCell))
        {
            DrawPath(debugDraw, path);
            return;
        }

        Vector4 traversableColor =
            new(0.15f, 0.85f, 0.35f, 0.45f);
        Vector4 blockedColor =
            new(1.0f, 0.2f, 0.15f, 0.75f);
        Vector4 sectorColor =
            new(0.25f, 0.55f, 1.0f, 0.8f);
        Vector4 portalColor =
            new(1.0f, 0.75f, 0.15f, 1.0f);

        int minimumX = Math.Max(0, focusCell.X - cellRadius);
        int maximumX = Math.Min(
            grid.Width - 1,
            focusCell.X + cellRadius);
        int minimumZ = Math.Max(0, focusCell.Z - cellRadius);
        int maximumZ = Math.Min(
            grid.Height - 1,
            focusCell.Z + cellRadius);

        for (int z = minimumZ; z <= maximumZ; z++)
        {
            for (int x = minimumX; x <= maximumX; x++)
            {
                NavigationCellCoordinate cell = new(x, z);
                bool traversable =
                    grid.IsTraversable(cell, capabilities);

                debugDraw.Box(
                    grid.GetCellBounds(
                        cell,
                        verticalHalfExtent: 0.03f),
                    traversable
                        ? traversableColor
                        : blockedColor);
            }
        }

        NavigationSectorGraph graph =
            world.GetSectorGraph(capabilities);
        NavigationSectorCoordinate focusSector =
            graph.GetSector(focusCell);

        for (int sectorZ =
                 Math.Max(0, focusSector.Z - sectorRadius);
             sectorZ <=
                 Math.Min(
                     graph.Height - 1,
                     focusSector.Z + sectorRadius);
             sectorZ++)
        {
            for (int sectorX =
                     Math.Max(0, focusSector.X - sectorRadius);
                 sectorX <=
                     Math.Min(
                         graph.Width - 1,
                         focusSector.X + sectorRadius);
                 sectorX++)
            {
                NavigationSectorCoordinate sector =
                    new(sectorX, sectorZ);

                if (!graph.Contains(sector))
                {
                    continue;
                }

                AxisAlignedBounds horizontal =
                    graph.GetWorldBounds(sector);
                debugDraw.Box(
                    new AxisAlignedBounds(
                        new Vector3(
                            horizontal.Minimum.X,
                            focus.Y + 0.1f,
                            horizontal.Minimum.Z),
                        new Vector3(
                            horizontal.Maximum.X,
                            focus.Y + 0.2f,
                            horizontal.Maximum.Z)),
                    sectorColor);
            }
        }

        float visibleRadius =
            (cellRadius + 2) * grid.Settings.CellSizeMeters;
        float visibleRadiusSquared =
            visibleRadius * visibleRadius;
        int drawnPortals = 0;

        foreach (NavigationPortal portal in graph.Portals)
        {
            if (drawnPortals >= maximumPortals)
            {
                break;
            }

            Vector2 delta = new(
                portal.WorldPosition.X - focus.X,
                portal.WorldPosition.Z - focus.Z);

            if (delta.LengthSquared() > visibleRadiusSquared)
            {
                continue;
            }

            debugDraw.Point(
                portal.WorldPosition + Vector3.UnitY * 0.2f,
                1.5f,
                portalColor);
            drawnPortals++;
        }

        DrawPath(debugDraw, path);
    }

    private static void DrawPath(
        DebugDraw debugDraw,
        NavigationPath? path)
    {
        if (path is null)
        {
            return;
        }

        Vector4 highLevelColor =
            new(1.0f, 0.75f, 0.1f, 1.0f);
        Vector4 refinedColor =
            new(0.95f, 0.2f, 1.0f, 1.0f);

        if (path.Portals.Count > 1)
        {
            for (int index = 1; index < path.Portals.Count; index++)
            {
                debugDraw.Line(
                    path.Portals[index - 1].WorldPosition +
                        Vector3.UnitY * 0.35f,
                    path.Portals[index].WorldPosition +
                        Vector3.UnitY * 0.35f,
                    highLevelColor);
            }
        }

        if (path.Waypoints.Count == 0)
        {
            return;
        }

        for (int index = 0; index < path.Waypoints.Count; index++)
        {
            Vector3 waypoint =
                path.Waypoints[index] + Vector3.UnitY * 0.5f;
            debugDraw.Point(
                waypoint,
                1.0f,
                refinedColor);

            if (index > 0)
            {
                debugDraw.Line(
                    path.Waypoints[index - 1] +
                        Vector3.UnitY * 0.5f,
                    waypoint,
                    refinedColor);
            }
        }
    }
}
