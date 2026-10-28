using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed record StrategicMapAuditRules
{
    public float LocalOpeningDistanceMeters { get; init; } = 450;
    public double AsymmetryWarningRatio { get; init; } = 1.25;
    public int MaximumRouteQueries { get; init; } = 20_000;
    public IReadOnlyList<ResourceId> VitalResources { get; init; } =
        Array.AsReadOnly(new[] { ResourceIds.FerrousOre, ResourceIds.Volatiles, ResourceIds.Silicates });

    internal void Validate()
    {
        if (!float.IsFinite(LocalOpeningDistanceMeters) || LocalOpeningDistanceMeters <= 0 ||
            !double.IsFinite(AsymmetryWarningRatio) || AsymmetryWarningRatio <= 1 ||
            MaximumRouteQueries is < 1 or > 1_000_000 ||
            VitalResources is null || VitalResources.Count is < 1 or > 32 ||
            VitalResources.Any(r => !r.IsSpecified) || VitalResources.Distinct().Count() != VitalResources.Count)
            throw new ArgumentException("Invalid strategic audit rules.");
    }
}

public sealed record StrategicMapDiagnostic(
    string Id, string Severity, string Code, string State, string Movement, ulong Player,
    string Target, float X, float Z, string Message, string? AcceptedReason = null);

public sealed record StrategicMapRoute(
    string State, string Movement, ulong Player, string TargetKind, string Target,
    float X, float Z, float? AccessibleDistanceMeters, string Failure);

public sealed record StrategicMapOpening(
    string Movement, ulong Player, uint Resource, string? Deposit,
    float? AccessibleDistanceMeters, double Quantity, double ExtractionPerSecond);

public sealed record StrategicMapExpansionChoice(string Movement, ulong Player, uint Resource,
    int ReachableDepositCount, string? NearestDeposit, float? AccessibleDistanceMeters);

public sealed record StrategicMapBuildArea(string Movement, ulong Player, double ReachablePermittedSquareMeters);
public sealed record StrategicMapRoadCapacity(string State, ulong FromPlayer, ulong ToPlayer,
    string? FromNode, string? ToNode, double WidestPathCapacityPerSecond);

public sealed record StrategicMapAuditReport(
    int SchemaVersion, string MapKey, int MapRevision, StrategicMapAuditRules Rules,
    float NavigationCellMeters, int SectorSizeCells, int RouteQueryCount,
    IReadOnlyList<StrategicMapRoute> Routes, IReadOnlyList<StrategicMapOpening> Openings,
    IReadOnlyList<StrategicMapBuildArea> BuildAreas, IReadOnlyList<StrategicMapRoadCapacity> RoadCapacities,
    IReadOnlyList<StrategicMapExpansionChoice> ExpansionChoices,
    IReadOnlyList<StrategicMapDiagnostic> Diagnostics)
{
    public int ErrorCount => Diagnostics.Count(d => d.Severity == "Error");
    public int WarningCount => Diagnostics.Count(d => d.Severity == "Warning" && d.AcceptedReason is null);
    public int AcceptedWarningCount => Diagnostics.Count(d => d.Severity == "Warning" && d.AcceptedReason is not null);
    public string Verdict => ErrorCount > 0 ? "Invalid" : WarningCount > 0 ? "ReviewRequired" : "NoThresholdViolation";
}

// Offline only. No runtime/session, visibility read model, random stream or navigation mutation.
public static class StrategicMapAudit
{
    private static readonly NavigationMovementClass[] Movements =
        [NavigationMovementClass.Infantry, NavigationMovementClass.Wheeled, NavigationMovementClass.Tracked];
    private static readonly (int X, int Z)[] Neighbors = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    public static StrategicMapAuditReport Analyze(
        BattlefieldDefinition map, TerrainWorld terrain, StrategicMapAuditRules? rules = null,
        IReadOnlyDictionary<string, string>? acceptedWarnings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(terrain);
        cancellationToken.ThrowIfCancellationRequested();
        BattlefieldValidator.ValidateDefinition(map);
        rules ??= new();
        rules.Validate();
        rules = rules with { VitalResources = Array.AsReadOnly(rules.VitalResources.ToArray()) };
        if (map.Starts.Count > 16 || map.Resources.Count > 4096 || map.Sites.Count > 256 ||
            map.Crossings.Count > 128 || map.RoadNodes.Count > 2048 || map.RoadEdges.Count > 8192 ||
            map.Metadata.WidthMeters > 8192 || map.Metadata.HeightMeters > 8192)
            throw new ArgumentException("Map exceeds offline audit bounds.", nameof(map));

        if (map.Resources.Any(r => !float.IsFinite(r.HalfExtents.X) || !float.IsFinite(r.HalfExtents.Z) ||
            r.HalfExtents.X < 0 || r.HalfExtents.Z < 0 || r.HalfExtents.X > 8192 || r.HalfExtents.Z > 8192) ||
            terrain.WorldBounds.Minimum.X != 0 || terrain.WorldBounds.Minimum.Z != 0 ||
            terrain.WorldBounds.Maximum.X != map.Metadata.WidthMeters || terrain.WorldBounds.Maximum.Z != map.Metadata.HeightMeters)
            throw new ArgumentException("Map extents or terrain do not match the bounded audit geometry.", nameof(map));

        var routes = new List<StrategicMapRoute>();
        var openings = new List<StrategicMapOpening>();
        var areas = new List<StrategicMapBuildArea>();
        var capacities = new List<StrategicMapRoadCapacity>();
        var expansionChoices = new List<StrategicMapExpansionChoice>();
        var diagnostics = new List<StrategicMapDiagnostic>();
        var starts = map.Starts.OrderBy(s => s.Player.Value).ToArray();
        var resources = map.Resources.OrderBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var gridSettings = new NavigationGridSettings { CellSizeMeters = 16, StaticObstacleClearanceMeters = .5f };
        var sectors = new NavigationSectorSettings { SectorSizeCells = 8 };
        int queryCount = 0;

        void Diagnose(string severity, string code, string state, string movement, ulong player,
            string target, Vector3 point, string message) =>
            diagnostics.Add(new($"{code}|{state}|{movement}|{player}|{target}", severity, code,
                state, movement, player, target, point.X, point.Z, message));

        var states = new List<(string Key, HashSet<string> Closed)> { ("Baseline", new(StringComparer.Ordinal)) };
        foreach (var crossing in map.Crossings.OrderBy(c => c.Key, StringComparer.Ordinal))
            states.Add(($"Closed:{crossing.Key}", new(StringComparer.Ordinal) { crossing.Key }));
        if (map.Crossings.Count > 1)
            states.Add(("AllCrossingsClosed", map.Crossings.Select(c => c.Key).ToHashSet(StringComparer.Ordinal)));

        foreach (var state in states)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var availability = map.Crossings.ToDictionary(c => c.Key,
                c => c.InitiallyOperational && !state.Closed.Contains(c.Key), StringComparer.Ordinal);
            var world = NavigationWorld.Build(terrain, map.CreateNavigationObstacles(availability), gridSettings, sectors);
            var finder = new HierarchicalPathfinder(world, maximumFallbackExpandedNodes: world.Grid.CellCount);
            var grid = world.Grid;
            foreach (var movement in Movements)
            {
                var capabilities = NavigationCapabilities.For(movement);
                string movementName = movement.ToString();
                foreach (var start in starts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool[] reachable = Flood(grid, start.Position, capabilities, cancellationToken);
                    bool spawnOpen = grid.TryWorldToCell(start.Position, out var spawn) && grid.IsTraversable(spawn, capabilities);
                    if (state.Key == "Baseline" && !spawnOpen)
                        Diagnose("Error", "BlockedSpawn", state.Key, movementName, start.Player.Value,
                            "spawn", start.Position, "Authored spawn is outside or blocked; endpoint projection is disabled.");

                    StrategicMapRoute Route(string kind, string key, Vector3 destination, bool admitted = true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++queryCount > rules.MaximumRouteQueries)
                            throw new InvalidOperationException("Strategic audit route budget exhausted; no complete verdict is available.");
                        NavigationSearchResult result = admitted
                            ? finder.FindPath(start.Position, destination, capabilities, projectBlockedEndpoints: false)
                            : new(world.Version, NavigationFailureReason.DestinationBlocked, null);
                        var route = new StrategicMapRoute(state.Key, movementName, start.Player.Value, kind, key,
                            destination.X, destination.Z, result.Succeeded ? result.Path!.Diagnostics.RouteLengthMeters : null,
                            result.FailureReason.ToString());
                        routes.Add(route);
                        if (result.FailureReason == NavigationFailureReason.SearchBudgetExhausted)
                            Diagnose("Error", "IncompleteSearch", state.Key, movementName, start.Player.Value,
                                key, destination, "Navigation search exhausted its budget; reachability is unknown.");
                        return route;
                    }

                    foreach (var opponent in starts.Where(s => s.Player != start.Player))
                    {
                        var route = Route("Start", $"start:{opponent.Player.Value}", opponent.Position);
                        if (route.AccessibleDistanceMeters is null)
                            Diagnose(state.Key == "Baseline" ? "Error" : "Warning", "DisconnectedStarts", state.Key,
                                movementName, start.Player.Value, route.Target, opponent.Position,
                                "No measured route between starts in this crossing state.");
                    }
                    foreach (var site in map.Sites.OrderBy(s => s.Key, StringComparer.Ordinal))
                    {
                        var route = Route("Site", site.Key, site.Position);
                        if (state.Key == "Baseline" && route.AccessibleDistanceMeters is null)
                            Diagnose("Warning", "UnreachableSite", state.Key, movementName, start.Player.Value,
                                site.Key, site.Position, "Expansion/staging site has no route in this state.");
                    }
                    var resourceRoutes = new Dictionary<string, StrategicMapRoute>(StringComparer.Ordinal);
                    foreach (var resource in resources)
                    {
                        // Four-neighbor flood has the same components as no-corner-cut eight-neighbor navigation.
                        bool approachFound = TryApproach(grid, reachable, resource, out Vector3 approach);
                        var route = Route("Resource", resource.Key, approachFound ? approach : resource.Center, approachFound);
                        resourceRoutes.Add(resource.Key, route);
                        if (state.Key == "Baseline" && route.AccessibleDistanceMeters is null)
                            Diagnose("Warning", "UnreachableResource", state.Key, movementName, start.Player.Value,
                                resource.Key, resource.Center, "No measured route to a reachable resource approach in this crossing state.");
                    }
                    if (state.Key != "Baseline") continue;

                    foreach (var resourceId in rules.VitalResources.OrderBy(r => r.Value))
                    {
                        var candidates = resources.Where(r => r.ResourceId == resourceId && !r.Contested)
                            .Where(r => resourceRoutes[r.Key].AccessibleDistanceMeters is not null)
                            .OrderBy(r => resourceRoutes[r.Key].AccessibleDistanceMeters)
                            .ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
                        var local = candidates.FirstOrDefault();
                        bool exists = candidates.Length > 0;
                        float? distance = exists ? resourceRoutes[local.Key].AccessibleDistanceMeters : null;
                        openings.Add(new(movementName, start.Player.Value, resourceId.Value, exists ? local.Key : null,
                            distance, exists ? local.TotalQuantity : 0, exists ? local.ExtractionRatePerSecond : 0));
                        if (distance is null || distance > rules.LocalOpeningDistanceMeters)
                            Diagnose("Error", "MissingVitalOpening", state.Key, movementName, start.Player.Value,
                                $"resource:{resourceId.Value}", start.Position,
                                "No reachable non-contested vital reserve within the configured route-distance limit.");
                    }
                    foreach (var resourceId in resources.Where(r => r.Contested).Select(r => r.ResourceId).Distinct().OrderBy(r => r.Value))
                    {
                        var choices = resources.Where(r => r.Contested && r.ResourceId == resourceId &&
                            resourceRoutes[r.Key].AccessibleDistanceMeters is not null)
                            .OrderBy(r => resourceRoutes[r.Key].AccessibleDistanceMeters).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
                        expansionChoices.Add(new(movementName, start.Player.Value, resourceId.Value, choices.Length,
                            choices.Length > 0 ? choices[0].Key : null, choices.Length > 0 ? resourceRoutes[choices[0].Key].AccessibleDistanceMeters : null));
                        if (choices.Length == 0)
                            Diagnose("Warning", "NoExpansionChoice", state.Key, movementName, start.Player.Value,
                                $"resource:{resourceId.Value}", start.Position, "No contested deposit of this resource is reachable.");
                    }
                    var query = new BattlefieldBuildableAreaQuery(map);
                    int permitted = 0;
                    for (int i = 0; i < grid.CellCount; i++)
                    {
                        if (!reachable[i]) continue;
                        var point = grid.GetCellCenter(grid.GetCoordinate(i));
                        if (Contains(start.BuildArea, point) &&
                            query.IsBuildable(start.Player, new(point - new Vector3(1, 0, 1), point + new Vector3(1, 0, 1))))
                            permitted++;
                    }
                    areas.Add(new(movementName, start.Player.Value, permitted * 256.0));
                    if (permitted == 0)
                        Diagnose("Error", "NoOpeningBuildArea", state.Key, movementName, start.Player.Value,
                            "yard", start.Position, "No reachable permitted 2 m footprint samples in the start yard.");
                }
            }
            foreach (var start in starts)
            foreach (var opponent in starts.Where(s => s.Player != start.Player))
            {
                string? source = NearestRoadNode(map, start.Position);
                string? destination = NearestRoadNode(map, opponent.Position);
                double capacity = RoadCapacity(map, source, destination, availability);
                capacities.Add(new(state.Key, start.Player.Value, opponent.Player.Value, source, destination, capacity));
                if (capacity == 0)
                    Diagnose("Warning", "RoadCut", state.Key, "Road", start.Player.Value,
                        $"start:{opponent.Player.Value}", opponent.Position, "No positive-capacity road path between the nearest authored start nodes.");
            }
        }

        foreach (var movement in Movements)
        {
            string name = movement.ToString();
            foreach (var resourceId in rules.VitalResources.OrderBy(r => r.Value))
            {
                var values = openings.Where(o => o.Movement == name && o.Resource == resourceId.Value).ToArray();
                Compare("OpeningDistance", name, $"resource:{resourceId.Value}", values.Select(o => (double?)o.AccessibleDistanceMeters));
                Compare("OpeningQuantity", name, $"resource:{resourceId.Value}", values.Select(o => (double?)o.Quantity));
                Compare("OpeningExtraction", name, $"resource:{resourceId.Value}", values.Select(o => (double?)o.ExtractionPerSecond));
            }
            Compare("OpeningBuildArea", name, "yards", areas.Where(a => a.Movement == name).Select(a => (double?)a.ReachablePermittedSquareMeters));
            foreach (var resourceId in expansionChoices.Select(c => c.Resource).Distinct().Order())
            {
                var choices = expansionChoices.Where(c => c.Movement == name && c.Resource == resourceId).ToArray();
                Compare("ExpansionDistance", name, $"resource:{resourceId}", choices.Select(c => (double?)c.AccessibleDistanceMeters));
                Compare("ExpansionChoiceCount", name, $"resource:{resourceId}", choices.Select(c => (double?)c.ReachableDepositCount));
            }
            foreach (var kind in map.Sites.Select(s => s.Kind).Distinct().Order())
            {
                var keys = map.Sites.Where(s => s.Kind == kind).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
                var nearest = starts.Select(s => routes.Where(r => r.State == "Baseline" && r.Movement == name &&
                    r.Player == s.Player.Value && r.TargetKind == "Site" && keys.Contains(r.Target) &&
                    r.AccessibleDistanceMeters is not null).Select(r => (double?)r.AccessibleDistanceMeters).Min());
                Compare("SiteChoiceDistance", name, kind.ToString(), nearest);
            }
        }

        void Compare(string code, string movement, string target, IEnumerable<double?> observations)
        {
            double?[] values = observations.ToArray();
            if (values.Any(v => v is null)) return;
            double minimum = values.Min() ?? 0;
            double maximum = values.Max() ?? 0;
            if (maximum <= 0 || maximum <= minimum * rules.AsymmetryWarningRatio) return;
            Diagnose("Warning", code, "Baseline", movement, 0, target, Vector3.Zero,
                FormattableString.Invariant($"Across starts: minimum={minimum:0.###}, maximum={maximum:0.###}; exceeds ratio {rules.AsymmetryWarningRatio:0.###}. Review deliberate asymmetry; this is not a fairness certificate."));
        }

        if (acceptedWarnings is not null)
        {
            if (acceptedWarnings.Count > 4096) throw new ArgumentException("Too many warning exceptions.", nameof(acceptedWarnings));
            foreach (var exception in acceptedWarnings.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                int index = diagnostics.FindIndex(d => d.Id == exception.Key && d.Severity == "Warning");
                if (index < 0 || string.IsNullOrWhiteSpace(exception.Value) || exception.Value.Length > 1024)
                    throw new ArgumentException("Warning exception must match a current warning and include a bounded rationale.", nameof(acceptedWarnings));
                diagnostics[index] = diagnostics[index] with { AcceptedReason = exception.Value };
            }
        }
        return new(1, map.Metadata.Key, map.MapRevision, rules, 16, 8, queryCount,
            routes.AsReadOnly(), openings.AsReadOnly(), areas.AsReadOnly(), capacities.AsReadOnly(), expansionChoices.AsReadOnly(), diagnostics.AsReadOnly());
    }

    private static bool Contains(AxisAlignedBounds area, Vector3 point) =>
        point.X >= area.Minimum.X && point.X <= area.Maximum.X && point.Z >= area.Minimum.Z && point.Z <= area.Maximum.Z;

    private static bool[] Flood(NavigationGrid grid, Vector3 start, NavigationCapabilities capabilities, CancellationToken token)
    {
        var reached = new bool[grid.CellCount];
        if (!grid.TryWorldToCell(start, out var cell) || !grid.IsTraversable(cell, capabilities)) return reached;
        var queue = new Queue<NavigationCellCoordinate>();
        reached[grid.GetIndex(cell)] = true;
        queue.Enqueue(cell);
        while (queue.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested();
            foreach (var offset in Neighbors)
            {
                var next = new NavigationCellCoordinate(current.X + offset.X, current.Z + offset.Z);
                if (!grid.IsTraversable(next, capabilities)) continue;
                int index = grid.GetIndex(next);
                if (reached[index]) continue;
                reached[index] = true;
                queue.Enqueue(next);
            }
        }
        return reached;
    }

    private static bool TryApproach(NavigationGrid grid, bool[] reachable,
        BattlefieldResourceDepositDefinition resource, out Vector3 approach)
    {
        approach = default;
        float maximum = MathF.Max(resource.HalfExtents.X, resource.HalfExtents.Z) + 128;
        float best = maximum * maximum;
        bool found = false;
        if (!grid.TryWorldToCell(resource.Center, out var origin)) return false;
        int radius = (int)MathF.Ceiling(maximum / grid.Settings.CellSizeMeters);
        for (int z = Math.Max(0, origin.Z - radius); z <= Math.Min(grid.Height - 1, origin.Z + radius); z++)
        for (int x = Math.Max(0, origin.X - radius); x <= Math.Min(grid.Width - 1, origin.X + radius); x++)
        {
            var cell = new NavigationCellCoordinate(x, z);
            if (!reachable[grid.GetIndex(cell)]) continue;
            var center = grid.GetCellCenter(cell);
            float dx = center.X - resource.Center.X, dz = center.Z - resource.Center.Z;
            float squared = dx * dx + dz * dz;
            if (squared > best || (found && squared == best)) continue;
            best = squared;
            approach = center;
            found = true;
        }
        return found;
    }

    private static string? NearestRoadNode(BattlefieldDefinition map, Vector3 point) =>
        map.RoadNodes.OrderBy(n => Vector2.DistanceSquared(new(n.Position.X, n.Position.Z), new(point.X, point.Z)))
            .ThenBy(n => n.Key, StringComparer.Ordinal).Select(n => n.Key).FirstOrDefault();

    private static double RoadCapacity(BattlefieldDefinition map, string? source, string? destination,
        Dictionary<string, bool> availability)
    {
        if (source is null || destination is null || source == destination) return 0;
        var capacity = map.RoadNodes.ToDictionary(n => n.Key, _ => 0.0, StringComparer.Ordinal);
        var settled = new HashSet<string>(StringComparer.Ordinal);
        capacity[source] = double.MaxValue;
        while (true)
        {
            string? current = capacity.Where(p => !settled.Contains(p.Key) && p.Value > 0)
                .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key).FirstOrDefault();
            if (current is null) return 0;
            if (current == destination) return capacity[current];
            settled.Add(current);
            foreach (var edge in map.RoadEdges.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (map.Crossings.Any(c => c.LogisticsEdgeKey == edge.Key && !availability[c.Key])) continue;
                string? next = edge.SourceNodeKey == current ? edge.DestinationNodeKey :
                    edge.DestinationNodeKey == current ? edge.SourceNodeKey : null;
                if (next is not null) capacity[next] = Math.Max(capacity[next], Math.Min(capacity[current], edge.CapacityPerSecond));
            }
        }
    }
}
