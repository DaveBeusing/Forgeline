using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class PresentationBoundsTests
{
    [Fact]
    public void MainBattleTankReadabilityBoundsIncludeWeaponSilhouette()
    {
        RenderInstance instance =
            Unit(
                new Vector3(
                    4.0f,
                    3.0f,
                    7.2f),
                Quaternion.Identity,
                UnitIds.MainBattleTank);

        Vector3 extents =
            PresentationBounds.ResolveLocalHalfExtents(
                instance);

        Assert.Equal(
            2.30f,
            extents.X,
            3);
        Assert.Equal(
            1.80f,
            extents.Y,
            3);
        Assert.Equal(
            4.86f,
            extents.Z,
            3);
    }

    [Fact]
    public void RotatedReadabilityBoundsCoverWorldOrientedSilhouette()
    {
        RenderInstance instance =
            Unit(
                new Vector3(
                    4.0f,
                    3.0f,
                    7.2f),
                Quaternion.CreateFromAxisAngle(
                    Vector3.UnitY,
                    MathF.PI *
                    0.5f),
                UnitIds.MainBattleTank);

        Vector3 extents =
            PresentationBounds.ResolveWorldAxisAlignedHalfExtents(
                instance);

        Assert.Equal(
            4.86f,
            extents.X,
            3);
        Assert.Equal(
            1.80f,
            extents.Y,
            3);
        Assert.Equal(
            2.30f,
            extents.Z,
            3);
    }

    [Fact]
    public void NonUnitBoundsRemainAtAuthoredPresentationScale()
    {
        RenderInstance building =
            new(
                new EntityId(
                    2,
                    1),
                new RenderTransform(
                    new Vector3(
                        10.0f,
                        6.0f,
                        20.0f),
                    Quaternion.Identity,
                    new Vector3(
                        20.0f,
                        12.0f,
                        16.0f)),
                new RenderMeshHandle(
                    1),
                RenderMaterialHandle.Default,
                RenderVisibilityMask.World);

        Vector3 local =
            PresentationBounds.ResolveLocalHalfExtents(
                building);

        Assert.Equal(
            new Vector3(
                10.0f,
                6.0f,
                8.0f),
            local);
        Assert.Equal(
            0.0f,
            PresentationBounds.ResolveGroundPlaneY(
                building),
            3);
    }

    [Fact]
    public void ReadabilityExpansionIsRoleSpecific()
    {
        Vector3 tank =
            UnitPresentationCatalog.ResolveReadabilityBoundsScale(
                UnitIds.MainBattleTank);
        Vector3 artillery =
            UnitPresentationCatalog.ResolveReadabilityBoundsScale(
                UnitIds.MobileArtillery);
        Vector3 scout =
            UnitPresentationCatalog.ResolveReadabilityBoundsScale(
                UnitIds.ScoutVehicle);
        Vector3 supply =
            UnitPresentationCatalog.ResolveReadabilityBoundsScale(
                UnitIds.SupplyTruck);

        Assert.True(
            artillery.Z >
            tank.Z);
        Assert.True(
            scout.Y >
            tank.Y);
        Assert.True(
            tank.Z >
            supply.Z);
    }

    private static RenderInstance Unit(
        Vector3 scale,
        Quaternion rotation,
        UnitId unit) =>
        new(
            new EntityId(
                1,
                1),
            new RenderTransform(
                Vector3.Zero,
                rotation,
                scale),
            new RenderMeshHandle(
                1),
            RenderMaterialHandle.Default,
            RenderVisibilityMask.World,
            UnitFeature:
                new UnitFeaturePresentationMetadata(
                    unit,
                    UnitPresentationDamageState.Intact));
}
