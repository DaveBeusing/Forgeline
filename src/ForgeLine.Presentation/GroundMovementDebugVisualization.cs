using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class GroundMovementDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        GroundMovementDebugSnapshot snapshot,
        int maximumAgents = 64)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumAgents);

        int count = Math.Min(
            snapshot.Agents.Count,
            maximumAgents);

        for (int index = 0; index < count; index++)
        {
            GroundMovementDebugAgent agent =
                snapshot.Agents[index];

            Vector4 statusColor = agent.Status switch
            {
                GroundMovementStatus.Stuck =>
                    new Vector4(1.0f, 0.2f, 0.15f, 1.0f),
                GroundMovementStatus.Arrived =>
                    new Vector4(0.2f, 1.0f, 0.35f, 1.0f),
                GroundMovementStatus.Moving =>
                    new Vector4(0.2f, 0.7f, 1.0f, 1.0f),
                _ =>
                    new Vector4(0.65f, 0.65f, 0.65f, 1.0f)
            };

            debugDraw.Circle(
                agent.Position,
                agent.SeparationRadius,
                new Vector4(
                    statusColor.X,
                    statusColor.Y,
                    statusColor.Z,
                    0.45f),
                segments: 24);

            if (agent.Velocity.LengthSquared() > 0.0001f)
            {
                debugDraw.Line(
                    agent.Position,
                    agent.Position + agent.Velocity,
                    statusColor);
            }

            if (agent.HasTarget)
            {
                debugDraw.Line(
                    agent.Position,
                    agent.Target,
                    new Vector4(1.0f, 0.8f, 0.2f, 0.8f));
                debugDraw.Point(
                    agent.Target,
                    MathF.Max(agent.Radius, 0.5f),
                    new Vector4(1.0f, 0.8f, 0.2f, 1.0f));
            }
        }
    }
}
