using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class TacticalCombatDebugVisualization
{
    public static void Draw(
        DebugDraw debugDraw,
        IReadOnlyList<TacticalCombatDebugEntry> entries,
        TacticalCombatMetrics tacticalMetrics,
        CombatReadinessDebugSnapshot readiness,
        AutomaticResupplyDecisionMetrics resupplyMetrics,
        Vector3 metricsPosition,
        int maximumUnits = 96,
        int maximumReadinessLabels = 64)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumUnits);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumReadinessLabels);

        int count =
            Math.Min(
                entries.Count,
                maximumUnits);

        for (int index = 0;
             index < count;
             index++)
        {
            TacticalCombatDebugEntry entry =
                entries[index];

            Vector4 orderColor =
                ResolveOrderColor(
                    entry.Order);

            debugDraw.Point(
                entry.Position,
                1.25f,
                orderColor);

            if (entry.PursuitLeashMeters > 0.0f)
            {
                debugDraw.Circle(
                    entry.Anchor,
                    entry.PursuitLeashMeters,
                    new Vector4(
                        orderColor.X,
                        orderColor.Y,
                        orderColor.Z,
                        0.35f),
                    segments: 32);
            }

            if (entry.Target.IsValid)
            {
                debugDraw.Line(
                    entry.Position,
                    entry.Destination,
                    new Vector4(
                        1.0f,
                        0.25f,
                        0.2f,
                        0.8f));
            }
            else if (entry.Order is
                     CombatOrderKind.AttackMove or
                     CombatOrderKind.Retreat)
            {
                debugDraw.Line(
                    entry.Position,
                    entry.Destination,
                    new Vector4(
                        orderColor.X,
                        orderColor.Y,
                        orderColor.Z,
                        0.65f));
            }

            string resupply =
                entry.ResupplyRequested
                    ? " supply"
                    : string.Empty;
            string movement =
                entry.MovementAllowed
                    ? " move"
                    : " hold";

            debugDraw.Label(
                entry.Position +
                    Vector3.UnitY * 3.0f,
                $"{entry.Order}/{entry.Status}{movement}{resupply} R={entry.OverallReadiness:P0}",
                orderColor);
        }

        int readinessLabels = 0;

        for (int index = 0;
             index < readiness.Units.Count &&
             readinessLabels < maximumReadinessLabels;
             index++)
        {
            UnitCombatReadinessReadModel unit =
                readiness.Units[index];

            if (unit.Readiness.OverallReadiness >= 0.75)
            {
                continue;
            }

            Vector4 color =
                unit.Readiness.OverallReadiness < 0.35
                    ? new Vector4(
                        1.0f,
                        0.2f,
                        0.15f,
                        1.0f)
                    : new Vector4(
                        1.0f,
                        0.75f,
                        0.2f,
                        1.0f);

            debugDraw.Label(
                unit.Position +
                    Vector3.UnitY * 5.0f,
                $"R {unit.Readiness.OverallReadiness:P0} H {unit.Readiness.Health:P0} A {unit.Readiness.Ammunition:P0} F {unit.Readiness.Fuel:P0}",
                color);
            readinessLabels++;
        }

        CombatReadinessMetrics readinessMetrics =
            readiness.Metrics;

        debugDraw.Label(
            metricsPosition,
            $"TACT orders={tacticalMetrics.OrderedUnits} engage={tacticalMetrics.EngagingUnits} pursue={tacticalMetrics.PursuingUnits} hold={tacticalMetrics.HoldingUnits} retreat={tacticalMetrics.RetreatingUnits} intelWait={tacticalMetrics.WaitingForIntelligenceUnits}",
            new Vector4(
                0.35f,
                0.9f,
                1.0f,
                1.0f));

        debugDraw.Label(
            metricsPosition +
                Vector3.UnitY * 2.0f,
            $"READY units={readinessMetrics.UnitCount} groups={readinessMetrics.GroupCount} avg={readinessMetrics.AverageUnitReadiness:P0} groupAvg={readinessMetrics.AverageGroupReadiness:P0} autoSupply={resupplyMetrics.ActiveResupplyOrders}",
            new Vector4(
                0.45f,
                1.0f,
                0.45f,
                1.0f));
    }

    private static Vector4 ResolveOrderColor(
        CombatOrderKind order) =>
        order switch
        {
            CombatOrderKind.Attack =>
                new Vector4(
                    1.0f,
                    0.25f,
                    0.15f,
                    1.0f),
            CombatOrderKind.AttackMove =>
                new Vector4(
                    1.0f,
                    0.65f,
                    0.15f,
                    1.0f),
            CombatOrderKind.Stop =>
                new Vector4(
                    0.65f,
                    0.65f,
                    0.65f,
                    1.0f),
            CombatOrderKind.HoldPosition =>
                new Vector4(
                    0.35f,
                    0.7f,
                    1.0f,
                    1.0f),
            CombatOrderKind.Retreat =>
                new Vector4(
                    0.8f,
                    0.35f,
                    1.0f,
                    1.0f),
            _ =>
                Vector4.One
        };
}
