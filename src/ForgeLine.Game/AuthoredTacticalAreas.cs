using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Deliberate map rules. Decorative instances never imply protection or concealment.</summary>
internal static class AuthoredTacticalAreas
{
    internal static TacticalAreaSet CentralDivide() => new(
    [
        Area("cover.northwest_approach", "West bridge cover", 1280, 850, 1400, 895, .65, false),
        Area("cover.northeast_approach", "East bridge cover", 1672, 850, 1792, 895, .65, false),
        Area("cover.southwest_approach", "West ford cover", 1260, 2300, 1380, 2345, .65, false),
        Area("cover.southeast_approach", "East ford cover", 1692, 2300, 1812, 2345, .65, false),
        Area("screen.west_outskirts", "West visual screen", 890, 650, 1010, 740, 1, true),
        Area("screen.east_outskirts", "East visual screen", 2062, 650, 2182, 740, 1, true)
    ]);

    internal static TacticalAreaSet SplitBasin() => new(
    [
        Area("cover.west_basin", "West basin cover", 840, 1175, 940, 1220, .65, false),
        Area("cover.east_basin", "East basin cover", 1108, 1175, 1208, 1220, .65, false),
        Area("cover.west_outer", "West outer cover", 820, 1815, 920, 1860, .65, false),
        Area("cover.east_outer", "East outer cover", 1128, 1815, 1228, 1860, .65, false),
        Area("screen.west_grove", "West grove screen", 560, 1540, 660, 1680, 1, true),
        Area("screen.east_grove", "East grove screen", 1388, 1540, 1488, 1680, 1, true)
    ]);

    private static TacticalAreaDefinition Area(string key, string name, float x0, float z0, float x1, float z1,
        double multiplier, bool screen) => new(key, name,
            new AxisAlignedBounds(new Vector3(x0, -64, z0), new Vector3(x1, 128, z1)), multiplier, screen);
}
