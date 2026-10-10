using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct StrategicLogisticsNodeReadModel(
    EntityId Entity,
    Vector3 WorldPosition,
    LogisticsNodeKind Kind,
    LogisticsNodeCapabilities Capabilities,
    bool Enabled);

public readonly record struct StrategicLogisticsLinkReadModel(
    Vector3 SourcePosition,
    Vector3 DestinationPosition,
    LogisticsTransportMode Mode,
    double CapacityPerSecond,
    bool Enabled);

public readonly record struct StrategicSupplyReadModel(
    EntityId Entity,
    Vector3 WorldPosition,
    bool IsProvider,
    bool IsDepot,
    bool IsTruck,
    float ResupplyRangeMeters,
    bool ProviderEnabled,
    bool HasUnitState,
    BattlefieldSupplyStatus Status,
    double FuelFraction,
    double AmmunitionFraction);

public readonly record struct StrategicSensorReadModel(
    EntityId Entity,
    Vector3 WorldPosition,
    float VisualRangeMeters,
    float RadarRangeMeters,
    float IdentificationRangeMeters)
{
    public bool HasVisual =>
        VisualRangeMeters >
        0.0f;

    public bool HasRadar =>
        RadarRangeMeters >
        0.0f;
}

public readonly record struct StrategicNavigationSectorReadModel(
    NavigationSectorCoordinate Sector,
    AxisAlignedBounds WorldBounds,
    int ConnectionCount);

public readonly record struct StrategicNavigationPortalReadModel(
    Vector3 WorldPosition);

public readonly record struct StrategicPowerEntityReadModel(
    EntityId Entity,
    Vector3 WorldPosition,
    PowerNetworkId NetworkId,
    bool IsGenerator,
    double MaximumGeneration,
    bool GeneratorEnabled,
    PowerGeneratorState GeneratorState,
    bool IsConsumer,
    double Demand,
    double AllocatedPower,
    PowerPriority Priority,
    bool ConsumerEnabled,
    PowerOperationalState ConsumerState);

public readonly record struct StrategicPowerNetworkReadModel(
    PowerNetworkId NetworkId,
    double Generation,
    double Demand,
    double AllocatedPower,
    double Deficit,
    int GeneratorCount,
    int ActiveGeneratorCount,
    int ConsumerCount,
    int PoweredConsumerCount,
    int BrownoutConsumerCount,
    int OfflineConsumerCount)
{
    public bool IsConstrained =>
        Deficit >
            0.0 ||
        BrownoutConsumerCount >
            0 ||
        OfflineConsumerCount >
            0;
}

public sealed class StrategicOverlaySnapshot
{
    private readonly IReadOnlyList<StrategicLogisticsNodeReadModel> _logisticsNodes;
    private readonly IReadOnlyList<StrategicLogisticsLinkReadModel> _logisticsLinks;
    private readonly IReadOnlyList<StrategicSupplyReadModel> _supply;
    private readonly IReadOnlyList<StrategicSensorReadModel> _sensors;
    private readonly IReadOnlyList<StrategicNavigationSectorReadModel> _navigationSectors;
    private readonly IReadOnlyList<StrategicNavigationPortalReadModel> _navigationPortals;
    private readonly IReadOnlyList<StrategicPowerEntityReadModel> _powerEntities;
    private readonly IReadOnlyList<StrategicPowerNetworkReadModel> _powerNetworks;

    public StrategicOverlaySnapshot(
        SimulationTick tick,
        StrategicOverlayMode requestedMode,
        IReadOnlyList<StrategicLogisticsNodeReadModel> logisticsNodes,
        IReadOnlyList<StrategicLogisticsLinkReadModel> logisticsLinks,
        IReadOnlyList<StrategicSupplyReadModel> supply,
        IReadOnlyList<StrategicSensorReadModel> sensors,
        IReadOnlyList<StrategicNavigationSectorReadModel> navigationSectors,
        IReadOnlyList<StrategicNavigationPortalReadModel> navigationPortals,
        IReadOnlyList<StrategicPowerEntityReadModel> powerEntities,
        IReadOnlyList<StrategicPowerNetworkReadModel> powerNetworks,
        SimulationSessionId session = default, PlayerId player = default)
    {
        Tick = tick;
        Session = session; Player = player;
        RequestedMode = requestedMode;
        _logisticsNodes =
            Array.AsReadOnly(logisticsNodes?.ToArray() ??
            throw new ArgumentNullException(
                nameof(logisticsNodes)));
        _logisticsLinks =
            Array.AsReadOnly(logisticsLinks?.ToArray() ??
            throw new ArgumentNullException(
                nameof(logisticsLinks)));
        _supply =
            Array.AsReadOnly(supply?.ToArray() ??
            throw new ArgumentNullException(
                nameof(supply)));
        _sensors =
            Array.AsReadOnly(sensors?.ToArray() ??
            throw new ArgumentNullException(
                nameof(sensors)));
        _navigationSectors =
            Array.AsReadOnly(navigationSectors?.ToArray() ??
            throw new ArgumentNullException(
                nameof(navigationSectors)));
        _navigationPortals =
            Array.AsReadOnly(navigationPortals?.ToArray() ??
            throw new ArgumentNullException(
                nameof(navigationPortals)));
        _powerEntities =
            Array.AsReadOnly(powerEntities?.ToArray() ??
            throw new ArgumentNullException(
                nameof(powerEntities)));
        _powerNetworks =
            Array.AsReadOnly(powerNetworks?.ToArray() ??
            throw new ArgumentNullException(
                nameof(powerNetworks)));
    }

    public SimulationTick Tick { get; }
    public SimulationSessionId Session { get; }
    public PlayerId Player { get; }

    public StrategicOverlayMode RequestedMode { get; }

    public IReadOnlyList<StrategicLogisticsNodeReadModel> LogisticsNodes =>
        _logisticsNodes;

    public IReadOnlyList<StrategicLogisticsLinkReadModel> LogisticsLinks =>
        _logisticsLinks;

    public IReadOnlyList<StrategicSupplyReadModel> Supply =>
        _supply;

    public IReadOnlyList<StrategicSensorReadModel> Sensors =>
        _sensors;

    public IReadOnlyList<StrategicNavigationSectorReadModel> NavigationSectors =>
        _navigationSectors;

    public IReadOnlyList<StrategicNavigationPortalReadModel> NavigationPortals =>
        _navigationPortals;

    public IReadOnlyList<StrategicPowerEntityReadModel> PowerEntities =>
        _powerEntities;

    public IReadOnlyList<StrategicPowerNetworkReadModel> PowerNetworks =>
        _powerNetworks;
}

internal static class StrategicOverlaySnapshotFactory
{
    public static StrategicOverlaySnapshot Capture(
        SimulationContext context,
        PresentationExtractionContext extraction,
        StrategicOverlayMode mode,
        FactionIntelligenceSnapshot? intelligence)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(extraction);

        bool all =
            mode ==
            StrategicOverlayMode.All;

        StrategicLogisticsNodeReadModel[] logisticsNodes =
            all ||
            mode ==
                StrategicOverlayMode.Logistics
                ? CaptureLogisticsNodes(
                    context.Entities,
                    extraction)
                : [];
        StrategicLogisticsLinkReadModel[] logisticsLinks =
            all ||
            mode ==
                StrategicOverlayMode.Logistics
                ? CaptureLogisticsLinks(
                    extraction,
                    logisticsNodes)
                : [];
        StrategicSupplyReadModel[] supply =
            all ||
            mode ==
                StrategicOverlayMode.Supply
                ? CaptureSupply(
                    context.Entities,
                    extraction.Player)
                : [];
        StrategicSensorReadModel[] sensors =
            all ||
            mode ==
                StrategicOverlayMode.Sensors
                ? CaptureSensors(
                    context.Entities,
                    new FactionId(
                        checked(
                            (uint)extraction.Player.Value)))
                : [];
        StrategicNavigationSectorReadModel[] navigationSectors =
            all ||
            mode ==
                StrategicOverlayMode.Navigation
                ? CaptureNavigationSectors(
                    extraction.Scenario.Services.Navigation.World,
                    intelligence)
                : [];
        StrategicNavigationPortalReadModel[] navigationPortals =
            all ||
            mode ==
                StrategicOverlayMode.Navigation
                ? CaptureNavigationPortals(
                    extraction.Scenario.Services.Navigation.World,
                    intelligence)
                : [];
        CapturePower(
            context.Entities,
            extraction,
            all ||
            mode ==
                StrategicOverlayMode.Power,
            out StrategicPowerEntityReadModel[] powerEntities,
            out StrategicPowerNetworkReadModel[] powerNetworks);

        return new StrategicOverlaySnapshot(
            context.Tick,
            mode,
            logisticsNodes,
            logisticsLinks,
            supply,
            sensors,
            navigationSectors,
            navigationPortals,
            powerEntities,
            powerNetworks, extraction.Scenario.Simulation.SessionId, extraction.Player);
    }

    private static StrategicLogisticsNodeReadModel[] CaptureLogisticsNodes(
        EntityRegistry entities,
        PresentationExtractionContext extraction)
    {
        IReadOnlyList<LogisticsNode> nodes =
            extraction.Scenario.Logistics.GetNodes();
        var result =
            new List<StrategicLogisticsNodeReadModel>(
                nodes.Count);

        for (int index = 0;
             index < nodes.Count;
             index++)
        {
            LogisticsNode node =
                nodes[index];

            if (!IsOwned(
                    entities,
                    node.Entity,
                    extraction.Player))
            {
                continue;
            }

            result.Add(
                new StrategicLogisticsNodeReadModel(
                    node.Entity,
                    node.WorldPosition,
                    node.Kind,
                    node.Capabilities,
                    node.Enabled));
        }

        return result.ToArray();
    }

    private static StrategicLogisticsLinkReadModel[] CaptureLogisticsLinks(
        PresentationExtractionContext extraction,
        StrategicLogisticsNodeReadModel[] localNodes)
    {
        if (localNodes.Length == 0)
        {
            return [];
        }

        var positions =
            new Dictionary<LogisticsNodeId, Vector3>(
                localNodes.Length);
        IReadOnlyList<LogisticsNode> nodes =
            extraction.Scenario.Logistics.GetNodes();

        for (int index = 0;
             index < nodes.Count;
             index++)
        {
            LogisticsNode node =
                nodes[index];

            for (int localIndex = 0;
                 localIndex <
                     localNodes.Length;
                 localIndex++)
            {
                if (localNodes[
                        localIndex].Entity ==
                    node.Entity)
                {
                    positions[node.Id] =
                        node.WorldPosition;
                    break;
                }
            }
        }

        IReadOnlyList<LogisticsEdge> edges =
            extraction.Scenario.Logistics.GetEdges();
        var result =
            new List<StrategicLogisticsLinkReadModel>(
                edges.Count);

        for (int index = 0;
             index < edges.Count;
             index++)
        {
            LogisticsEdge edge =
                edges[index];

            if (edge.Mode !=
                    LogisticsTransportMode.GroundRoad ||
                !positions.TryGetValue(
                    edge.Source,
                    out Vector3 source) ||
                !positions.TryGetValue(
                    edge.Destination,
                    out Vector3 destination))
            {
                continue;
            }

            result.Add(
                new StrategicLogisticsLinkReadModel(
                    source,
                    destination,
                    edge.Mode,
                    edge.CapacityPerSecond,
                    edge.Enabled));
        }

        return result.ToArray();
    }

    private static StrategicSupplyReadModel[] CaptureSupply(
        EntityRegistry entities,
        PlayerId player)
    {
        var result =
            new List<StrategicSupplyReadModel>(
                entities.GetComponentCount<WorldTransform>());

        foreach (EntityId entity in
                 entities.Query<WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            WorldTransform transform =
                entities.GetComponent<WorldTransform>(
                    entity);
            bool providerOwned = false;
            bool isProvider = false;
            bool isDepot = false;
            bool isTruck = false;
            bool providerEnabled = false;
            float range = 0.0f;

            if (entities.TryGetComponent(
                    entity,
                    out SupplyProvider provider) &&
                provider.Owner ==
                    player)
            {
                providerOwned = true;
                isProvider = true;
                providerEnabled =
                    provider.Enabled;
                range =
                    provider.ResupplyRangeMeters;
            }

            if (entities.TryGetComponent(
                    entity,
                    out SupplyDepot depot) &&
                depot.Owner ==
                    player)
            {
                providerOwned = true;
                isProvider = true;
                isDepot = true;
                providerEnabled =
                    depot.State ==
                    SupplyDepotState.Operational;
            }

            if (entities.TryGetComponent(
                    entity,
                    out SupplyTruck truck) &&
                truck.Owner ==
                    player)
            {
                providerOwned = true;
                isProvider = true;
                isTruck = true;
                providerEnabled = true;
                range =
                    MathF.Max(
                        range,
                        truck.ResupplyRangeMeters);
            }

            UnitSupplyState unitSupply =
                default;
            bool hasUnitState =
                IsOwned(
                    entities,
                    entity,
                    player) &&
                entities.TryGetComponent(
                    entity,
                    out unitSupply);

            if (!providerOwned &&
                !hasUnitState)
            {
                continue;
            }

            result.Add(
                new StrategicSupplyReadModel(
                    entity,
                    transform.Position,
                    isProvider,
                    isDepot,
                    isTruck,
                    range,
                    providerEnabled,
                    hasUnitState,
                    hasUnitState
                        ? unitSupply.Status
                        : BattlefieldSupplyStatus.Supplied,
                    hasUnitState
                        ? unitSupply.FuelFraction
                        : 1.0,
                    hasUnitState
                        ? unitSupply.AmmunitionFraction
                        : 1.0));
        }

        return result.ToArray();
    }

    private static StrategicSensorReadModel[] CaptureSensors(
        EntityRegistry entities,
        FactionId faction)
    {
        var result =
            new List<StrategicSensorReadModel>(
                entities.GetComponentCount<WorldTransform>());

        foreach (EntityId entity in
                 entities.Query<WorldTransform>(
                     QueryIterationOrder.StableByEntityIndex))
        {
            float visualRange = 0.0f;
            float radarRange = 0.0f;
            float identificationRange = 0.0f;

            if (entities.TryGetComponent(
                    entity,
                    out VisualSensorState visual) &&
                visual.Faction ==
                    faction)
            {
                visualRange =
                    visual.RangeMeters;
            }

            if (entities.TryGetComponent(
                    entity,
                    out RadarSensorState radar) &&
                radar.Faction ==
                    faction)
            {
                radarRange =
                    radar.DetectionRangeMeters;
                identificationRange =
                    radar.IdentificationRangeMeters;
            }

            if (visualRange <= 0.0f &&
                radarRange <= 0.0f)
            {
                continue;
            }

            result.Add(
                new StrategicSensorReadModel(
                    entity,
                    entities.GetComponent<WorldTransform>(
                        entity).Position,
                    visualRange,
                    radarRange,
                    identificationRange));
        }

        return result.ToArray();
    }

    private static StrategicNavigationSectorReadModel[] CaptureNavigationSectors(
        NavigationWorld world,
        FactionIntelligenceSnapshot? intelligence)
    {
        NavigationSectorGraph graph =
            world.GetSectorGraph(
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked));
        var result =
            new List<StrategicNavigationSectorReadModel>(
                graph.SectorCount);

        for (int z = 0;
             z < graph.Height;
             z++)
        {
            for (int x = 0;
                 x < graph.Width;
                 x++)
            {
                var sector =
                    new NavigationSectorCoordinate(
                        x,
                        z);

                if (!graph.Contains(
                        sector))
                {
                    continue;
                }

                AxisAlignedBounds bounds =
                    graph.GetWorldBounds(
                        sector);
                Vector3 center =
                    (bounds.Minimum +
                     bounds.Maximum) *
                    0.5f;

                if (!IsVisible(
                        intelligence,
                        center))
                {
                    continue;
                }

                result.Add(
                    new StrategicNavigationSectorReadModel(
                        sector,
                        bounds,
                        graph.GetEdges(
                                sector)
                            .Length));
            }
        }

        return result.ToArray();
    }

    private static StrategicNavigationPortalReadModel[] CaptureNavigationPortals(
        NavigationWorld world,
        FactionIntelligenceSnapshot? intelligence)
    {
        NavigationSectorGraph graph =
            world.GetSectorGraph(
                NavigationCapabilities.For(
                    NavigationMovementClass.Tracked));
        var result =
            new List<StrategicNavigationPortalReadModel>(
                graph.Portals.Count);

        for (int index = 0;
             index < graph.Portals.Count;
             index++)
        {
            NavigationPortal portal =
                graph.Portals[index];

            if (!IsVisible(
                    intelligence,
                    portal.WorldPosition))
            {
                continue;
            }

            result.Add(
                new StrategicNavigationPortalReadModel(
                    portal.WorldPosition));
        }

        return result.ToArray();
    }

    private static void CapturePower(
        EntityRegistry entities,
        PresentationExtractionContext extraction,
        bool requested,
        out StrategicPowerEntityReadModel[] powerEntities,
        out StrategicPowerNetworkReadModel[] powerNetworks)
    {
        if (!requested)
        {
            powerEntities = [];
            powerNetworks = [];
            return;
        }

        var entitiesResult =
            new List<StrategicPowerEntityReadModel>();
        var localNetworks =
            new HashSet<PowerNetworkId>();

        foreach (EntityId entity in
                 entities.Query<
                     PowerNetworkMembership,
                     WorldTransform>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            if (!IsOwned(
                    entities,
                    entity,
                    extraction.Player))
            {
                continue;
            }

            PowerNetworkMembership membership =
                entities.GetComponent<PowerNetworkMembership>(
                    entity);
            bool hasGenerator =
                entities.TryGetComponent(
                    entity,
                    out PowerGenerator generator);
            bool hasConsumer =
                entities.TryGetComponent(
                    entity,
                    out PowerConsumer consumer);

            if (!hasGenerator &&
                !hasConsumer)
            {
                continue;
            }

            localNetworks.Add(
                membership.NetworkId);
            entitiesResult.Add(
                new StrategicPowerEntityReadModel(
                    entity,
                    entities.GetComponent<WorldTransform>(
                        entity).Position,
                    membership.NetworkId,
                    hasGenerator,
                    hasGenerator
                        ? generator.MaximumGeneration
                        : 0.0,
                    hasGenerator &&
                    generator.Enabled,
                    hasGenerator
                        ? generator.State
                        : PowerGeneratorState.Offline,
                    hasConsumer,
                    hasConsumer
                        ? consumer.Demand
                        : 0.0,
                    hasConsumer
                        ? consumer.AllocatedPower
                        : 0.0,
                    hasConsumer
                        ? consumer.Priority
                        : PowerPriority.Industrial,
                    hasConsumer &&
                    consumer.Enabled,
                    hasConsumer
                        ? consumer.State
                        : PowerOperationalState.Offline));
        }

        IReadOnlyList<PowerNetworkReadModel> networks =
            extraction.Scenario.Power.Networks;
        var networksResult =
            new List<StrategicPowerNetworkReadModel>(
                localNetworks.Count);

        for (int index = 0;
             index < networks.Count;
             index++)
        {
            PowerNetworkReadModel network =
                networks[index];

            if (!localNetworks.Contains(
                    network.NetworkId))
            {
                continue;
            }

            networksResult.Add(
                new StrategicPowerNetworkReadModel(
                    network.NetworkId,
                    network.Generation,
                    network.Demand,
                    network.AllocatedPower,
                    network.Deficit,
                    network.GeneratorCount,
                    network.ActiveGeneratorCount,
                    network.ConsumerCount,
                    network.PoweredConsumerCount,
                    network.BrownoutConsumerCount,
                    network.OfflineConsumerCount));
        }

        powerEntities =
            entitiesResult.ToArray();
        powerNetworks =
            networksResult.ToArray();
    }

    private static bool IsOwned(
        EntityRegistry entities,
        EntityId entity,
        PlayerId player) =>
        entities.TryGetComponent(
            entity,
            out ControllableEntity controllable) &&
        controllable.Owner ==
            player;

    private static bool IsVisible(
        FactionIntelligenceSnapshot? intelligence,
        Vector3 worldPosition)
    {
        if (intelligence is null)
        {
            return true;
        }

        float cellSize =
            intelligence.CellSizeMeters;

        if (!float.IsFinite(
                cellSize) ||
            cellSize <= 0.0f)
        {
            return false;
        }

        var coordinate =
            new VisibilityCellCoordinate(
                (int)MathF.Floor(
                    worldPosition.X /
                    cellSize),
                (int)MathF.Floor(
                    worldPosition.Z /
                    cellSize));

        for (int index = 0;
             index < intelligence.Cells.Count;
             index++)
        {
            VisibilityCellSnapshot cell =
                intelligence.Cells[index];

            if (cell.Cell ==
                coordinate)
            {
                return cell.State ==
                    IntelligenceState.Visible;
            }
        }

        return false;
    }
}
