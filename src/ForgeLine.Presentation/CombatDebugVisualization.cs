using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class CombatDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        CombatDebugSnapshot snapshot,
        int maximumWeapons = 64,
        int maximumProjectiles = 256,
        int maximumHealthLabels = 64,
        int maximumImpacts = 128)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumWeapons);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumProjectiles);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumHealthLabels);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumImpacts);

        DrawWeapons(
            debugDraw,
            snapshot,
            maximumWeapons);
        DrawProjectiles(
            debugDraw,
            snapshot,
            maximumProjectiles);
        DrawImpacts(
            debugDraw,
            snapshot,
            maximumImpacts);
        DrawHealth(
            debugDraw,
            snapshot,
            maximumHealthLabels);
    }

    private static void DrawWeapons(
        DebugDraw debugDraw,
        CombatDebugSnapshot snapshot,
        int maximumWeapons)
    {
        int count =
            Math.Min(
                snapshot.Weapons.Count,
                maximumWeapons);

        for (int index = 0;
             index < count;
             index++)
        {
            CombatWeaponReadModel weapon =
                snapshot.Weapons[index];

            debugDraw.Circle(
                weapon.Position,
                weapon.RangeMeters,
                new Vector4(
                    0.95f,
                    0.75f,
                    0.2f,
                    0.4f),
                segments: 32);

            if (weapon.HasTarget)
            {
                debugDraw.Line(
                    weapon.Position,
                    weapon.TargetPosition,
                    new Vector4(
                        1.0f,
                        0.35f,
                        0.2f,
                        0.8f));
            }
        }
    }

    private static void DrawProjectiles(
        DebugDraw debugDraw,
        CombatDebugSnapshot snapshot,
        int maximumProjectiles)
    {
        int count =
            Math.Min(
                snapshot.Projectiles.Count,
                maximumProjectiles);

        for (int index = 0;
             index < count;
             index++)
        {
            CombatProjectileReadModel projectile =
                snapshot.Projectiles[index];

            debugDraw.Point(
                projectile.Position,
                0.75f,
                new Vector4(
                    1.0f,
                    0.8f,
                    0.15f,
                    1.0f));

            if (projectile.Velocity.LengthSquared() >
                0.0001f)
            {
                debugDraw.Line(
                    projectile.Position,
                    projectile.Position +
                        Vector3.Normalize(
                            projectile.Velocity) *
                        2.0f,
                    new Vector4(
                        1.0f,
                        0.8f,
                        0.15f,
                        0.8f));
            }
        }
    }

    private static void DrawImpacts(
        DebugDraw debugDraw,
        CombatDebugSnapshot snapshot,
        int maximumImpacts)
    {
        int count =
            Math.Min(
                snapshot.Impacts.Count,
                maximumImpacts);

        for (int index = 0;
             index < count;
             index++)
        {
            CombatImpactReadModel impact =
                snapshot.Impacts[index];

            debugDraw.Point(
                impact.Position,
                1.5f,
                new Vector4(
                    1.0f,
                    0.2f,
                    0.1f,
                    1.0f));
        }
    }

    private static void DrawHealth(
        DebugDraw debugDraw,
        CombatDebugSnapshot snapshot,
        int maximumHealthLabels)
    {
        int labels = 0;

        for (int index = 0;
             index < snapshot.Health.Count &&
             labels < maximumHealthLabels;
             index++)
        {
            CombatHealthReadModel health =
                snapshot.Health[index];

            if (health.Current >= health.Maximum)
            {
                continue;
            }

            double fraction =
                health.Maximum <= 0.0
                    ? 0.0
                    : health.Current /
                      health.Maximum;

            Vector4 color =
                fraction <= 0.25
                    ? new Vector4(
                        1.0f,
                        0.15f,
                        0.15f,
                        1.0f)
                    : fraction <= 0.5
                        ? new Vector4(
                            1.0f,
                            0.55f,
                            0.15f,
                            1.0f)
                        : new Vector4(
                            0.95f,
                            0.85f,
                            0.2f,
                            1.0f);

            string current =
                health.Current.ToString(
                    "F0",
                    System.Globalization.CultureInfo.InvariantCulture);
            string maximum =
                health.Maximum.ToString(
                    "F0",
                    System.Globalization.CultureInfo.InvariantCulture);

            debugDraw.Label(
                health.Position +
                    Vector3.UnitY * 2.0f,
                $"HP {current}/{maximum}",
                color);
            labels++;
        }
    }
}
