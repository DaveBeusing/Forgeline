using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class PrototypeBattlefieldDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        PrototypeBattlefieldDefinition definition,
        IReadOnlyDictionary<
            string,
            StrategicInfrastructureOperationalState>? crossingStates = null)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(definition);

        Vector4 startColor =
            new(0.2f, 0.8f, 1.0f, 1.0f);
        Vector4 resourceColor =
            new(0.95f, 0.75f, 0.2f, 1.0f);
        Vector4 contestedResourceColor =
            new(1.0f, 0.35f, 0.15f, 1.0f);
        Vector4 siteColor =
            new(0.45f, 0.95f, 0.45f, 0.8f);
        Vector4 roadColor =
            new(0.65f, 0.65f, 0.65f, 0.8f);
        Vector4 barrierColor =
            new(0.85f, 0.25f, 0.25f, 0.65f);
        Vector4 crossingOperationalColor =
            new(0.2f, 0.95f, 0.5f, 1.0f);
        Vector4 crossingDisabledColor =
            new(1.0f, 0.15f, 0.15f, 1.0f);
        Vector4 crossingRestoringColor =
            new(1.0f, 0.65f, 0.15f, 1.0f);
        Vector4 objectiveColor =
            new(1.0f, 0.2f, 0.9f, 1.0f);

        for (int index = 0;
             index < definition.StaticNavigationObstacles.Count;
             index++)
        {
            debugDraw.Box(
                definition.StaticNavigationObstacles[index],
                barrierColor);
        }

        for (int index = 0;
             index < definition.Starts.Count;
             index++)
        {
            BattlefieldStartPosition start =
                definition.Starts[index];

            debugDraw.Box(
                start.BuildArea,
                startColor);
            debugDraw.Point(
                start.Position + Vector3.UnitY * 2.0f,
                8.0f,
                startColor);
            debugDraw.Label(
                start.Position + Vector3.UnitY * 5.0f,
                $"Start P{start.Player.Value}",
                startColor);
        }

        for (int index = 0;
             index < definition.Resources.Count;
             index++)
        {
            BattlefieldResourceDepositDefinition resource =
                definition.Resources[index];
            Vector4 color =
                resource.Contested
                    ? contestedResourceColor
                    : resourceColor;

            debugDraw.Circle(
                resource.Center + Vector3.UnitY * 1.0f,
                MathF.Max(
                    resource.HalfExtents.X,
                    resource.HalfExtents.Z),
                color,
                segments: 16);
            debugDraw.Label(
                resource.Center + Vector3.UnitY * 3.0f,
                resource.Key,
                color);
        }

        for (int index = 0;
             index < definition.Sites.Count;
             index++)
        {
            BattlefieldSiteDefinition site =
                definition.Sites[index];

            debugDraw.Box(
                site.BuildArea,
                siteColor);
            debugDraw.Label(
                site.Position + Vector3.UnitY * 4.0f,
                site.DisplayName,
                siteColor);
        }

        var roadNodes =
            definition.RoadNodes.ToDictionary(
                static node => node.Key,
                StringComparer.Ordinal);

        for (int index = 0;
             index < definition.RoadEdges.Count;
             index++)
        {
            BattlefieldRoadEdgeDefinition edge =
                definition.RoadEdges[index];
            debugDraw.Line(
                roadNodes[edge.SourceNodeKey].Position +
                Vector3.UnitY * 1.5f,
                roadNodes[edge.DestinationNodeKey].Position +
                Vector3.UnitY * 1.5f,
                roadColor);
        }

        for (int index = 0;
             index < definition.Crossings.Count;
             index++)
        {
            BattlefieldCrossingDefinition crossing =
                definition.Crossings[index];
            StrategicInfrastructureOperationalState state =
                crossingStates is not null &&
                crossingStates.TryGetValue(
                    crossing.Key,
                    out StrategicInfrastructureOperationalState explicitState)
                    ? explicitState
                    : crossing.InitiallyOperational
                        ? StrategicInfrastructureOperationalState.Operational
                        : StrategicInfrastructureOperationalState.Disabled;

            Vector4 color =
                state switch
                {
                    StrategicInfrastructureOperationalState.Operational =>
                        crossingOperationalColor,
                    StrategicInfrastructureOperationalState.Restoring =>
                        crossingRestoringColor,
                    _ =>
                        crossingDisabledColor
                };

            debugDraw.Box(
                crossing.PassageBounds,
                color);
            debugDraw.Label(
                crossing.Position + Vector3.UnitY * 6.0f,
                $"{crossing.DisplayName} [{state}]",
                color);
        }

        for (int index = 0;
             index < definition.Objectives.Count;
             index++)
        {
            BattlefieldObjectiveDefinition objective =
                definition.Objectives[index];

            debugDraw.Point(
                objective.CommandCorePosition +
                Vector3.UnitY * 4.0f,
                12.0f,
                objectiveColor);
            debugDraw.Label(
                objective.CommandCorePosition +
                Vector3.UnitY * 8.0f,
                $"Command Core P{objective.Owner.Value}",
                objectiveColor);
        }
    }
}
