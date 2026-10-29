using System.Numerics;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class TerrainRoadSegmentTests
{
    [Fact]
    public void NavigationSizedRoadPiecesJoinWithoutRelaxingTheRegionBudget()
    {
        var world=BiomeTerrainMaterialTests.Flat(default(ForgeLine.World.ChunkCoordinate));
        var edges=Enumerable.Range(0,128).Select(i=>(new Vector2(i*2,128),new Vector2((i+1)*2,128))).ToArray();
        var raw=new TerrainInfrastructureMask(edges.Select(e=>TerrainInfrastructureMask.Road(e.Item1,e.Item2,.5f)));
        Assert.Throws<InvalidOperationException>(()=>TerrainVisualProfileResolver.Resolve(
            TerrainVisualProfileResolver.CreateBiomeDefinition(),world,new BiomeTerrainMaterialTests.Field(),raw));
        var joined=TerrainInfrastructureMask.FromRoadSegments(edges,.5f);
        var profile=TerrainVisualProfileResolver.Resolve(TerrainVisualProfileResolver.CreateBiomeDefinition(),world,
            new BiomeTerrainMaterialTests.Field(),joined);
        Assert.Equal(1,profile.CaptureBiomePalette(default)!.InfrastructureRegions);
        var actual=TerrainSplatMapBuilder.Build(world.Chunks[0],profile);
        for(int x=0;x<33;x++)Assert.Equal(255,BiomeTerrainMaterialTests.Decode(actual,x,16)[(int)TerrainMaterialSlot.Gravel]);
        BiomeTerrainMaterialTests.Normalized(actual);
    }

    [Fact]
    public void ReversedSignedAndDuplicateSegmentsHaveTheSameJoinedControls()
    {
        (Vector2 Start,Vector2 End)[] source=[(new(-256,-256),new(-128,-128)),(new(-128,-128),new(0,0)),
            (new(0,0),new(128,128)),(new(-128,-128),new(-128,128))];
        var expected=TerrainInfrastructureMask.FromRoadSegments(source,8);
        var reordered=TerrainInfrastructureMask.FromRoadSegments(source.Reverse().Select(e=>(e.End,e.Start)).Concat(source),8);
        Assert.Equal(expected.Regions.Select(r=>r.Value),reordered.Regions.Select(r=>r.Value));
        Assert.Equal(2,expected.Regions.Length);
    }

    [Fact]
    public void GapsRemainOpenAndInvalidOrOversizedInputsFail()
    {
        var mask=TerrainInfrastructureMask.FromRoadSegments([(new(0,0),new(16,0)),(new(32,0),new(48,0))],4);
        Assert.Equal(2,mask.Regions.Length);
        Assert.Throws<ArgumentException>(()=>TerrainInfrastructureMask.FromRoadSegments(
            Enumerable.Repeat((Vector2.Zero,Vector2.One),4097),4));
        Assert.Throws<ArgumentException>(()=>TerrainInfrastructureMask.FromRoadSegments([(Vector2.Zero,Vector2.Zero)],4));
        Assert.Throws<ArgumentException>(()=>TerrainInfrastructureMask.FromRoadSegments([(new(float.NaN,0),Vector2.One)],4));
    }
}
