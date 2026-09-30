using System.Numerics;
using ForgeLine.Intelligence;
using ForgeLine.Navigation;

namespace ForgeLine.Presentation;

public static class RtsStrategicOverlayVisualization
{
    public static void Draw(
        DebugDraw draw,
        StrategicOverlayMode mode,
        PresentationDebugSnapshot debug,
        FactionIntelligenceSnapshot? intelligence,
        Vector3 cameraTarget)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(debug);

        if (mode ==
            StrategicOverlayMode.None)
        {
            return;
        }

        bool all =
            mode ==
            StrategicOverlayMode.All;

        if (all ||
            mode ==
            StrategicOverlayMode.Logistics)
        {
            if (debug.Logistics is not null)
            {
                LogisticsDebugVisualization.Draw(
                    draw,
                    debug.Logistics,
                    maximumNodes: 128,
                    maximumEdges: 256,
                    maximumLabels: 0);
            }

            if (debug.CargoTransport is not null)
            {
                CargoTransportDebugVisualization.Draw(
                    draw,
                    debug.CargoTransport,
                    maximumTransports: 128,
                    maximumLabels: 0);
            }

            if (debug.Distribution is not null)
            {
                AutomatedDistributionDebugVisualization.Draw(
                    draw,
                    debug.Distribution,
                    maximumRequests: 128,
                    maximumLabels: 0);
            }

            if (debug.LogisticsCapacity is not null)
            {
                LogisticsCapacityDebugVisualization.Draw(
                    draw,
                    debug.LogisticsCapacity,
                    maximumNodes: 128,
                    maximumEdges: 256,
                    maximumLabels: 0);
            }
        }

        if (all ||
            mode ==
            StrategicOverlayMode.Supply)
        {
            if (debug.BattlefieldSupply is not null)
            {
                BattlefieldSupplyDebugVisualization.Draw(
                    draw,
                    debug.BattlefieldSupply,
                    maximumProviders: 96,
                    maximumUnits: 192,
                    maximumLabels: 0);
            }
        }

        if (all ||
            mode ==
            StrategicOverlayMode.Sensors)
        {
            IntelligenceDebugVisualization.DrawSensors(
                draw,
                debug.IntelligenceSensors,
                maximumSensors: 96);

            if (intelligence is not null)
            {
                IntelligenceDebugVisualization.Draw(
                    draw,
                    intelligence,
                    maximumCells: 768,
                    maximumContacts: 128);
            }
        }

        if (all ||
            mode ==
            StrategicOverlayMode.Navigation)
        {
            NavigationDebugVisualization.Draw(
                draw,
                debug.NavigationWorld,
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked),
                debug.NavigationPath,
                cameraTarget);
        }
    }
}
