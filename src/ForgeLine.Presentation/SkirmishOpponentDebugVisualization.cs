using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class SkirmishOpponentDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        IReadOnlyList<SkirmishOpponentDebugReadModel> opponents,
        int maximumLabels = 8)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(opponents);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        Vector4 homeColor =
            new(0.25f, 0.8f, 1.0f, 1.0f);
        Vector4 objectiveColor =
            new(1.0f, 0.45f, 0.15f, 1.0f);
        Vector4 healthyColor =
            new(0.2f, 0.95f, 0.4f, 1.0f);
        Vector4 warningColor =
            new(1.0f, 0.75f, 0.15f, 1.0f);

        int labels = 0;

        for (int index = 0;
             index < opponents.Count;
             index++)
        {
            SkirmishOpponentDebugReadModel opponent =
                opponents[index];

            debugDraw.Circle(
                opponent.HomePosition +
                Vector3.UnitY * 1.0f,
                32.0f,
                homeColor,
                segments: 20);
            debugDraw.Point(
                opponent.HomePosition +
                Vector3.UnitY * 2.0f,
                8.0f,
                homeColor);

            if (opponent.HasChosenObjective)
            {
                debugDraw.Line(
                    opponent.HomePosition +
                    Vector3.UnitY * 4.0f,
                    opponent.ChosenObjective +
                    Vector3.UnitY * 4.0f,
                    objectiveColor);
                debugDraw.Point(
                    opponent.ChosenObjective +
                    Vector3.UnitY * 3.0f,
                    10.0f,
                    objectiveColor);
            }

            if (labels >= maximumLabels)
            {
                continue;
            }

            Vector4 statusColor =
                opponent.Economy.HealthScore >= 0.55 &&
                opponent.Force.AverageReadiness >= 0.45
                    ? healthyColor
                    : warningColor;

            debugDraw.Label(
                opponent.HomePosition +
                Vector3.UnitY * 10.0f,
                $"P{opponent.Player.Value} {opponent.StrategicState} / {opponent.ActiveGoal}",
                statusColor);
            debugDraw.Label(
                opponent.HomePosition +
                Vector3.UnitY * 14.0f,
                $"Econ {opponent.Economy.HealthScore:P0} Power {opponent.Economy.PowerGeneration:F0}/{opponent.Economy.PowerDemand:F0} Units {opponent.Force.TotalUnits} Ready {opponent.Force.AverageReadiness:P0} Contacts {opponent.Force.CurrentHostileContacts}",
                statusColor);
            labels += 2;
        }
    }
}
