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
            Starts.Length != definition.Starts.Count ||
            Resources.Length != definition.Resources.Count ||
            WorldObjects.Length != definition.WorldObjects.Count ||
            Sites.Length != definition.Sites.Count ||
            RoadNodes.Length != definition.RoadNodes.Count ||
            RoadEdges.Length != definition.RoadEdges.Count ||
            Crossings.Length != definition.Crossings.Count ||
            Objectives.Length != definition.Objectives.Count ||
            StaticNavigationObstacles.Length !=
                definition.StaticNavigationObstacles.Count)
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
            new JsonStringEnumConverter());

        return options;
    }
}
