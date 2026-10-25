using System.Globalization;
using System.Text;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.MapCompiler;

internal static class BattlefieldMapPreview
{
    // Offline documentation view of the same loaded geometry used in qualification.
    internal static void Write(string path, BattlefieldDefinition map, TerrainWorld terrain)
    {
        var svg = new StringBuilder();
        string N(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        string E(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {N(map.Metadata.WidthMeters)} {N(map.Metadata.HeightMeters + 160)}'><title>{E(map.Metadata.DisplayName)} - compiled geography</title>");
        for (int z = 0; z < map.Metadata.HeightMeters; z += 32)
        for (int x = 0; x < map.Metadata.WidthMeters; x += 32)
        {
            if (!terrain.TrySampleHeight(x + 16, z + 16, out float height)) throw new InvalidOperationException("Preview sample outside terrain.");
            int shade = Math.Clamp((int)(70 + height * 3), 40, 190);
            svg.Append(CultureInfo.InvariantCulture, $"<rect x='{x}' y='{z}' width='32' height='32' fill='rgb({shade},{shade + 20},{shade - 15})'/>");
        }
        void Area(AxisAlignedBounds b, string color) => svg.Append(CultureInfo.InvariantCulture, $"<rect x='{N(b.Minimum.X)}' y='{N(b.Minimum.Z)}' width='{N(b.Maximum.X - b.Minimum.X)}' height='{N(b.Maximum.Z - b.Minimum.Z)}' fill='{color}' fill-opacity='.18' stroke='{color}' stroke-width='3'/>");
        foreach (var b in map.StaticNavigationObstacles) Area(b, "#f4bd73");
        foreach (var s in map.Sites) Area(s.BuildArea, "#c6d3df");
        foreach (var edge in map.RoadEdges)
        {
            var a = map.RoadNodes.Single(n => n.Key == edge.SourceNodeKey).Position;
            var b = map.RoadNodes.Single(n => n.Key == edge.DestinationNodeKey).Position;
            svg.Append(CultureInfo.InvariantCulture, $"<line x1='{N(a.X)}' y1='{N(a.Z)}' x2='{N(b.X)}' y2='{N(b.Z)}' stroke='#e9d3a0' stroke-width='10'/>");
        }
        foreach (var resource in map.Resources)
        {
            string color = resource.ResourceId.Value switch { 1 => "#c1c8d0", 2 => "#ffc269", _ => "#87d4e8" };
            svg.Append(CultureInfo.InvariantCulture, $"<circle cx='{N(resource.Center.X)}' cy='{N(resource.Center.Z)}' r='22' fill='{color}' stroke='#17202a' stroke-width='4'><title>{E(resource.Key)}: {resource.TotalQuantity.ToString(CultureInfo.InvariantCulture)}</title></circle>");
        }
        foreach (var start in map.Starts)
        {
            string color = start.Player.Value == 1 ? "#6cc3ff" : "#ff8f76";
            Area(start.BuildArea, color);
            svg.Append(CultureInfo.InvariantCulture, $"<circle cx='{N(start.CommandCorePosition.X)}' cy='{N(start.CommandCorePosition.Z)}' r='27' fill='{color}' stroke='white' stroke-width='4'/>");
        }
        foreach (var crossing in map.Crossings)
            svg.Append(CultureInfo.InvariantCulture, $"<text x='{N(crossing.Position.X)}' y='{N(crossing.Position.Z - 35)}' text-anchor='middle' font-family='sans-serif' font-size='36' fill='white'>{E(crossing.DisplayName)}</text>");
        svg.Append(CultureInfo.InvariantCulture, $"<rect y='{N(map.Metadata.HeightMeters)}' width='{N(map.Metadata.WidthMeters)}' height='160' fill='#17202a'/><text x='35' y='{N(map.Metadata.HeightMeters + 60)}' font-family='sans-serif' font-size='38' fill='white'>{E(map.Metadata.DisplayName)} | {N(map.Metadata.WidthMeters)} x {N(map.Metadata.HeightMeters)} m | North at top</text><text x='35' y='{N(map.Metadata.HeightMeters + 115)}' font-family='sans-serif' font-size='28' fill='white'>Core: blue/red | Resources: ore silver, volatiles amber, silicates cyan | Roads: cream | Blockers: amber outlines</text></svg>");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, svg.ToString());
    }
}
