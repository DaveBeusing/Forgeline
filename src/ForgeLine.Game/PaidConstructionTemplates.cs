using System.Numerics;

namespace ForgeLine.Game;

/// <summary>Small authored designs; entries express intent, never paid or completed work.</summary>
public static class PaidConstructionTemplates
{
    public const uint MenuBase = 3000;
    public const int BuildingCount = 2;
    public const int LinkCount = 1;
    public static bool IsSupported(int template) => template is 1 or 2;
    public static string Name(int template) => template == 1 ? "STORAGE LINK" : "SMELTER OUTPUT";
    public static BuildingId Building(int template, int index) =>
        index == 0 && template == 2 ? BuildingIds.Smelter : BuildingIds.StorageDepot;
    public static Vector3 Position(Vector3 anchor, BuildingOrientation rotation, int index) =>
        anchor + Vector3.Transform(new Vector3(index * 48, 0, 0), BuildingFootprint.GetRotation(rotation));
    // The one explicit relation is first building OUT -> second building IN.
    public const int Source = 0;
    public const int Destination = 1;
}
