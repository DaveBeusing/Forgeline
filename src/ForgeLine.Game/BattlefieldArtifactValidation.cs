using System.Numerics;
using System.Text.Json;
using ForgeLine.World;

namespace ForgeLine.Game;

internal static class BattlefieldArtifactValidation
{
    internal static void Preflight(ReadOnlySpan<byte> utf8)
    {
        Require(utf8.Length > 0 && utf8.Length <= BattlefieldMapArtifact.MaximumInputBytes, "Compiled map exceeds the input budget.");
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 24 });
        var arrays = new int[25];
        var properties = new HashSet<string>?[25];
        int tokens = 0;
        while (reader.Read())
        {
            Require(++tokens <= 500_000, "Compiled map exceeds the token budget.");
            int depth = reader.CurrentDepth;
            if (reader.TokenType is not (JsonTokenType.EndArray or JsonTokenType.EndObject or JsonTokenType.PropertyName) && depth > 0 && arrays[depth - 1] > 0)
                Require(++arrays[depth - 1] <= 16_385, "Compiled map array exceeds the collection budget.");
            if (reader.TokenType == JsonTokenType.StartArray) arrays[depth] = 1;
            if (reader.TokenType == JsonTokenType.EndArray) arrays[depth] = 0;
            if (reader.TokenType == JsonTokenType.StartObject) properties[depth] = new(StringComparer.Ordinal);
            if (reader.TokenType == JsonTokenType.EndObject) properties[depth] = null;
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName)
                Require(reader.ValueSpan.Length <= 2048, "Compiled map string exceeds the input budget.");
            if (reader.TokenType == JsonTokenType.PropertyName)
                Require(properties[depth - 1]!.Add(reader.GetString()!), "Duplicate compiled map property.");
            if (reader.TokenType == JsonTokenType.Number)
                Require(reader.TryGetDouble(out double value) && double.IsFinite(value), "Compiled map number must be finite.");
        }
    }

    internal static void Validate(BattlefieldMapArtifact map)
    {
        Require(map.MapRevision > 0 && map.ContentKey == ScenarioContentIdentity.DirectorateKey &&
            map.ContentSha256 is { Length: 64 } && map.ContentSha256.All(Uri.IsHexDigit) &&
            map.StartRule == ScenarioContentIdentity.StartRule && map.ObjectiveRule == ScenarioContentIdentity.ObjectiveRule,
            "Compiled map has an unsupported content or rule identity.");
        Require(map.Terrain is not null, "Compiled map requires an authoritative terrain recipe.");
        Require(map.Starts.Length is >= 2 and <= 16 && map.Objectives.Length <= 16 &&
            map.Resources.Length <= 4096 && map.WorldObjects.Length <= 16384 && map.Sites.Length <= 256 &&
            map.RoadNodes.Length <= 2048 && map.RoadEdges.Length <= 8192 && map.Crossings.Length <= 128 &&
            map.StaticNavigationObstacles.Length <= 4096, "Compiled map exceeds a collection budget.");
        Require(map.TerrainVisual.ControlSamplesPerSide <= 129 && map.TerrainVisual.MaterialAssetIds.Length <= 16,
            "Compiled map exceeds a terrain visual budget.");
        Require(map.Metadata.RecommendedPlayers == map.Starts.Length && map.Objectives.Select(o => o.Owner).Distinct().Count() == map.Starts.Length,
            "Compiled map must define exactly one objective per start owner.");
        bool Point(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) &&
            Math.Abs(p.Y) <= 100_000 && p.X >= 0 && p.Z >= 0 && p.X <= map.Metadata.WidthMeters && p.Z <= map.Metadata.HeightMeters;
        bool Bounds(AxisAlignedBounds b) => Point(b.Minimum) && Point(b.Maximum) && b.Maximum.X > b.Minimum.X && b.Maximum.Z > b.Minimum.Z;
        Require(map.Starts.All(s => Bounds(s.BuildArea)) && map.Sites.All(s => Bounds(s.BuildArea) && Enum.IsDefined(s.Kind) && !string.IsNullOrWhiteSpace(s.DisplayName)) &&
            map.Crossings.All(c => Bounds(c.PassageBounds) && !string.IsNullOrWhiteSpace(c.DisplayName)) &&
            map.StaticNavigationObstacles.All(Bounds), "Compiled map contains an invalid area.");
        Require(map.RoadNodes.All(n => Point(n.Position)) && map.Objectives.All(o => Point(o.CommandCorePosition)),
            "Compiled map contains an invalid road node or objective position.");
        Require(map.Resources.All(r => float.IsFinite(r.HalfExtents.X) && float.IsFinite(r.HalfExtents.Y) && float.IsFinite(r.HalfExtents.Z) &&
            r.HalfExtents.X > 0 && r.HalfExtents.Y > 0 && r.HalfExtents.Z > 0 &&
            Point(r.Center - new Vector3(r.HalfExtents.X, 0, r.HalfExtents.Z)) && Point(r.Center + new Vector3(r.HalfExtents.X, 0, r.HalfExtents.Z))),
            "Compiled map contains an invalid resource area.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
