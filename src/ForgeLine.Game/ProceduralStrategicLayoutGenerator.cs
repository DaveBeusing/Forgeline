using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Offline two-player placement with bounded search and the existing qualification gates.</summary>
public static class ProceduralStrategicLayoutGenerator
{
    public const string GeographyProfile = "geography.strategic.two-player.v1";
    public const string RulesProfile = "rules.directorate.strategic.v1";
    public const int MaximumAttempts = 8;
    private static readonly ResourceId[] Vital = [ResourceIds.FerrousOre, ResourceIds.Volatiles, ResourceIds.Silicates];
    private static readonly double[] Quantities = [5500, 3500, 4000];
    private static readonly double[] Rates = [7.5, 5, 5.5];
    private static readonly NavigationMovementClass[] Movements =
        [NavigationMovementClass.Infantry, NavigationMovementClass.Wheeled, NavigationMovementClass.Tracked];
    private static readonly (int X, int Z)[] Offsets =
        (from z in Enumerable.Range(-8, 17) from x in Enumerable.Range(-8, 17)
         orderby x * x + z * z, z, x select (x * 16, z * 16)).ToArray();

    public static ProceduralStrategicLayoutResult Generate(TerrainWorld terrain, WorldGenerationRecipe recipe,
        int maximumAttempts = MaximumAttempts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumAttempts, MaximumAttempts);
        cancellationToken.ThrowIfCancellationRequested();
        var attempts = new List<StrategicLayoutAttempt>();
        string? invalid = ValidateInput(terrain, recipe);
        if (invalid is not null)
            return new(null, [new(0, "InvalidInput", invalid)]);
        var random = new WorldGenerationRandom(recipe);
        for (int attempt = 0; attempt < maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var map = Place(terrain, random, attempt, cancellationToken);
                BattlefieldValidator.ValidateDefinition(map);
                var geography = BattlefieldOperationalGeographyValidator.Validate(map, terrain);
                var audit = StrategicMapAudit.Analyze(map, terrain, cancellationToken: cancellationToken);
                // Generated layouts have no implicit warning exceptions or asymmetry waivers.
                if (audit.ErrorCount != 0 || audit.WarningCount != 0)
                {
                    var first = audit.Diagnostics.First(d => d.Severity is "Error" or "Warning");
                    throw new LayoutRejectedException("AuditRejected", first.Id + ": " + first.Message);
                }
                var mask = Reserve(map, terrain);
                var fingerprint = Fingerprint(recipe, terrain, map, mask);
                return new(new(map, mask, fingerprint, audit, geography, attempt + 1), attempts);
            }
            catch (LayoutRejectedException error)
            {
                attempts.Add(new(attempt + 1, error.Code, error.Message));
            }
            catch (InvalidOperationException error)
            {
                attempts.Add(new(attempt + 1, "QualificationRejected", error.Message[..Math.Min(1024, error.Message.Length)]));
            }
        }
        return new(null, attempts);
    }

    private static string? ValidateInput(TerrainWorld terrain, WorldGenerationRecipe recipe)
    {
        if (recipe.GetValidationError() is { } error) return error.Code + ": " + error.Message;
        if (recipe.GeographyProfileId != GeographyProfile || recipe.RulesProfileId != RulesProfile)
            return "Unavailable strategic geography or rules profile.";
        var bounds = terrain.WorldBounds;
        float size = recipe.ChunkSizeMillimeters / 1000f;
        if (recipe.Origin != new ChunkCoordinate(0, 0) || bounds.Minimum.X != 0 || bounds.Minimum.Z != 0 ||
            bounds.Maximum.X != recipe.WidthInChunks * size || bounds.Maximum.Z != recipe.HeightInChunks * size ||
            size != terrain.Settings.ChunkSizeMeters || recipe.SamplesPerSide != terrain.Settings.HeightSamplesPerSide ||
            bounds.Maximum.X is < 2048 or > 4096 || bounds.Maximum.Z is < 2048 or > 4096 ||
            size % 16 != 0 || terrain.Chunks.Count != recipe.WidthInChunks * recipe.HeightInChunks ||
            terrain.Chunks.Any(c => c.Heightfield.SamplesPerSide != recipe.SamplesPerSide))
            return "Version 1 requires a complete zero-origin 2048..4096 m rectangle matching the recipe and a 16 m navigation lattice.";
        return null;
    }

    private static BattlefieldDefinition Place(TerrainWorld terrain, WorldGenerationRandom random, int attempt,
        CancellationToken cancellationToken)
    {
        float width = terrain.WorldBounds.Maximum.X;
        float height = terrain.WorldBounds.Maximum.Z;
        ulong draw = random.At(WorldGenerationDomain.Geography, new(0, 0), (ulong)attempt, 1);
        float shift = ((int)(draw % 9) - 4) * 16;
        var used = new List<AxisAlignedBounds>();
        Vector3 Position(float x, float z, float radius, string label)
        {
            foreach (var offset in Offsets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                float px = MathF.Floor((x + offset.X) / 16) * 16 + 8;
                float pz = MathF.Floor((z + offset.Z) / 16) * 16 + 8;
                var area = Bounds(px, pz, radius);
                if (area.Minimum.X < 16 || area.Minimum.Z < 16 || area.Maximum.X >= width - 16 ||
                    area.Maximum.Z >= height - 16 || used.Any(b => Overlap(b, area)) || !IsSuitableConstructionFootprint(terrain, area)) continue;
                if (!terrain.TrySampleHeight(px, pz, out float y)) continue;
                used.Add(area);
                return new(px, y, pz);
            }
            throw new LayoutRejectedException("PlacementExhausted", label + ": no clear low-slope footprint in 289 candidates.");
        }
        var starts = new BattlefieldStartPosition[2];
        var resources = new List<BattlefieldResourceDepositDefinition>();
        var sites = new List<BattlefieldSiteDefinition>();
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? width * .17f : width * .83f;
            var p = Position(x, height * .5f + shift, 128, "start");
            float coreX = p.X + (side == 0 ? 48 : -48);
            terrain.TrySampleHeight(coreX, p.Z, out float coreY);
            starts[side] = new(new PlayerId((ulong)side + 1), p, new(coreX, coreY, p.Z), Bounds(p.X, p.Z, 128));
            for (int type = 0; type < 3; type++)
            {
                var center = Position(p.X + (side == 0 ? 224 : -224), p.Z + (type - 1) * 160, 48, "local resource");
                resources.Add(new(Key("local", side, type), Vital[type], center, new(42, 4, 42),
                    Quantities[type], Rates[type], false));
            }
            for (int kind = 0; kind < 3; kind++)
            {
                float siteX = width * (side == 0 ? .3f : .7f);
                float siteZ = height * (kind == 0 ? .25f : kind == 1 ? .75f : .5f) + shift;
                var center = Position(siteX, siteZ, 96, "strategic site");
                string key = Key("site", side, kind);
                sites.Add(new(key, key, (BattlefieldSiteKind)(kind + 1), center, Bounds(center.X, center.Z, 96)));
            }
        }
        for (int row = 0; row < 2; row++)
            for (int type = 0; type < 3; type++)
            {
                var center = Position(width * .5f + (type - 1) * 176,
                    height * (row == 0 ? .25f : .75f) + shift, 48, "contested resource");
                resources.Add(new(Key("contested", row, type), Vital[type], center, new(42, 4, 42),
                    Quantities[type] * 2, Rates[type] * 1.3, true));
            }
        var obstacles = resources.Select(r => Bounds(r.Center.X, r.Center.Z, 44)).ToArray();
        var world = NavigationWorld.Build(terrain, obstacles,
            new NavigationGridSettings { CellSizeMeters = 16, StaticObstacleClearanceMeters = .5f });
        var finder = new HierarchicalPathfinder(world, maximumFallbackExpandedNodes: world.Grid.CellCount);
        var nodes = new Dictionary<NavigationCellCoordinate, BattlefieldRoadNodeDefinition>();
        var edges = new Dictionary<string, BattlefieldRoadEdgeDefinition>(StringComparer.Ordinal);
        world.Grid.TryWorldToCell(starts[0].Position, out var westStart);
        world.Grid.TryWorldToCell(starts[1].Position, out var eastStart);
        string Node(NavigationCellCoordinate cell)
        {
            if (!nodes.TryGetValue(cell, out var node))
            {
                string key = cell == westStart ? "west.start" : cell == eastStart ? "east.start" : Key("road", cell.X, cell.Z);
                node = new(key, world.Grid.GetCellCenter(cell));
                nodes.Add(cell, node);
            }
            return node.Key;
        }
        List<string> Route(Vector3 from, Vector3 to)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NavigationPath? path = null;
            foreach (var movement in Movements)
            {
                var result = finder.FindPath(from, to, NavigationCapabilities.For(movement), projectBlockedEndpoints: false);
                if (!result.Succeeded) throw new LayoutRejectedException("RouteRejected", movement + ": " + result.FailureReason);
                if (movement == NavigationMovementClass.Wheeled) path = result.Path;
            }
            var keys = new List<string>();
            var cells = ExpandRoadCells(path!.RefinedCells, world.Grid);
            for (int i = 0; i < cells.Count; i++)
            {
                string current = Node(cells[i]);
                if (i == 0) continue;
                string previous = Node(cells[i - 1]);
                string key = string.CompareOrdinal(previous, current) < 0 ? previous + ".to." + current : current + ".to." + previous;
                edges.TryAdd(key, new(key, previous, current, Vector3.Distance(nodes[cells[i - 1]].Position,
                    nodes[cells[i]].Position), 120));
                keys.Add(key);
            }
            if (nodes.Count > 2048 || edges.Count > 8192)
                throw new LayoutRejectedException("TopologyBudget", "Road graph exceeds bounded map topology.");
            return keys;
        }
        // Two separated waypoints create a cycle. All cell intersections share one actual node.
        var cuts = new List<string>();
        for (int row = 0; row < 2; row++)
        {
            var west = sites[row].Position;
            var east = sites[3 + row].Position;
            Route(starts[0].Position, west);
            var corridor = Route(west, east);
            cuts.Add(corridor.OrderBy(key => MathF.Abs(nodes.Values.First(n => n.Key == edges[key].SourceNodeKey).Position.X - width / 2))
                .ThenBy(key => key, StringComparer.Ordinal).First());
            Route(east, starts[1].Position);
        }
        foreach (var site in sites) Route(starts[site.Position.X < width / 2 ? 0 : 1].Position, site.Position);
        ValidateAlternatives(terrain, starts, nodes.Values.ToArray(), edges.Values.ToArray(), obstacles, cuts, cancellationToken);
        // Junctions are shared road cells. No invented bridge/ford is declared on arbitrary landforms.
        var visual = new BattlefieldTerrainVisualDefinition("strategic.production.v1",
            BattlefieldTerrainControlEncoding.RgbaFourLayer, 4, 33,
            ["material.world.terrain.grass_ground", "material.world.terrain.dirt", "material.world.terrain.rock", "material.world.terrain.gravel"]);
        return new(new("battlefield.generated.strategic", "Generated Strategic Battlefield", width, height, 2), starts,
            resources.ToArray(), [], sites.ToArray(), nodes.Values.OrderBy(n => n.Key, StringComparer.Ordinal).ToArray(),
            edges.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToArray(), [],
            starts.Select(s => new BattlefieldObjectiveDefinition(Key("objective", (int)s.Player.Value, 0), s.Player, s.CommandCorePosition)).ToArray(),
            obstacles, visual);
    }

    /// <summary>Checks the version-1 16 m footprint lattice against the six-degree placement limit.</summary>
    public static bool IsSuitableConstructionFootprint(ITerrainQuery terrain, AxisAlignedBounds area)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (!float.IsFinite(area.Minimum.X) || !float.IsFinite(area.Minimum.Z) ||
            !float.IsFinite(area.Maximum.X) || !float.IsFinite(area.Maximum.Z) ||
            area.Maximum.X <= area.Minimum.X || area.Maximum.Z <= area.Minimum.Z ||
            area.Minimum.X < terrain.WorldBounds.Minimum.X || area.Minimum.Z < terrain.WorldBounds.Minimum.Z ||
            area.Maximum.X >= terrain.WorldBounds.Maximum.X || area.Maximum.Z >= terrain.WorldBounds.Maximum.Z ||
            area.Maximum.X - area.Minimum.X > 256 || area.Maximum.Z - area.Minimum.Z > 256)
            return false;
        for (float z = area.Minimum.Z; z <= area.Maximum.Z; z += 16)
            for (float x = area.Minimum.X; x <= area.Maximum.X; x += 16)
                if (!terrain.TrySampleHeight(x, z, out float h) || !float.IsFinite(h) ||
                    !terrain.TrySampleNormal(x, z, out var normal) || !float.IsFinite(normal.Y) || normal.Y < .9945219f)
                    return false; // six-degree placement policy; gameplay still owns actual building validation.
        return true;
    }

    private static List<NavigationCellCoordinate> ExpandRoadCells(IReadOnlyList<NavigationCellCoordinate> compressed,
        NavigationGrid grid)
    {
        var result = new List<NavigationCellCoordinate>();
        var capabilities = NavigationCapabilities.For(NavigationMovementClass.Wheeled);
        void Add(NavigationCellCoordinate cell)
        {
            if (!grid.IsTraversable(cell, capabilities))
                throw new LayoutRejectedException("RoadRasterRejected", "A road raster cell is not traversable.");
            if (result.Count >= grid.CellCount)
                throw new LayoutRejectedException("RoadRasterBudget", "Road raster exceeds the navigation cell budget.");
            result.Add(cell);
        }
        if (compressed.Count == 0)
            throw new LayoutRejectedException("RoadRasterRejected", "Navigation returned an empty route.");
        Add(compressed[0]);
        for (int i = 1; i < compressed.Count; i++)
        {
            var from = compressed[i - 1];
            var to = compressed[i];
            int dx = to.X - from.X;
            int dz = to.Z - from.Z;
            if (dx != 0 && dz != 0 && Math.Abs(dx) != Math.Abs(dz))
                throw new LayoutRejectedException("RoadRasterRejected", "Unsupported non-collinear compressed route segment.");
            int length = Math.Max(Math.Abs(dx), Math.Abs(dz));
            for (int step = 0; step < length; step++)
            {
                // LocalPathSearch compresses collinear eight-neighbor steps. Expand them without another search.
                // Orthogonal road links turn diagonal intersections into real shared lattice nodes.
                var next = new NavigationCellCoordinate(from.X + Math.Sign(dx), from.Z + Math.Sign(dz));
                if (dx != 0 && dz != 0) Add(new(next.X, from.Z));
                Add(next);
                from = next;
            }
        }
        return result;
    }

    private static void ValidateAlternatives(TerrainWorld terrain, BattlefieldStartPosition[] starts,
        BattlefieldRoadNodeDefinition[] nodes, BattlefieldRoadEdgeDefinition[] edges, AxisAlignedBounds[] obstacles,
        List<string> cuts, CancellationToken cancellationToken)
    {
        var byKey = nodes.ToDictionary(n => n.Key, StringComparer.Ordinal);
        var cutEdges = cuts.Select(key => edges.First(e => e.Key == key)).ToArray();
        if (cutEdges.Length != 2 || cutEdges[0].Key == cutEdges[1].Key ||
            MathF.Abs(byKey[cutEdges[0].SourceNodeKey].Position.Z - byKey[cutEdges[1].SourceNodeKey].Position.Z) <
            terrain.WorldBounds.Maximum.Z / 4)
            throw new LayoutRejectedException("NoIndependentCorridors", "Northern and southern road corridors do not remain separated.");
        foreach (var cut in cutEdges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var visited = new HashSet<string>(StringComparer.Ordinal) { "west.start" };
            var queue = new Queue<string>();
            queue.Enqueue("west.start");
            while (queue.TryDequeue(out var current))
                foreach (var edge in edges)
                {
                    if (edge.Key == cut.Key) continue;
                    string? next = edge.SourceNodeKey == current ? edge.DestinationNodeKey :
                        edge.DestinationNodeKey == current ? edge.SourceNodeKey : null;
                    if (next is not null && visited.Add(next)) queue.Enqueue(next);
                }
            if (!visited.Contains("east.start"))
                throw new LayoutRejectedException("NoAlternateRoad", "Removing a central corridor edge disconnects the logistics graph.");
            var point = byKey[cut.SourceNodeKey].Position;
            var alternate = NavigationWorld.Build(terrain, obstacles.Append(Bounds(point.X, point.Z, 8)),
                new NavigationGridSettings { CellSizeMeters = 16, StaticObstacleClearanceMeters = .5f });
            var finder = new HierarchicalPathfinder(alternate, maximumFallbackExpandedNodes: alternate.Grid.CellCount);
            foreach (var movement in Movements)
                if (!finder.FindPath(starts[0].Position, starts[1].Position, NavigationCapabilities.For(movement),
                    projectBlockedEndpoints: false).Succeeded)
                    throw new LayoutRejectedException("NoAlternateNavigation", "A central corridor cut disconnects " + movement + ".");
        }
    }

    private static StrategicReservationMask Reserve(BattlefieldDefinition map, TerrainWorld terrain)
    {
        var grid = NavigationWorld.Build(terrain, map.StaticNavigationObstacles,
            new NavigationGridSettings { CellSizeMeters = 16 }).Grid;
        var cells = new StrategicReservation[grid.CellCount];
        var areas = map.Starts.Select(s => (s.BuildArea, StrategicReservation.Construction))
            .Concat(map.Sites.Select(s => (s.BuildArea, StrategicReservation.Construction)))
            .Concat(map.Resources.Select(r => (Bounds(r.Center.X, r.Center.Z, 114), StrategicReservation.Resource | StrategicReservation.Construction)))
            .Concat(map.RoadNodes.Select(n => (Bounds(n.Position.X, n.Position.Z, 16), StrategicReservation.Traffic)))
            .ToArray();
        for (int z = 0; z < grid.Height; z++)
            for (int x = 0; x < grid.Width; x++)
            {
                var coordinate = new NavigationCellCoordinate(x, z);
                var bounds = grid.GetCellBounds(coordinate);
                StrategicReservation flags = grid.IsTraversable(coordinate, NavigationCapabilities.For(NavigationMovementClass.Wheeled))
                    ? StrategicReservation.None : StrategicReservation.Obstacle;
                foreach (var area in areas)
                    if (Overlap(bounds, area.Item1)) flags |= area.Item2;
                cells[z * grid.Width + x] = flags;
            }
        return new(grid.Width, grid.Height, cells);
    }

    private static WorldGenerationFingerprint Fingerprint(WorldGenerationRecipe recipe, TerrainWorld terrain,
        BattlefieldDefinition map, StrategicReservationMask mask)
    {
        var writer = new WorldCanonicalWriter();
        // Payload schema 1: exact finite IEEE-754 bits in little endian, ordered arrays with counts.
        void Float(float v) => writer.WriteInt32(BitConverter.SingleToInt32Bits(v));
        void Point(Vector3 p) { Float(p.X); Float(p.Y); Float(p.Z); }
        void Area(AxisAlignedBounds b) { Point(b.Minimum); Point(b.Maximum); }
        void Double(double v) => writer.WriteInt64(BitConverter.DoubleToInt64Bits(v));
        writer.WriteInt32(map.MapRevision); Float(map.Metadata.WidthMeters); Float(map.Metadata.HeightMeters);
        writer.WriteInt32(map.Starts.Count);
        foreach (var s in map.Starts) { writer.WriteUInt64(s.Player.Value); Point(s.Position); Point(s.CommandCorePosition); Area(s.BuildArea); }
        writer.WriteInt32(map.Resources.Count);
        foreach (var r in map.Resources)
        { writer.WriteIdentifier(r.Key); writer.WriteUInt32(r.ResourceId.Value); Point(r.Center); Point(r.HalfExtents); Double(r.TotalQuantity); Double(r.ExtractionRatePerSecond); writer.WriteUInt32(r.Contested ? 1u : 0u); }
        writer.WriteInt32(map.Sites.Count);
        foreach (var s in map.Sites) { writer.WriteIdentifier(s.Key); writer.WriteUInt32((uint)s.Kind); Point(s.Position); Area(s.BuildArea); }
        writer.WriteInt32(map.RoadNodes.Count);
        foreach (var n in map.RoadNodes) { writer.WriteIdentifier(n.Key); Point(n.Position); }
        writer.WriteInt32(map.RoadEdges.Count);
        foreach (var e in map.RoadEdges)
        { writer.WriteIdentifier(e.Key); writer.WriteIdentifier(e.SourceNodeKey); writer.WriteIdentifier(e.DestinationNodeKey); Double(e.BaseCost); Double(e.CapacityPerSecond); }
        writer.WriteInt32(map.StaticNavigationObstacles.Count);
        foreach (var b in map.StaticNavigationObstacles) Area(b);
        writer.WriteInt32(mask.Width); writer.WriteInt32(mask.Height);
        for (int z = 0; z < mask.Height; z++) for (int x = 0; x < mask.Width; x++) writer.WriteUInt32((uint)mask.GetCell(x, z));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] bytes = new byte[4];
        foreach (var chunk in terrain.Chunks)
        {
            foreach (int value in new[] { chunk.Coordinate.X, chunk.Coordinate.Z, chunk.Heightfield.SamplesPerSide })
            { BinaryPrimitives.WriteInt32LittleEndian(bytes, value); hash.AppendData(bytes); }
            for (int z = 0; z < chunk.Heightfield.SamplesPerSide; z++)
                for (int x = 0; x < chunk.Heightfield.SamplesPerSide; x++)
                { BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(chunk.Heightfield.GetHeight(x, z))); hash.AppendData(bytes); }
        }
        writer.WriteBytes(hash.GetHashAndReset());
        byte[] payload = writer.ToArray();
        var records = new List<WorldGenerationContent>();
        for (int start = 0; start < payload.Length; start += WorldGenerationContent.MaximumPayloadBytes)
            records.Add(new(new(0, 0), 0x534c0001, 1, (ulong)records.Count,
                payload.AsSpan(start, Math.Min(WorldGenerationContent.MaximumPayloadBytes, payload.Length - start))));
        return WorldGenerationFingerprint.Gameplay(recipe, records);
    }

    private static string Key(string kind, int x, int z) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind}.{x}.{z}");
    private static AxisAlignedBounds Bounds(float x, float z, float radius) =>
        new(new(x - radius, -64, z - radius), new(x + radius, 128, z + radius));
    private static bool Overlap(AxisAlignedBounds a, AxisAlignedBounds b) =>
        a.Minimum.X <= b.Maximum.X && a.Maximum.X >= b.Minimum.X && a.Minimum.Z <= b.Maximum.Z && a.Maximum.Z >= b.Minimum.Z;

    private sealed class LayoutRejectedException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
