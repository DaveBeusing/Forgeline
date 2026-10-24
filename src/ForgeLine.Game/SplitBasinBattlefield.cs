using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.World;

namespace ForgeLine.Game;

// Authoring only. Production resolves the pinned compiled artifact through ScenarioRegistry.
public static class SplitBasinBattlefield
{
    public const string MapKey = "battlefield.split_basin";
    public const string CompositionKey = "split-basin.directorate.v1";
    public const float SizeMeters = 2048;

    public static BattlefieldTerrainRecipe CreateTerrainRecipe() => new(
        BattlefieldTerrainRecipe.CurrentVersion, new WorldGridSettings(),
        [new(0, 0), new(0, 0), new(0, 0)],
        1024, 90, 32, 1,
        [new(1024, 180), new(1664, 145)],
        [new(22, 760, 580, 120, 150), new(22, 1288, 580, 120, 150), new(-6, 1024, 1024, 300, 260)]);

    public static BattlefieldDefinition Create()
    {
        BattlefieldStartPosition[] starts =
        [
            new(new PlayerId(1), new(320, 0, 1024), new(370, 0, 1024), Bounds(96, 784, 400, 1264)),
            new(new PlayerId(2), new(1728, 0, 1024), new(1678, 0, 1024), Bounds(1648, 784, 1952, 1264))
        ];
        var resources = new List<BattlefieldResourceDepositDefinition>();
        foreach (bool east in new[] { false, true })
        {
            string side = east ? "east" : "west";
            float X(float x) => east ? SizeMeters - x : x;
            resources.Add(Deposit(side + ".ferrous", ResourceIds.FerrousOre, X(520), 870, 5500, 7.5, false));
            resources.Add(Deposit(side + ".volatiles", ResourceIds.Volatiles, X(460), 1180, 3500, 5, false));
            resources.Add(Deposit(side + ".silicates", ResourceIds.Silicates, X(650), 880, 4000, 5.5, false));
            resources.Add(Deposit(side + ".contested.ferrous", ResourceIds.FerrousOre, X(740), 760, 12000, 10, true));
            resources.Add(Deposit(side + ".contested.volatiles", ResourceIds.Volatiles, X(680), 1390, 8000, 7, true));
            resources.Add(Deposit(side + ".outpost.silicates", ResourceIds.Silicates, X(740), 1800, 10000, 8, true));
        }
        BattlefieldSiteDefinition[] sites =
        [
            Site("northwest.expansion", "West Overlook", BattlefieldSiteKind.Expansion, 650, 590, 105),
            Site("northeast.expansion", "East Overlook", BattlefieldSiteKind.Expansion, 1398, 590, 105),
            Site("west.mining_outpost", "West Basin Outpost", BattlefieldSiteKind.MiningOutpost, 680, 1500, 100),
            Site("east.mining_outpost", "East Basin Outpost", BattlefieldSiteKind.MiningOutpost, 1368, 1500, 100),
            Site("southwest.fob", "West Outer Staging", BattlefieldSiteKind.ForwardOperatingBase, 820, 1900, 90),
            Site("southeast.fob", "East Outer Staging", BattlefieldSiteKind.ForwardOperatingBase, 1228, 1900, 90)
        ];
        BattlefieldRoadNodeDefinition[] nodes =
        [
            new("west.start", new(420, 0, 1024)), new("west.junction", new(820, 0, 1024)),
            new("basin.west", new(928, 0, 1024)), new("basin.east", new(1120, 0, 1024)),
            new("outer.west", new(820, 0, 1664)), new("outer.east", new(1228, 0, 1664)),
            new("east.junction", new(1228, 0, 1024)), new("east.start", new(1628, 0, 1024))
        ];
        BattlefieldRoadEdgeDefinition[] edges =
        [
            new("west.start-link", "west.start", "west.junction", 1, 140),
            new("basin.west-link", "west.junction", "basin.west", 1, 120),
            new("basin.bridge", "basin.west", "basin.east", .8, 160),
            new("basin.east-link", "basin.east", "east.junction", 1, 120),
            new("outer.west-link", "west.junction", "outer.west", 1.2, 110),
            new("outer.ford", "outer.west", "outer.east", 1.4, 90),
            new("outer.east-link", "outer.east", "east.junction", 1.2, 110),
            new("east.start-link", "east.junction", "east.start", 1, 140)
        ];
        BattlefieldCrossingDefinition[] crossings =
        [
            new("crossing.basin_bridge", "Basin Bridge", new(1024, 0, 1024), Bounds(976, 880, 1072, 1168), "basin.bridge", true, true, 80),
            new("crossing.outer_ford", "Outer Ford", new(1024, 0, 1664), Bounds(976, 1520, 1072, 1808), "outer.ford", true, true, 120)
        ];
        AxisAlignedBounds[] obstacles =
        [Bounds(976, 0, 1072, 880), Bounds(976, 1168, 1072, 1520), Bounds(976, 1808, 1072, 2048)];
        var objects = new List<BattlefieldWorldObjectDefinition>();
        // Rocks on the impassable divide expose the actual blocker boundary. Groves stay outside yards/roads.
        foreach (bool east in new[] { false, true })
        {
            float X(float x) => east ? SizeMeters - x : x;
            string side = east ? "east" : "west";
            for (int i = 0; i < 9; i++)
                objects.Add(new(side + ".ridge.rock." + i, WorldVisualId.PropRock, WorldPresentationKind.Prop,
                    new(X(994), 0, 100 + i * 80), new(38, 24, 48), 0, true));
            for (int i = 0; i < 12; i++)
                objects.Add(new(side + ".outer.grove." + i, WorldVisualId.VegetationConifer, WorldPresentationKind.Vegetation,
                    new(X(580 + i % 3 * 22), 0, 1560 + i / 3 * 28), new(8, 22, 8), i * 37, true));
            objects.Add(new(side + ".yard.crate", WorldVisualId.PropCrate, WorldPresentationKind.Prop,
                new(X(240), 0, 720), new(5, 4, 5), 0, true));
            objects.Add(new(side + ".yard.wear", WorldVisualId.DecalTireTracks, WorldPresentationKind.Decal,
                new(X(420), 0, 1024), new(40, .08f, 8), 0, true));
        }
        var definition = new BattlefieldDefinition(new(MapKey, "Split Basin", SizeMeters, SizeMeters, 2), starts,
            resources.ToArray(), objects.ToArray(), sites, nodes, edges, crossings,
            [new("objective.west_command_core", new PlayerId(1), starts[0].CommandCorePosition),
             new("objective.east_command_core", new PlayerId(2), starts[1].CommandCorePosition)],
            obstacles, CentralDivideBattlefield.Create().TerrainVisual with { ProfileId = "split_basin.production" });
        BattlefieldValidator.ValidateDefinition(definition);
        return definition;
    }

    private static BattlefieldResourceDepositDefinition Deposit(string key, ResourceId resource, float x, float z, double quantity, double rate, bool contested) =>
        new(key, resource, new(x, 0, z), new(42, 4, 42), quantity, rate, contested);

    private static BattlefieldSiteDefinition Site(string key, string name, BattlefieldSiteKind kind, float x, float z, float radius) =>
        new(key, name, kind, new(x, 0, z), Bounds(x - radius, z - radius, x + radius, z + radius));

    private static AxisAlignedBounds Bounds(float x0, float z0, float x1, float z1) =>
        new(new(x0, -64, z0), new(x1, 128, z1));
}
