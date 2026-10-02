using System.Numerics;
using ForgeLine.Core;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class TerrainLineOfFirePolicyTests
{
    [Fact]
    public void FlatTerrainPreservesLineOfFire()
    {
        var policy =
            new TerrainLineOfFirePolicy(
                new ProfileTerrain(
                    static (_, _) => 0.0f),
                sampleSpacingMeters: 5.0f,
                clearanceMeters: 0.25f);

        Assert.True(
            policy.HasLineOfFire(
                new EntityId(1, 1),
                new EntityId(2, 1),
                new Vector3(0.0f, 3.0f, 0.0f),
                new Vector3(100.0f, 3.0f, 0.0f)));
    }

    [Fact]
    public void InterveningRidgeBlocksLineOfFire()
    {
        var policy =
            new TerrainLineOfFirePolicy(
                new ProfileTerrain(
                    static (x, _) =>
                        x >= 45.0f &&
                        x <= 55.0f
                            ? 8.0f
                            : 0.0f),
                sampleSpacingMeters: 5.0f,
                clearanceMeters: 0.25f);

        Assert.False(
            policy.HasLineOfFire(
                new EntityId(1, 1),
                new EntityId(2, 1),
                new Vector3(0.0f, 3.0f, 0.0f),
                new Vector3(100.0f, 3.0f, 0.0f)));
    }

    [Fact]
    public void ElevatedFiringLineClearsLowerTerrain()
    {
        var policy =
            new TerrainLineOfFirePolicy(
                new ProfileTerrain(
                    static (x, _) =>
                        x >= 45.0f &&
                        x <= 55.0f
                            ? 8.0f
                            : 0.0f),
                sampleSpacingMeters: 5.0f,
                clearanceMeters: 0.25f);

        Assert.True(
            policy.HasLineOfFire(
                new EntityId(1, 1),
                new EntityId(2, 1),
                new Vector3(0.0f, 14.0f, 0.0f),
                new Vector3(100.0f, 14.0f, 0.0f)));
    }

    private sealed class ProfileTerrain : ITerrainQuery
    {
        private readonly Func<float, float, float> _height;

        public ProfileTerrain(
            Func<float, float, float> height)
        {
            _height =
                height ??
                throw new ArgumentNullException(nameof(height));
        }

        public AxisAlignedBounds WorldBounds =>
            new(
                new Vector3(-1_000.0f, -100.0f, -1_000.0f),
                new Vector3(1_000.0f, 100.0f, 1_000.0f));

        public bool TrySampleHeight(
            float worldX,
            float worldZ,
            out float height)
        {
            height =
                _height(
                    worldX,
                    worldZ);
            return true;
        }

        public bool TrySampleNormal(
            float worldX,
            float worldZ,
            out Vector3 normal)
        {
            _ = worldX;
            _ = worldZ;
            normal = Vector3.UnitY;
            return true;
        }
    }
}
