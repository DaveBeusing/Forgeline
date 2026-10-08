using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.World;

namespace ForgeLine.Game;

public static class CentralDivideBattlefield
{
    public const float WidthMeters = 3_072.0f;
    public const float HeightMeters = 3_072.0f;
    public const float BarrierMinimumX = 1_488.0f;
    public const float BarrierMaximumX = 1_584.0f;

    public static BattlefieldDefinition Create()
    {
        var definition = new BattlefieldDefinition(
            new BattlefieldMapMetadata(
                "prototype.vertical_slice",
                "Central Divide",
                WidthMeters,
                HeightMeters,
                RecommendedPlayers: 2),
            CreateStarts(),
            CreateResources(),
            CreateWorldObjects(),
            CreateSites(),
            CreateRoadNodes(),
            CreateRoadEdges(),
            CreateCrossings(),
            CreateObjectives(),
            CreateBarrierObstacles(),
            CreateTerrainVisual());

        BattlefieldValidator.ValidateDefinition(definition);
        return definition;
    }

    private static BattlefieldTerrainVisualDefinition CreateTerrainVisual() =>
        new(
            "central_divide.production",
            BattlefieldTerrainControlEncoding.RgbaFourLayer,
            ActiveLayerLimit: 4,
            ControlSamplesPerSide: 33,
            [
                "material.world.terrain.grass_ground",
                "material.world.terrain.dirt",
                "material.world.terrain.mud",
                "material.world.terrain.rock",
                "material.world.terrain.gravel",
                "material.world.terrain.industrial_ground",
                "material.world.terrain.concrete",
                "material.world.terrain.scorched"
            ]);

    private static BattlefieldStartPosition[] CreateStarts() =>
    [
        new(
            new PlayerId(1),
            new Vector3(420.0f, 0.0f, 1_536.0f),
            new Vector3(470.0f, 0.0f, 1_536.0f),
            Bounds2D(220.0f, 1_300.0f, 720.0f, 1_772.0f)),
        new(
            new PlayerId(2),
            new Vector3(2_652.0f, 0.0f, 1_536.0f),
            new Vector3(2_602.0f, 0.0f, 1_536.0f),
            Bounds2D(2_352.0f, 1_300.0f, 2_852.0f, 1_772.0f))
    ];

    private static BattlefieldResourceDepositDefinition[] CreateResources() =>
    [
        Deposit("west.ferrous", ResourceIds.FerrousOre, 620, 1_390, 5_500, 7.5, false),
        Deposit("west.volatiles", ResourceIds.Volatiles, 560, 1_690, 3_500, 5.0, false),
        Deposit("west.silicates", ResourceIds.Silicates, 760, 1_560, 4_000, 5.5, false),

        Deposit("east.ferrous", ResourceIds.FerrousOre, 2_452, 1_390, 5_500, 7.5, false),
        Deposit("east.volatiles", ResourceIds.Volatiles, 2_512, 1_690, 3_500, 5.0, false),
        Deposit("east.silicates", ResourceIds.Silicates, 2_312, 1_560, 4_000, 5.5, false),

        Deposit("north.contested.ferrous", ResourceIds.FerrousOre, 1_360, 760, 12_000, 10.0, true),
        Deposit("north.contested.volatiles", ResourceIds.Volatiles, 1_720, 690, 8_000, 7.0, true),
        Deposit("center.contested.silicates", ResourceIds.Silicates, 1_536, 1_580, 10_000, 8.0, true),
        Deposit("south.contested.ferrous", ResourceIds.FerrousOre, 1_720, 2_430, 12_000, 10.0, true),
        Deposit("south.contested.volatiles", ResourceIds.Volatiles, 1_330, 2_360, 8_000, 7.0, true),

        Deposit("west.outpost.silicates", ResourceIds.Silicates, 700, 520, 9_000, 7.0, true),
        Deposit("east.outpost.silicates", ResourceIds.Silicates, 2_372, 2_560, 9_000, 7.0, true),
        Deposit("center.rare_elements", ResourceIds.RareElements, 1_536, 1_310, 4_500, 3.5, true),
        Deposit("south.rare_elements", ResourceIds.RareElements, 1_535, 2_690, 5_200, 3.5, true)
    ];

    private static BattlefieldWorldObjectDefinition[] CreateWorldObjects() =>
    [
        WorldObject("prop.rock.west", WorldVisualId.PropRock, WorldPresentationKind.Prop, 830, 1_250, 18, 9, 15, 22),
        WorldObject("prop.barrier.west", WorldVisualId.PropBarrier, WorldPresentationKind.Prop, 1_060, 1_520, 22, 3, 5, 90),
        WorldObject("prop.concrete.north", WorldVisualId.PropConcreteBlock, WorldPresentationKind.Prop, 1_330, 895, 9, 4, 4, 0),
        WorldObject("prop.crate.west", WorldVisualId.PropCrate, WorldPresentationKind.Prop, 650, 560, 5, 5, 5, 15),
        WorldObject("prop.drum.west", WorldVisualId.PropDrum, WorldPresentationKind.Prop, 675, 548, 3, 5, 3, 0),
        WorldObject("prop.pallet.east", WorldVisualId.PropPallet, WorldPresentationKind.Prop, 2_410, 2_520, 7, 1, 5, 10),
        WorldObject("prop.pipe.east", WorldVisualId.PropPipeSection, WorldPresentationKind.Prop, 2_380, 2_535, 12, 4, 4, 80),
        WorldObject("prop.utility.east", WorldVisualId.PropUtilityBox, WorldPresentationKind.Prop, 2_445, 2_535, 5, 7, 4, 0),
        WorldObject("prop.fence.north", WorldVisualId.PropFence, WorldPresentationKind.Prop, 1_255, 950, 24, 4, 1, 0),
        WorldObject("prop.light.south", WorldVisualId.PropIndustrialLightSignage, WorldPresentationKind.Prop, 1_815, 2_165, 3, 14, 3, 0),
        WorldObject("prop.rubble.center", WorldVisualId.PropRubble, WorldPresentationKind.Prop, 1_610, 1_690, 18, 5, 14, 35),

        WorldObject("vegetation.conifer.01", WorldVisualId.VegetationConifer, WorldPresentationKind.Vegetation, 920, 680, 8, 24, 8, 5),
        WorldObject("vegetation.conifer.02", WorldVisualId.VegetationConifer, WorldPresentationKind.Vegetation, 960, 705, 7, 21, 7, 42),
        WorldObject("vegetation.conifer.03", WorldVisualId.VegetationConifer, WorldPresentationKind.Vegetation, 2_130, 2_385, 9, 26, 9, 19),
        WorldObject("vegetation.scrub.01", WorldVisualId.VegetationScrub, WorldPresentationKind.Vegetation, 1_170, 1_245, 10, 5, 9, 0),
        WorldObject("vegetation.scrub.02", WorldVisualId.VegetationScrub, WorldPresentationKind.Vegetation, 1_905, 1_785, 9, 4, 8, 0),
        WorldObject("vegetation.scrub.03", WorldVisualId.VegetationScrub, WorldPresentationKind.Vegetation, 780, 2_180, 11, 5, 10, 0),
        WorldObject("vegetation.grass.01", WorldVisualId.VegetationGrassClump, WorldPresentationKind.Vegetation, 1_085, 735, 12, 2, 12, 0),
        WorldObject("vegetation.grass.02", WorldVisualId.VegetationGrassClump, WorldPresentationKind.Vegetation, 1_980, 2_315, 14, 2, 14, 0),

        WorldObject("decal.tire.west", WorldVisualId.DecalTireTracks, WorldPresentationKind.Decal, 790, 1_535, 42, 0.08f, 8, 0),
        WorldObject("decal.tracked.north", WorldVisualId.DecalTrackedVehicleMarks, WorldPresentationKind.Decal, 1_345, 945, 36, 0.08f, 10, 8),
        WorldObject("decal.roadwear.center", WorldVisualId.DecalRoadWear, WorldPresentationKind.Decal, 1_536, 920, 72, 0.08f, 14, 90),
        WorldObject("decal.oil.west", WorldVisualId.DecalOilStain, WorldPresentationKind.Decal, 665, 535, 12, 0.08f, 10, 0),
        WorldObject("decal.blast.center", WorldVisualId.DecalBlastMark, WorldPresentationKind.Decal, 1_640, 1_630, 16, 0.08f, 16, 0),
        WorldObject("decal.shell.south", WorldVisualId.DecalShellImpact, WorldPresentationKind.Decal, 1_420, 2_310, 8, 0.08f, 8, 0),
        WorldObject("decal.scorch.east", WorldVisualId.DecalScorchMark, WorldPresentationKind.Decal, 2_365, 2_545, 14, 0.08f, 12, 0),
        WorldObject("decal.crack.north", WorldVisualId.DecalConcreteCrack, WorldPresentationKind.Decal, 1_535, 918, 18, 0.08f, 10, 90)
    ];

    private static BattlefieldSiteDefinition[] CreateSites() =>
    [
        Site("northwest.expansion", "Northwest Expansion", BattlefieldSiteKind.Expansion, 900, 760, 180),
        Site("northeast.expansion", "Northeast Expansion", BattlefieldSiteKind.Expansion, 2_160, 760, 180),
        Site("southwest.expansion", "Southwest Expansion", BattlefieldSiteKind.Expansion, 900, 2_330, 180),
        Site("southeast.expansion", "Southeast Expansion", BattlefieldSiteKind.Expansion, 2_160, 2_330, 180),
        Site("west.mining_outpost", "Western Mining Outpost", BattlefieldSiteKind.MiningOutpost, 650, 520, 150),
        Site("east.mining_outpost", "Eastern Mining Outpost", BattlefieldSiteKind.MiningOutpost, 2_420, 2_560, 150),
        Site("northwest.fob", "Northwest FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_250, 930, 120),
        Site("northeast.fob", "Northeast FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_820, 930, 120),
        Site("southwest.fob", "Southwest FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_250, 2_190, 120),
        Site("southeast.fob", "Southeast FOB", BattlefieldSiteKind.ForwardOperatingBase, 1_820, 2_190, 120)
    ];

    private static BattlefieldRoadNodeDefinition[] CreateRoadNodes() =>
    [
        new("west.start", new Vector3(520.0f, 0.0f, 1_536.0f)),
        new("west.junction", new Vector3(1_020.0f, 0.0f, 1_536.0f)),
        new("north.west", new Vector3(1_360.0f, 0.0f, 920.0f)),
        new("north.east", new Vector3(1_712.0f, 0.0f, 920.0f)),
        new("south.west", new Vector3(1_360.0f, 0.0f, 2_200.0f)),
        new("south.east", new Vector3(1_712.0f, 0.0f, 2_200.0f)),
        new("east.junction", new Vector3(2_052.0f, 0.0f, 1_536.0f)),
        new("east.start", new Vector3(2_552.0f, 0.0f, 1_536.0f))
    ];

    private static BattlefieldRoadEdgeDefinition[] CreateRoadEdges() =>
    [
        new("west.start-link", "west.start", "west.junction", 1.0, 140.0),
        new("north.west-link", "west.junction", "north.west", 1.0, 120.0),
        new("north.bridge", "north.west", "north.east", 0.8, 160.0),
        new("north.east-link", "north.east", "east.junction", 1.0, 120.0),
        new("south.west-link", "west.junction", "south.west", 1.2, 110.0),
        new("south.ford", "south.west", "south.east", 1.4, 90.0),
        new("south.east-link", "south.east", "east.junction", 1.2, 110.0),
        new("east.start-link", "east.junction", "east.start", 1.0, 140.0)
    ];

    private static BattlefieldCrossingDefinition[] CreateCrossings() =>
    [
        new(
            "crossing.north_bridge",
            "North Bridge",
            new Vector3(1_536.0f, 0.0f, 920.0f),
            Bounds2D(BarrierMinimumX, 840.0f, BarrierMaximumX, 1_000.0f),
            "north.bridge",
            InitiallyOperational: true,
            Restorable: true,
            RestorationTicks: 80),
        new(
            "crossing.south_ford",
            "South Ford",
            new Vector3(1_536.0f, 0.0f, 2_200.0f),
            Bounds2D(BarrierMinimumX, 2_100.0f, BarrierMaximumX, 2_300.0f),
            "south.ford",
            InitiallyOperational: true,
            Restorable: true,
            RestorationTicks: 120)
    ];

    private static BattlefieldObjectiveDefinition[] CreateObjectives() =>
    [
        new("objective.west_command_core", new PlayerId(1), new Vector3(470.0f, 0.0f, 1_536.0f)),
        new("objective.east_command_core", new PlayerId(2), new Vector3(2_602.0f, 0.0f, 1_536.0f))
    ];

    private static AxisAlignedBounds[] CreateBarrierObstacles() =>
    [
        Bounds2D(BarrierMinimumX, 0.0f, BarrierMaximumX, 840.0f),
        Bounds2D(BarrierMinimumX, 1_000.0f, BarrierMaximumX, 2_100.0f),
        Bounds2D(BarrierMinimumX, 2_300.0f, BarrierMaximumX, HeightMeters)
    ];

    private static BattlefieldResourceDepositDefinition Deposit(
        string key,
        ResourceId resource,
        float x,
        float z,
        double quantity,
        double rate,
        bool contested) =>
        new(
            key,
            resource,
            new Vector3(x, 0.0f, z),
            new Vector3(42.0f, 4.0f, 42.0f),
            quantity,
            rate,
            contested);

    private static BattlefieldWorldObjectDefinition WorldObject(
        string key,
        WorldVisualId visual,
        WorldPresentationKind kind,
        float x,
        float z,
        float scaleX,
        float scaleY,
        float scaleZ,
        float rotationDegrees) =>
        new(
            key,
            visual,
            kind,
            new Vector3(x, 0.0f, z),
            new Vector3(scaleX, scaleY, scaleZ),
            rotationDegrees);

    private static BattlefieldSiteDefinition Site(
        string key,
        string displayName,
        BattlefieldSiteKind kind,
        float x,
        float z,
        float halfSize) =>
        new(
            key,
            displayName,
            kind,
            new Vector3(x, 0.0f, z),
            Bounds2D(
                x - halfSize,
                z - halfSize,
                x + halfSize,
                z + halfSize));

    internal static AxisAlignedBounds Bounds2D(
        float minimumX,
        float minimumZ,
        float maximumX,
        float maximumZ) =>
        new(
            new Vector3(minimumX, -64.0f, minimumZ),
            new Vector3(maximumX, 128.0f, maximumZ));
}
