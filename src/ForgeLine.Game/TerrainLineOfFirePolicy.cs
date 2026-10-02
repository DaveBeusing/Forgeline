using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class TerrainLineOfFirePolicy : ILineOfFirePolicy
{
    private readonly ITerrainQuery _terrain;
    private readonly float _sampleSpacingMeters;
    private readonly float _clearanceMeters;

    public TerrainLineOfFirePolicy(
        ITerrainQuery terrain,
        float sampleSpacingMeters = 8.0f,
        float clearanceMeters = 0.35f)
    {
        _terrain =
            terrain ??
            throw new ArgumentNullException(nameof(terrain));

        if (!float.IsFinite(sampleSpacingMeters) ||
            sampleSpacingMeters <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleSpacingMeters));
        }

        if (!float.IsFinite(clearanceMeters) ||
            clearanceMeters < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(clearanceMeters));
        }

        _sampleSpacingMeters = sampleSpacingMeters;
        _clearanceMeters = clearanceMeters;
    }

    public bool HasLineOfFire(
        EntityId source,
        EntityId target,
        Vector3 sourcePosition,
        Vector3 targetPosition)
    {
        if (!source.IsValid ||
            !target.IsValid ||
            !IsFinite(sourcePosition) ||
            !IsFinite(targetPosition))
        {
            return false;
        }

        Vector3 delta =
            targetPosition -
            sourcePosition;
        float horizontalDistance =
            MathF.Sqrt(
                delta.X * delta.X +
                delta.Z * delta.Z);

        if (horizontalDistance <=
            _sampleSpacingMeters)
        {
            return true;
        }

        int sampleCount =
            Math.Max(
                1,
                (int)MathF.Ceiling(
                    horizontalDistance /
                    _sampleSpacingMeters));

        for (int sample = 1;
             sample < sampleCount;
             sample++)
        {
            float t =
                (float)sample /
                sampleCount;
            float x =
                MathF.Lerp(
                    sourcePosition.X,
                    targetPosition.X,
                    t);
            float z =
                MathF.Lerp(
                    sourcePosition.Z,
                    targetPosition.Z,
                    t);

            if (!_terrain.TrySampleHeight(
                    x,
                    z,
                    out float terrainHeight))
            {
                return false;
            }

            float rayHeight =
                MathF.Lerp(
                    sourcePosition.Y,
                    targetPosition.Y,
                    t);

            if (terrainHeight +
                    _clearanceMeters >=
                rayHeight)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFinite(
        Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
