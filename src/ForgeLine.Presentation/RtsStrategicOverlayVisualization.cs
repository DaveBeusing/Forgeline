using System.Globalization;
using System.Numerics;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public static class RtsStrategicOverlayVisualization
{
    public static void Draw(
        DebugDraw draw,
        StrategicOverlayMode mode,
        StrategicOverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (mode ==
            StrategicOverlayMode.None)
        {
            return;
        }

        bool all =
            mode ==
            StrategicOverlayMode.All;

        // Layer low-priority area information first, then routes/ranges,
        // then operational markers so All remains readable.
        if (all ||
            mode ==
                StrategicOverlayMode.Navigation)
        {
            DrawNavigation(
                draw,
                snapshot,
                all);
        }

        if (all ||
            mode ==
                StrategicOverlayMode.Logistics)
        {
            DrawLogistics(
                draw,
                snapshot,
                all);
        }

        if (all ||
            mode ==
                StrategicOverlayMode.Sensors)
        {
            DrawSensors(
                draw,
                snapshot,
                all);
        }

        if (all ||
            mode ==
                StrategicOverlayMode.Supply)
        {
            DrawSupply(
                draw,
                snapshot,
                all);
        }

        if (all ||
            mode ==
                StrategicOverlayMode.Power)
        {
            DrawPower(
                draw,
                snapshot,
                all);
        }
    }

    private static void DrawNavigation(
        DebugDraw draw,
        StrategicOverlaySnapshot snapshot,
        bool compact)
    {
        int sectorLimit =
            compact
                ? 36
                : 128;
        int sectors =
            Math.Min(
                snapshot.NavigationSectors.Count,
                sectorLimit);
        var sectorColor =
            new Vector4(
                0.18f,
                0.42f,
                0.75f,
                compact
                    ? 0.28f
                    : 0.42f);

        for (int index = 0;
             index < sectors;
             index++)
        {
            StrategicNavigationSectorReadModel sector =
                snapshot.NavigationSectors[index];
            AxisAlignedBounds bounds =
                sector.WorldBounds;
            draw.Box(
                new AxisAlignedBounds(
                    new Vector3(
                        bounds.Minimum.X,
                        bounds.Minimum.Y +
                            0.12f,
                        bounds.Minimum.Z),
                    new Vector3(
                        bounds.Maximum.X,
                        bounds.Minimum.Y +
                            0.16f,
                        bounds.Maximum.Z)),
                sectorColor);
        }

        int portalLimit =
            compact
                ? 48
                : 160;
        int portals =
            Math.Min(
                snapshot.NavigationPortals.Count,
                portalLimit);

        for (int index = 0;
             index < portals;
             index++)
        {
            draw.Point(
                snapshot.NavigationPortals[
                    index].WorldPosition +
                    Vector3.UnitY *
                    0.25f,
                compact
                    ? 1.0f
                    : 1.6f,
                new Vector4(
                    0.35f,
                    0.68f,
                    1.0f,
                    0.85f));
        }
    }

    private static void DrawLogistics(
        DebugDraw draw,
        StrategicOverlaySnapshot snapshot,
        bool compact)
    {
        int linkLimit =
            compact
                ? 64
                : 192;
        int links =
            Math.Min(
                snapshot.LogisticsLinks.Count,
                linkLimit);

        for (int index = 0;
             index < links;
             index++)
        {
            StrategicLogisticsLinkReadModel link =
                snapshot.LogisticsLinks[index];
            Vector4 color =
                link.Enabled
                    ? new Vector4(
                        0.22f,
                        0.72f,
                        1.0f,
                        0.70f)
                    : new Vector4(
                        0.95f,
                        0.30f,
                        0.22f,
                        0.90f);
            Vector3 start =
                link.SourcePosition +
                Vector3.UnitY *
                0.55f;
            Vector3 end =
                link.DestinationPosition +
                Vector3.UnitY *
                0.55f;

            draw.Line(
                start,
                end,
                color);

            if (!link.Enabled)
            {
                DrawCross(
                    draw,
                    (start + end) *
                        0.5f,
                    2.0f,
                    color);
            }
        }

        int nodeLimit =
            compact
                ? 48
                : 128;
        int nodes =
            Math.Min(
                snapshot.LogisticsNodes.Count,
                nodeLimit);
        int labels = 0;

        for (int index = 0;
             index < nodes;
             index++)
        {
            StrategicLogisticsNodeReadModel node =
                snapshot.LogisticsNodes[index];
            Vector4 color =
                node.Enabled
                    ? new Vector4(
                        0.25f,
                        0.92f,
                        0.58f,
                        1.0f)
                    : new Vector4(
                        0.95f,
                        0.32f,
                        0.22f,
                        1.0f);

            draw.Point(
                node.WorldPosition +
                    Vector3.UnitY *
                    0.9f,
                node.Enabled
                    ? 2.2f
                    : 3.0f,
                color);

            if (!node.Enabled)
            {
                DrawCross(
                    draw,
                    node.WorldPosition +
                        Vector3.UnitY *
                        0.9f,
                    2.5f,
                    color);

                if (labels <
                    (compact
                        ? 2
                        : 8))
                {
                    draw.Label(
                        node.WorldPosition +
                            Vector3.UnitY *
                            2.0f,
                        "LOG BLOCKED",
                        color);
                    labels++;
                }
            }
        }
    }

    private static void DrawSupply(
        DebugDraw draw,
        StrategicOverlaySnapshot snapshot,
        bool compact)
    {
        int limit =
            compact
                ? 56
                : 128;
        int count =
            Math.Min(
                snapshot.Supply.Count,
                limit);
        int labels = 0;

        for (int index = 0;
             index < count;
             index++)
        {
            StrategicSupplyReadModel item =
                snapshot.Supply[index];

            if (item.IsProvider)
            {
                Vector4 providerColor =
                    item.ProviderEnabled
                        ? new Vector4(
                            0.20f,
                            0.88f,
                            1.0f,
                            0.80f)
                        : new Vector4(
                            0.90f,
                            0.28f,
                            0.22f,
                            1.0f);

                if (item.ResupplyRangeMeters >
                    0.0f)
                {
                    draw.Circle(
                        item.WorldPosition +
                            Vector3.UnitY *
                            0.18f,
                        item.ResupplyRangeMeters,
                        providerColor,
                        segments:
                            compact
                                ? 16
                                : 24);
                }

                draw.Point(
                    item.WorldPosition +
                        Vector3.UnitY,
                    item.IsDepot
                        ? 3.0f
                        : 2.2f,
                    providerColor);

                if (!item.ProviderEnabled)
                {
                    DrawCross(
                        draw,
                        item.WorldPosition +
                            Vector3.UnitY,
                        2.6f,
                        providerColor);

                    if (labels <
                        (compact
                            ? 2
                            : 8))
                    {
                        draw.Label(
                            item.WorldPosition +
                                Vector3.UnitY *
                                2.0f,
                            "SUPPLY OFFLINE",
                            providerColor);
                        labels++;
                    }
                }
            }

            if (!item.HasUnitState ||
                item.Status ==
                    BattlefieldSupplyStatus.Supplied)
            {
                continue;
            }

            Vector4 statusColor =
                item.Status switch
                {
                    BattlefieldSupplyStatus.LowSupply =>
                        new Vector4(
                            1.0f,
                            0.80f,
                            0.20f,
                            1.0f),
                    BattlefieldSupplyStatus.Critical =>
                        new Vector4(
                            1.0f,
                            0.42f,
                            0.12f,
                            1.0f),
                    _ =>
                        new Vector4(
                            1.0f,
                            0.16f,
                            0.12f,
                            1.0f)
                };

            draw.Point(
                item.WorldPosition +
                    Vector3.UnitY *
                    1.25f,
                item.Status ==
                    BattlefieldSupplyStatus.LowSupply
                    ? 1.8f
                    : 2.8f,
                statusColor);

            if (item.Status is
                BattlefieldSupplyStatus.Critical or
                BattlefieldSupplyStatus.Unsupplied)
            {
                DrawCross(
                    draw,
                    item.WorldPosition +
                        Vector3.UnitY *
                        1.25f,
                    2.4f,
                    statusColor);
            }

            if (labels <
                (compact
                    ? 3
                    : 12))
            {
                string state =
                    item.Status ==
                        BattlefieldSupplyStatus.LowSupply
                        ? "SUPPLY LOW"
                        : item.Status ==
                            BattlefieldSupplyStatus.Critical
                            ? "SUPPLY CRITICAL"
                            : "SUPPLY EMPTY";
                draw.Label(
                    item.WorldPosition +
                        Vector3.UnitY *
                        2.1f,
                    state,
                    statusColor);
                labels++;
            }
        }
    }

    private static void DrawSensors(
        DebugDraw draw,
        StrategicOverlaySnapshot snapshot,
        bool compact)
    {
        int limit =
            compact
                ? 24
                : 64;
        int count =
            Math.Min(
                snapshot.Sensors.Count,
                limit);

        for (int index = 0;
             index < count;
             index++)
        {
            StrategicSensorReadModel sensor =
                snapshot.Sensors[index];

            if (sensor.HasVisual)
            {
                draw.Circle(
                    sensor.WorldPosition +
                        Vector3.UnitY *
                        0.12f,
                    sensor.VisualRangeMeters,
                    new Vector4(
                        0.15f,
                        0.78f,
                        1.0f,
                        compact
                            ? 0.38f
                            : 0.55f),
                    segments:
                        compact
                            ? 16
                            : 24);
            }

            if (sensor.HasRadar)
            {
                draw.Circle(
                    sensor.WorldPosition +
                        Vector3.UnitY *
                        0.18f,
                    sensor.RadarRangeMeters,
                    new Vector4(
                        0.70f,
                        0.32f,
                        1.0f,
                        compact
                            ? 0.38f
                            : 0.58f),
                    segments:
                        compact
                            ? 16
                            : 28);

                if (!compact &&
                    sensor.IdentificationRangeMeters >
                        0.0f)
                {
                    draw.Circle(
                        sensor.WorldPosition +
                            Vector3.UnitY *
                            0.20f,
                        sensor.IdentificationRangeMeters,
                        new Vector4(
                            1.0f,
                            0.60f,
                            0.18f,
                            0.72f),
                        segments: 20);
                }
            }

            draw.Point(
                sensor.WorldPosition +
                    Vector3.UnitY *
                    0.8f,
                sensor.HasRadar
                    ? 2.4f
                    : 1.8f,
                new Vector4(
                    0.78f,
                    0.88f,
                    1.0f,
                    1.0f));
        }
    }

    private static void DrawPower(
        DebugDraw draw,
        StrategicOverlaySnapshot snapshot,
        bool compact)
    {
        int limit =
            compact
                ? 48
                : 128;
        int count =
            Math.Min(
                snapshot.PowerEntities.Count,
                limit);
        int entityLabels = 0;

        for (int index = 0;
             index < count;
             index++)
        {
            StrategicPowerEntityReadModel item =
                snapshot.PowerEntities[index];
            PowerOperationalState state =
                item.IsConsumer
                    ? item.ConsumerState
                    : item.GeneratorState ==
                        PowerGeneratorState.Generating
                        ? PowerOperationalState.Powered
                        : PowerOperationalState.Offline;
            Vector4 color =
                state switch
                {
                    PowerOperationalState.Powered =>
                        new Vector4(
                            0.28f,
                            0.92f,
                            0.48f,
                            1.0f),
                    PowerOperationalState.Brownout =>
                        new Vector4(
                            1.0f,
                            0.70f,
                            0.18f,
                            1.0f),
                    _ =>
                        new Vector4(
                            1.0f,
                            0.22f,
                            0.16f,
                            1.0f)
                };
            Vector3 marker =
                item.WorldPosition +
                Vector3.UnitY *
                (item.IsGenerator
                    ? 1.4f
                    : 1.0f);

            draw.Point(
                marker,
                item.IsGenerator
                    ? 3.2f
                    : state ==
                        PowerOperationalState.Powered
                        ? 2.0f
                        : 2.8f,
                color);

            if (state ==
                PowerOperationalState.Offline)
            {
                DrawCross(
                    draw,
                    marker,
                    2.8f,
                    color);
            }

            if (state == PowerOperationalState.Brownout)
            {
                var a = marker + new Vector3(-3, 0, 0); var b = marker + new Vector3(0, 0, 3);
                var c = marker + new Vector3(3, 0, 0); var d = marker + new Vector3(0, 0, -3);
                draw.Line(a, b, color); draw.Line(b, c, color); draw.Line(c, d, color); draw.Line(d, a, color);
            }

            if (entityLabels <
                    (compact
                        ? 2
                        : 10) &&
                state !=
                    PowerOperationalState.Powered)
            {
                draw.Label(
                    marker +
                        Vector3.UnitY *
                        1.0f,
                    state ==
                        PowerOperationalState.Brownout
                        ? "POWER BROWNOUT"
                        : "POWER OFFLINE",
                    color);
                entityLabels++;
            }
        }

        int networkLabels = 0;
        for (int networkIndex = 0;
             networkIndex <
                 snapshot.PowerNetworks.Count &&
             networkLabels <
                 (compact
                     ? 2
                     : 8);
             networkIndex++)
        {
            StrategicPowerNetworkReadModel network =
                snapshot.PowerNetworks[
                    networkIndex];

            for (int entityIndex = 0;
                 entityIndex < count;
                 entityIndex++)
            {
                StrategicPowerEntityReadModel entity =
                    snapshot.PowerEntities[
                        entityIndex];

                if (entity.NetworkId !=
                    network.NetworkId)
                {
                    continue;
                }

                string label =
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PWR N{network.NetworkId.Value} " +
                        $"{(network.IsConstrained ? "CONSTRAINED" : "STABLE")} " +
                        $"{network.Generation:F0}/{network.Demand:F0}");
                draw.Label(
                    entity.WorldPosition +
                        Vector3.UnitY *
                        3.2f,
                    label,
                    network.IsConstrained
                        ? new Vector4(
                            1.0f,
                            0.62f,
                            0.16f,
                            1.0f)
                        : new Vector4(
                            0.35f,
                            0.92f,
                            0.55f,
                            1.0f));
                networkLabels++;
                break;
            }
        }

        // Deliberately no entity-to-entity lines are drawn here. Current
        // PowerNetworkId membership is logical and is not transmission
        // topology.
    }

    private static void DrawCross(
        DebugDraw draw,
        Vector3 center,
        float halfSize,
        Vector4 color)
    {
        draw.Line(
            center +
                new Vector3(
                    -halfSize,
                    0.0f,
                    -halfSize),
            center +
                new Vector3(
                    halfSize,
                    0.0f,
                    halfSize),
            color);
        draw.Line(
            center +
                new Vector3(
                    -halfSize,
                    0.0f,
                    halfSize),
            center +
                new Vector3(
                    halfSize,
                    0.0f,
                    -halfSize),
            color);
    }
}
