namespace ForgeLine.Navigation.Regional;

/// <summary>Canonical input mapping only; page-backed public snapshot output remains unconnected.</summary>
internal static class RegionalContractAdapter
{
    public static bool TryAdapt(in NavigationObstacleChange change, NavigationObstacleKind kind,
        out RegionalObstacleEdit edit)
    {
        edit = default;
        if (!change.SourceRevision.IsValid || change.Identity.Generation == 0) return false;
        RegionalObstacleLayer layer;
        switch (change.Identity.Source)
        {
            case NavigationObstacleSource.AuthoredGeometry when kind == NavigationObstacleKind.Structure:
                layer = RegionalObstacleLayer.Authored;
                break;
            case NavigationObstacleSource.Entity when kind == NavigationObstacleKind.Structure:
                layer = RegionalObstacleLayer.Structure;
                break;
            case NavigationObstacleSource.Entity when kind == NavigationObstacleKind.Wreck:
                layer = RegionalObstacleLayer.PhysicalWreck;
                break;
            default:
                return false;
        }
        var key = new RegionalObstacleKey((byte)change.Identity.Source, change.Identity.Value, change.Identity.Generation);
        edit = new RegionalObstacleEdit(key, change.SourceRevision, layer, change.PreviousBounds, change.CurrentBounds);
        return true;
    }

    public static bool TryGetSweptRadius(in NavigationVehicleProfile profile, out float radius)
    {
        radius = default;
        if (!profile.IsValid) return false;
        // A circumscribed radius covers translation and any intervening rotation,
        // including terrain pitch. Profile.ClearanceMeters alone is only a margin.
        double x = profile.HalfExtents.X;
        double y = profile.HalfExtents.Y;
        double z = profile.HalfExtents.Z;
        double fullRadius = Math.Sqrt(x * x + y * y + z * z) + profile.ClearanceMeters;
        float rounded = (float)fullRadius;
        radius = MathF.BitIncrement(rounded);
        return float.IsFinite(radius) && radius > 0;
    }
}
