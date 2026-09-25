using System.Numerics;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class BuildingConstructionDebugVisualization
{
    public static void DrawPreview(
        DebugDraw debugDraw,
        in BuildingPlacementPreview preview,
        Vector4 validColor,
        Vector4 invalidColor)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);

        Vector4 color = preview.IsValid
            ? validColor
            : invalidColor;

        debugDraw.Box(preview.Bounds, color);

        string state = preview.IsValid
            ? "VALID"
            : preview.Failure.ToString();

        debugDraw.Label(
            preview.Bounds.Center +
            new Vector3(0.0f, preview.Bounds.Extents.Y + 2.0f, 0.0f),
            $"{preview.DisplayName} [{state}]",
            color);
    }

    public static void DrawConstructionSites(
        DebugDraw debugDraw,
        BuildingConstructionDebugSnapshot snapshot,
        Vector4 constructionColor,
        Vector4 completedColor,
        int maximumSites = 128,
        int maximumCompleted = 64,
        int maximumLabels = 32)
    {
        ArgumentNullException.ThrowIfNull(debugDraw);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumSites);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCompleted);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLabels);

        int siteCount = Math.Min(snapshot.Sites.Count, maximumSites);
        int labels = 0;

        for (int index = 0; index < siteCount; index++)
        {
            ConstructionSiteReadModel site = snapshot.Sites[index];
            debugDraw.Box(site.Bounds, constructionColor);

            if (labels >= maximumLabels)
            {
                continue;
            }

            debugDraw.Label(
                site.Bounds.Center +
                new Vector3(0.0f, site.Bounds.Extents.Y + 2.0f, 0.0f),
                $"{site.BuildingKey} {site.Progress * 100.0f:F0}% ({site.ProgressTicks}/{site.RequiredTicks})",
                constructionColor);
            labels++;
        }

        int completedCount = Math.Min(
            snapshot.CompletedBuildings.Count,
            maximumCompleted);

        for (int index = 0; index < completedCount; index++)
        {
            CompletedBuildingReadModel building =
                snapshot.CompletedBuildings[index];
            debugDraw.Box(building.Bounds, completedColor);

            if (labels >= maximumLabels)
            {
                continue;
            }

            debugDraw.Label(
                building.Bounds.Center +
                new Vector3(0.0f, building.Bounds.Extents.Y + 2.0f, 0.0f),
                $"{building.BuildingKey} ACTIVE",
                completedColor);
            labels++;
        }
    }
}
