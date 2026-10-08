using ForgeLine.Core;
using ForgeLine.World;

namespace ForgeLine.Game;

public sealed class BattlefieldBuildableAreaQuery : IBuildableAreaQuery
{
    private const float ResourceZonePaddingMeters = 72.0f;

    private readonly BattlefieldDefinition _battlefield;

    public BattlefieldBuildableAreaQuery(
        BattlefieldDefinition battlefield)
    {
        _battlefield =
            battlefield ??
            throw new ArgumentNullException(nameof(battlefield));
    }

    public bool IsBuildable(
        PlayerId issuer,
        in AxisAlignedBounds footprintBounds)
    {
        if (!issuer.IsSpecified)
        {
            return false;
        }

        for (int index = 0;
             index < _battlefield.Starts.Count;
             index++)
        {
            BattlefieldStartPosition start =
                _battlefield.Starts[index];

            if (start.Player == issuer &&
                ContainsXZ(
                    start.BuildArea,
                    footprintBounds))
            {
                return true;
            }
        }

        for (int index = 0;
             index < _battlefield.Sites.Count;
             index++)
        {
            if (ContainsXZ(
                    _battlefield.Sites[index].BuildArea,
                    footprintBounds))
            {
                return true;
            }
        }

        for (int index = 0;
             index < _battlefield.Resources.Count;
             index++)
        {
            BattlefieldResourceDepositDefinition resource =
                _battlefield.Resources[index];
            float halfX =
                resource.HalfExtents.X +
                ResourceZonePaddingMeters;
            float halfZ =
                resource.HalfExtents.Z +
                ResourceZonePaddingMeters;

            if (footprintBounds.Minimum.X >=
                    resource.Center.X - halfX &&
                footprintBounds.Maximum.X <=
                    resource.Center.X + halfX &&
                footprintBounds.Minimum.Z >=
                    resource.Center.Z - halfZ &&
                footprintBounds.Maximum.Z <=
                    resource.Center.Z + halfZ)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsXZ(
        in AxisAlignedBounds area,
        in AxisAlignedBounds footprint) =>
        footprint.Minimum.X >= area.Minimum.X &&
        footprint.Maximum.X <= area.Maximum.X &&
        footprint.Minimum.Z >= area.Minimum.Z &&
        footprint.Maximum.Z <= area.Maximum.Z;
}
