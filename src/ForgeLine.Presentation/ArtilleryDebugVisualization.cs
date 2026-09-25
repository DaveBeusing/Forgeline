using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class ArtilleryDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        ArtilleryDebugSnapshot snapshot,
        Vector3 metricsPosition,
        int maximumMissions = 64,
        int maximumProjectiles = 128)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMissions);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumProjectiles);

        int missionCount =
            Math.Min(
                snapshot.Missions.Count,
                maximumMissions);

        for (int index = 0;
             index < missionCount;
             index++)
        {
            ArtilleryMissionDebugEntry mission =
                snapshot.Missions[index];

            debugDraw.Circle(
                mission.SourcePosition,
                mission.MinimumRangeMeters,
                new Vector4(1.0f, 0.55f, 0.15f, 0.45f),
                segments: 32);
            debugDraw.Circle(
                mission.SourcePosition,
                mission.MaximumRangeMeters,
                new Vector4(1.0f, 0.25f, 0.15f, 0.35f),
                segments: 48);
            debugDraw.Line(
                mission.SourcePosition,
                mission.TargetPosition,
                new Vector4(1.0f, 0.7f, 0.15f, 0.75f));
            debugDraw.Circle(
                mission.TargetPosition,
                mission.AreaRadiusMeters,
                new Vector4(1.0f, 0.15f, 0.1f, 0.8f),
                segments: 24);
            debugDraw.Label(
                mission.TargetPosition + Vector3.UnitY * 2.0f,
                $"{mission.Status} {mission.RoundsFired}/{mission.RequestedRounds}",
                new Vector4(1.0f, 0.8f, 0.3f, 1.0f));
        }

        int projectileCount =
            Math.Min(
                snapshot.Projectiles.Count,
                maximumProjectiles);

        for (int index = 0;
             index < projectileCount;
             index++)
        {
            ArtilleryProjectileDebugEntry projectile =
                snapshot.Projectiles[index];

            debugDraw.Point(
                projectile.Position,
                1.25f,
                new Vector4(1.0f, 0.9f, 0.25f, 1.0f));

            const int trajectorySegments = 16;
            Vector3 previous =
                projectile.LaunchPosition;

            for (int segment = 1;
                 segment <= trajectorySegments;
                 segment++)
            {
                float progress =
                    segment /
                    (float)trajectorySegments;
                Vector3 next =
                    Vector3.Lerp(
                        projectile.LaunchPosition,
                        projectile.TargetPosition,
                        progress);
                next.Y +=
                    4.0f *
                    projectile.ApexHeightMeters *
                    progress *
                    (1.0f - progress);

                debugDraw.Line(
                    previous,
                    next,
                    new Vector4(1.0f, 0.75f, 0.2f, 0.55f));
                previous = next;
            }

            debugDraw.Circle(
                projectile.TargetPosition,
                projectile.AreaRadiusMeters,
                new Vector4(1.0f, 0.2f, 0.1f, 0.6f),
                segments: 24);
        }

        ArtilleryFireMissionMetrics metrics =
            snapshot.Metrics;
        debugDraw.Label(
            metricsPosition,
            $"ART missions={metrics.ActiveMissions} noAmmo={metrics.NoAmmoMissions} shells={metrics.ShellsInFlight} shots={metrics.ShotsFiredThisTick} impacts={metrics.ImpactsThisTick} dmgQ={metrics.AreaDamageQueuedThisTick:F1}",
            new Vector4(1.0f, 0.82f, 0.35f, 1.0f));
    }
}
