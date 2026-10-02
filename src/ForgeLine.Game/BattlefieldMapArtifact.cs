using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed record BattlefieldMapArtifact(
    int FormatVersion,
    BattlefieldMapMetadata Metadata,
    BattlefieldStartPosition[] Starts,
    BattlefieldResourceDepositDefinition[] Resources,
    BattlefieldWorldObjectDefinition[] WorldObjects,
    BattlefieldSiteDefinition[] Sites,
    BattlefieldRoadNodeDefinition[] RoadNodes,
    BattlefieldRoadEdgeDefinition[] RoadEdges,
    BattlefieldCrossingDefinition[] Crossings,
    BattlefieldObjectiveDefinition[] Objectives,
    AxisAlignedBounds[] StaticNavigationObstacles)
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions =
        CreateSerializerOptions();

    public static BattlefieldMapArtifact Capture(
        PrototypeBattlefieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new BattlefieldMapArtifact(
            CurrentFormatVersion,
            definition.Metadata,
            definition.Starts.ToArray(),
            definition.Resources.ToArray(),
            definition.WorldObjects.ToArray(),
            definition.Sites.ToArray(),
            definition.RoadNodes.ToArray(),
            definition.RoadEdges.ToArray(),
            definition.Crossings.ToArray(),
            definition.Objectives.ToArray(),
            definition.StaticNavigationObstacles.ToArray());
    }

    public byte[] Serialize()
    {
        Validate();

        return JsonSerializer.SerializeToUtf8Bytes(
            this,
            SerializerOptions);
    }

    public static BattlefieldMapArtifact Deserialize(
        ReadOnlySpan<byte> utf8)
    {
        BattlefieldMapArtifact? artifact =
            JsonSerializer.Deserialize<BattlefieldMapArtifact>(
                utf8,
                SerializerOptions);

        if (artifact is null)
        {
            throw new InvalidOperationException(
                "Compiled battlefield map artifact is empty.");
        }

        artifact.Validate();
        return artifact;
    }

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported battlefield map format version {FormatVersion}.");
        }

        if (string.IsNullOrWhiteSpace(Metadata.Key) ||
            string.IsNullOrWhiteSpace(Metadata.DisplayName))
        {
            throw new InvalidOperationException(
                "Compiled battlefield map metadata is invalid.");
        }

        ArgumentNullException.ThrowIfNull(Starts);
        ArgumentNullException.ThrowIfNull(Resources);
        ArgumentNullException.ThrowIfNull(WorldObjects);
        ArgumentNullException.ThrowIfNull(Sites);
        ArgumentNullException.ThrowIfNull(RoadNodes);
        ArgumentNullException.ThrowIfNull(RoadEdges);
        ArgumentNullException.ThrowIfNull(Crossings);
        ArgumentNullException.ThrowIfNull(Objectives);
        ArgumentNullException.ThrowIfNull(StaticNavigationObstacles);
    }

    public void ValidateMatches(
        PrototypeBattlefieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Validate();

        if (Metadata != definition.Metadata ||
            !Starts.SequenceEqual(definition.Starts) ||
            !Resources.SequenceEqual(definition.Resources) ||
            !WorldObjects.SequenceEqual(definition.WorldObjects) ||
            !Sites.SequenceEqual(definition.Sites) ||
            !RoadNodes.SequenceEqual(definition.RoadNodes) ||
            !RoadEdges.SequenceEqual(definition.RoadEdges) ||
            !Crossings.SequenceEqual(definition.Crossings) ||
            !Objectives.SequenceEqual(definition.Objectives) ||
            !StaticNavigationObstacles.SequenceEqual(
                definition.StaticNavigationObstacles))
        {
            throw new InvalidOperationException(
                "Compiled battlefield map artifact does not match the canonical definition.");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
        options.Converters.Add(
            new AxisAlignedBoundsConverter());
        options.Converters.Add(
            new JsonStringEnumConverter());

        return options;
    }

    private sealed class AxisAlignedBoundsConverter
        : JsonConverter<AxisAlignedBounds>
    {
        public override AxisAlignedBounds Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document =
                JsonDocument.ParseValue(
                    ref reader);
            JsonElement root =
                document.RootElement;

            Vector3 minimum =
                ReadVector(
                    root.GetProperty(
                        "minimum"));
            Vector3 maximum =
                ReadVector(
                    root.GetProperty(
                        "maximum"));

            return new AxisAlignedBounds(
                minimum,
                maximum);
        }

        public override void Write(
            Utf8JsonWriter writer,
            AxisAlignedBounds value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WritePropertyName(
                "minimum");
            WriteVector(
                writer,
                value.Minimum);

            writer.WritePropertyName(
                "maximum");
            WriteVector(
                writer,
                value.Maximum);

            writer.WriteEndObject();
        }

        private static Vector3 ReadVector(
            JsonElement element) =>
            new(
                element.GetProperty("x").GetSingle(),
                element.GetProperty("y").GetSingle(),
                element.GetProperty("z").GetSingle());

        private static void WriteVector(
            Utf8JsonWriter writer,
            Vector3 value)
        {
            writer.WriteStartObject();
            writer.WriteNumber(
                "x",
                value.X);
            writer.WriteNumber(
                "y",
                value.Y);
            writer.WriteNumber(
                "z",
                value.Z);
            writer.WriteEndObject();
        }
    }
}
