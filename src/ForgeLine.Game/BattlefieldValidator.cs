using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

public static class BattlefieldValidator
{
    public static void ValidateDefinition(
        BattlefieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var errors = new List<string>();

        ValidateMetadata(
            definition,
            errors);
        ValidateStarts(
            definition,
            errors);
        ValidateResources(
            definition,
            errors);
        ValidateWorldObjects(
            definition,
            errors);
        ValidateSites(
            definition,
            errors);
        ValidateRoadTopology(
            definition,
            errors);
        ValidateCrossings(
            definition,
            errors);
        ValidateObjectives(
            definition,
            errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Battlefield validation failed:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors.Select(
                        static error => "- " + error)));
        }
    }

    private static void ValidateMetadata(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        BattlefieldMapMetadata metadata =
            definition.Metadata;

        if (string.IsNullOrWhiteSpace(metadata.Key) ||
            string.IsNullOrWhiteSpace(metadata.DisplayName))
        {
            errors.Add(
                "Map metadata requires stable key and display name.");
        }

        if (!float.IsFinite(metadata.WidthMeters) ||
            !float.IsFinite(metadata.HeightMeters) ||
            metadata.WidthMeters <= 0.0f ||
            metadata.HeightMeters <= 0.0f)
        {
            errors.Add(
                "Battlefield dimensions must be finite and positive.");
        }

        if (metadata.RecommendedPlayers < 2)
        {
            errors.Add(
                "A battlefield requires at least two players.");
        }
    }

    private static void ValidateStarts(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        if (definition.Starts.Count < 2)
        {
            errors.Add(
                "At least two start areas are required.");
            return;
        }

        var players = new HashSet<ulong>();

        for (int index = 0; index < definition.Starts.Count; index++)
        {
            BattlefieldStartPosition start =
                definition.Starts[index];

            if (!start.Player.IsSpecified ||
                !players.Add(start.Player.Value))
            {
                errors.Add(
                    "Start positions require unique valid player IDs.");
            }

            ValidatePoint(
                definition,
                start.Position,
                $"Start {start.Player}",
                errors);
            ValidatePoint(
                definition,
                start.CommandCorePosition,
                $"Command Core {start.Player}",
                errors);

            if (!ContainsXZ(
                    start.BuildArea,
                    start.Position) ||
                !ContainsXZ(
                    start.BuildArea,
                    start.CommandCorePosition))
            {
                errors.Add(
                    $"Start {start.Player} must be inside its buildable area.");
            }
        }
    }

    private static void ValidateResources(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        ValidateUniqueKeys(
            definition.Resources.Select(
                static resource => resource.Key),
            "resource deposit",
            errors);

        for (int index = 0; index < definition.Resources.Count; index++)
        {
            BattlefieldResourceDepositDefinition resource =
                definition.Resources[index];

            ValidatePoint(
                definition,
                resource.Center,
                $"Resource {resource.Key}",
                errors);

            if (!resource.ResourceId.IsSpecified ||
                resource.TotalQuantity <= 0.0 ||
                !double.IsFinite(resource.TotalQuantity) ||
                resource.ExtractionRatePerSecond <= 0.0 ||
                !double.IsFinite(resource.ExtractionRatePerSecond))
            {
                errors.Add(
                    $"Resource '{resource.Key}' has invalid finite-resource data.");
            }
        }

    }

    private static void ValidateWorldObjects(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        ValidateUniqueKeys(
            definition.WorldObjects.Select(
                static worldObject => worldObject.Key),
            "world object",
            errors);

        for (int index = 0; index < definition.WorldObjects.Count; index++)
        {
            BattlefieldWorldObjectDefinition worldObject =
                definition.WorldObjects[index];

            ValidatePoint(
                definition,
                worldObject.Position,
                $"World object {worldObject.Key}",
                errors);

            if (worldObject.Visual == WorldVisualId.None ||
                !Enum.IsDefined(worldObject.Visual) ||
                !Enum.IsDefined(worldObject.Kind) ||
                !float.IsFinite(worldObject.Scale.X) ||
                !float.IsFinite(worldObject.Scale.Y) ||
                !float.IsFinite(worldObject.Scale.Z) ||
                worldObject.Scale.X <= 0.0f ||
                worldObject.Scale.Y <= 0.0f ||
                worldObject.Scale.Z <= 0.0f ||
                !float.IsFinite(worldObject.RotationDegrees))
            {
                errors.Add(
                    $"World object '{worldObject.Key}' has invalid presentation data.");
            }
        }
    }

    private static void ValidateSites(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        ValidateUniqueKeys(
            definition.Sites.Select(
                static site => site.Key),
            "strategic site",
            errors);

        for (int index = 0; index < definition.Sites.Count; index++)
        {
            BattlefieldSiteDefinition site =
                definition.Sites[index];
            ValidatePoint(
                definition,
                site.Position,
                $"Site {site.Key}",
                errors);

            if (!ContainsXZ(
                    site.BuildArea,
                    site.Position))
            {
                errors.Add(
                    $"Site '{site.Key}' must be inside its build area.");
            }
        }
    }

    private static void ValidateRoadTopology(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        ValidateUniqueKeys(
            definition.RoadNodes.Select(
                static node => node.Key),
            "road node",
            errors);
        ValidateUniqueKeys(
            definition.RoadEdges.Select(
                static edge => edge.Key),
            "road edge",
            errors);

        var nodes =
            definition.RoadNodes.ToDictionary(
                static node => node.Key,
                StringComparer.Ordinal);

        for (int index = 0; index < definition.RoadEdges.Count; index++)
        {
            BattlefieldRoadEdgeDefinition edge =
                definition.RoadEdges[index];

            if (!nodes.ContainsKey(edge.SourceNodeKey) ||
                !nodes.ContainsKey(edge.DestinationNodeKey) ||
                string.Equals(
                    edge.SourceNodeKey,
                    edge.DestinationNodeKey,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Road edge '{edge.Key}' references invalid endpoints.");
            }

            if (!double.IsFinite(edge.BaseCost) ||
                edge.BaseCost < 0.0 ||
                !double.IsFinite(edge.CapacityPerSecond) ||
                edge.CapacityPerSecond <= 0.0)
            {
                errors.Add(
                    $"Road edge '{edge.Key}' has invalid routing/capacity values.");
            }
        }
    }

    private static void ValidateCrossings(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        ValidateUniqueKeys(
            definition.Crossings.Select(
                static crossing => crossing.Key),
            "crossing",
            errors);

        var edgeKeys =
            definition.RoadEdges
                .Select(static edge => edge.Key)
                .ToHashSet(StringComparer.Ordinal);

        for (int index = 0; index < definition.Crossings.Count; index++)
        {
            BattlefieldCrossingDefinition crossing =
                definition.Crossings[index];

            ValidatePoint(
                definition,
                crossing.Position,
                $"Crossing {crossing.Key}",
                errors);

            if (!edgeKeys.Contains(
                    crossing.LogisticsEdgeKey))
            {
                errors.Add(
                    $"Crossing '{crossing.Key}' does not reference a real logistics edge.");
            }

            if (crossing.Restorable &&
                crossing.RestorationTicks == 0)
            {
                errors.Add(
                    $"Restorable crossing '{crossing.Key}' requires non-zero restoration ticks.");
            }
        }


    }

    private static void ValidateObjectives(
        BattlefieldDefinition definition,
        List<string> errors)
    {
        if (definition.Objectives.Count !=
            definition.Starts.Count)
        {
            errors.Add(
                "Each start requires one Command Core objective.");
        }

        ValidateUniqueKeys(
            definition.Objectives.Select(
                static objective => objective.Key),
            "objective",
            errors);

        for (int index = 0; index < definition.Objectives.Count; index++)
        {
            BattlefieldObjectiveDefinition objective =
                definition.Objectives[index];

            BattlefieldStartPosition? matchingStart =
                definition.Starts
                    .Cast<BattlefieldStartPosition?>()
                    .FirstOrDefault(
                        start =>
                            start.HasValue &&
                            start.Value.Player ==
                            objective.Owner);

            if (!matchingStart.HasValue ||
                HorizontalDistance(
                    matchingStart.Value.CommandCorePosition,
                    objective.CommandCorePosition) > 0.01f)
            {
                errors.Add(
                    $"Objective '{objective.Key}' does not match its owner's Command Core position.");
            }
        }
    }

    private static void ValidateUniqueKeys(
        IEnumerable<string> keys,
        string label,
        List<string> errors)
    {
        var seen =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (string key in keys)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                !seen.Add(key))
            {
                errors.Add(
                    $"{label} keys must be non-empty and unique.");
            }
        }
    }

    private static void ValidatePoint(
        BattlefieldDefinition definition,
        Vector3 point,
        string label,
        List<string> errors)
    {
        if (!float.IsFinite(point.X) ||
            !float.IsFinite(point.Y) ||
            !float.IsFinite(point.Z) ||
            point.X < 0.0f ||
            point.Z < 0.0f ||
            point.X > definition.Metadata.WidthMeters ||
            point.Z > definition.Metadata.HeightMeters)
        {
            errors.Add(
                $"{label} lies outside map bounds.");
        }
    }

    private static bool ContainsXZ(
        ForgeLine.World.AxisAlignedBounds bounds,
        Vector3 point) =>
        point.X >= bounds.Minimum.X &&
        point.X <= bounds.Maximum.X &&
        point.Z >= bounds.Minimum.Z &&
        point.Z <= bounds.Maximum.Z;

    private static float HorizontalDistance(
        Vector3 left,
        Vector3 right)
    {
        Vector2 delta =
            new(
                left.X - right.X,
                left.Z - right.Z);
        return delta.Length();
    }
}
