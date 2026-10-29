using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class ScatterClearanceTests
{
    [Fact]
    public void IndexedQueriesAgreeWithIndependentBruteForceGeometry()
    {
        ScatterArea[] areas = [new(ScatterAreaKind.Rectangle,-90000,-10000,-20000,30000,3000),
            new(ScatterAreaKind.Corridor,-160000,-90000,150000,110000,12000),
            new(ScatterAreaKind.Corridor,64000,-64000,64000,-64000,8000)];
        var index = new ScatterClearanceIndex(areas);
        Assert.Equal(index.Sha256, new ScatterClearanceIndex(areas.Reverse()).Sha256);
        for (int z = -170000; z <= 170000; z += 7193)
        for (int x = -170000; x <= 170000; x += 5197)
        {
            const int radius = 3500;
            bool expected = areas.Any(a => Distance(a, x, z) <= radius + a.PaddingMillimeters);
            Assert.Equal(expected, index.IsBlocked(x,z,radius));
        }
    }

    [Fact]
    public void YardContainmentAndOverlapUseNegativeCellBoundaries()
    {
        var source = new[] { new ScatterArea(ScatterAreaKind.Yard,-128000,-64000,0,64000) };
        var index = new ScatterClearanceIndex(source);
        source[0] = new(ScatterAreaKind.Rectangle,0,0,1,1);
        Assert.True(index.IsYard(-64000,0,16000));
        Assert.False(index.IsYard(-100,0,500));
        Assert.True(index.OverlapsYard(100,0,500));
        Assert.False(index.IsBlocked(-64000,0,500));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.IsBlocked(0,0,16001));
    }

    private static double Distance(ScatterArea a, double x, double z)
    {
        if (a.Kind == ScatterAreaKind.Rectangle)
        { double dx=x-Math.Clamp(x,a.X0,a.X1), dz=z-Math.Clamp(z,a.Z0,a.Z1);return Math.Sqrt(dx*dx+dz*dz); }
        double sx=a.X1-a.X0, sz=a.Z1-a.Z0, length=sx*sx+sz*sz;
        double t=length==0?0:Math.Clamp(((x-a.X0)*sx+(z-a.Z0)*sz)/length,0,1);
        double px=x-(a.X0+t*sx),pz=z-(a.Z0+t*sz);return Math.Sqrt(px*px+pz*pz);
    }
}
