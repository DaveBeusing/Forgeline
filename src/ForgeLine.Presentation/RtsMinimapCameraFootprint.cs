using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class RtsMinimapCameraFootprint
{
    internal static bool TryProject(RtsCamera camera, HudRect map, in AxisAlignedBounds bounds,
        int width,int height,Span<Vector2> output)
    {
        if (output.Length < 4) throw new ArgumentException("Four camera footprint corners are required.",nameof(output));
        if (map.IsEmpty || width <= 0 || height <= 0) return false;
        for (int corner=0;corner<4;corner++)
        {
            Vector2 screen = new(corner is 1 or 2 ? width : 0,corner is 2 or 3 ? height : 0);
            if (!camera.TryScreenPointToWorldOnHorizontalPlane(screen,camera.Target.Y,width,height,out var world)) return false;
            Vector2 p = RtsMinimapModelBuilder.NormalizeWorldPosition(world,bounds);
            output[corner] = new(map.X+p.X*map.Width,map.Y+(1-p.Y)*map.Height);
        }
        return true;
    }
}
