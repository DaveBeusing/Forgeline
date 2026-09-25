using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

public static class PrototypeBattlefieldValidator
{
    private static readonly ResourceId[] BootstrapResources =
    [
        ResourceIds.FerrousOre,
        ResourceIds.Volatiles,
        ResourceIds.Silicates
    ];

    public static void ValidateDefinition(
        PrototypeBattlefieldDefinition definition)
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
                "Prototype battlefield validation failed:" +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors.Select(
                        static error => "- " + error)));
        }
    }

    private static void ValidateMetadata(
        PrototypeBattlefieldDefinition definition,
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
            metadata.WidthMeters < 2_000.0f ||
            metadata.WidthMeters > 4_000.0f ||
            metadata.HeightMeters < 2_000.0f ||
            metadata.HeightMeters > 4_000.0f)
        {
            errors.Add(
                "Prototype dimensions must remain within the 2-4 km vertical-slice envelope.");
        }

        if (metadata.RecommendedPlayers != 2)
        {
            errors.Add(
                "The canonical prototype battlefield requires exactly two opposing players.");
        }
    }

    private static void ValidateStarts(
        PrototypeBattlefieldDefinition definition,
        List<string> errors)
    {
        if (definition.Starts.Count != 2)
        {
            errors.Add(
                "Exactly two start areas are required.");
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
        PrototypeBattlefieldDefinition definition,
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

        for (int startIndex = 0;
             startIndex < definition.Starts.Count;
             startIndex++)
        {
            BattlefieldStartPosition start =
                definition.Starts[startIndex];

            for (int resourceIndex = 0;
                 resourceIndex < BootstrapResources.Length;
                 resourceIndex++)
            {
                ResourceId required =
                    BootstrapResources[resourceIndex];

                bool accessible =
                    definition.Resources.Any(
                        deposit =>
                            !deposit.Contested &&
                            deposit.ResourceId == required &&
                            HorizontalDistance(
                                deposit.Center,
                                start.Position) <= 450.0f);

                if (!accessible)
                {
                    errors.Add(
                        $"Start {start.Player} lacks nearby bootstrap resource {required}.");
                }
            }
        }

        if (!definition.Resources.Any(
                static resource => resource.Contested))
        {
            errors.Add(
                "At least one richer contested resource deposit is required.");
        }
    }

    private static void ValidateSites(
        PrototypeBattlefieldDefinition definition,
        List<string> errors)
    {
        ValidateUniqueKeys(
            definition.Sites.Select(
                static site => site.Key),
            "strategic site",
            errors);

        BattlefieldSiteKind[] required =
        [
            BattlefieldSiteKind.Expansion,
            BattlefieldSiteKind.MiningOutpost,
            BattlefieldSiteKind.ForwardOperatingBase
        ];

        for (int index = 0; index < required.Length; index++)
        {
            BattlefieldSiteKind kind = required[index];
            if (!definition.Sites.Any(
                    site => site.Kind == kind))
            {
                errors.Add(
                    $"Strategic site type '{kind}' is missing.");
            }
        }

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
        PrototypeBattlefieldDefinition definition,
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
        PrototypeBattlefieldDefinition definition,
        List<string> errors)
    {
        if (definition.Crossings.Count < 2)
        {
            errors.Add(
                "At least two strategically distinct crossings are required.");
        }

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

        if (!definition.Crossings.Any(
                static crossing => crossing.Restorable))
        {
            errors.Add(
                "At least one crossing must support authoritative restoration.");
        }
    }

    private static void ValidateObjectives(
        PrototypeBattlefieldDefinition definition,
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
        PrototypeBattlefieldDefinition definition,
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
