using System.Numerics;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class BattlefieldScatterClearanceTests
{
    [Fact]
    public void AuthoredGameplayAreasAndAccessSnapshotsRemainClear()
    {
        var map = CentralDivideBattlefield.Create();
        ScatterAccessReservation[] access = [
            new(ScatterAccessKind.Dock, Area(700,700)),
            new(ScatterAccessKind.ConveyorPort, Area(800,700)),
            new(ScatterAccessKind.ConstructionApproach, Area(900,700))];
        var index = new BattlefieldScatterClearance(map, access:access);
        foreach (var item in map.Starts) Assert.True(index.IsBlocked(Mm(item.CommandCorePosition.X),Mm(item.CommandCorePosition.Z),1000));
        foreach (var item in map.Sites) Assert.True(index.IsBlocked(Mm(item.Position.X),Mm(item.Position.Z),1000));
        foreach (var item in map.Resources) Assert.True(index.IsBlocked(Mm(item.Center.X),Mm(item.Center.Z),1000));
        foreach (var item in map.Crossings) Assert.True(index.IsBlocked(Mm(item.Position.X),Mm(item.Position.Z),1000));
        foreach (var item in access) Assert.True(index.IsBlocked(Mm(item.Bounds.Center.X),Mm(item.Bounds.Center.Z),1000));
        var nodes = map.RoadNodes.ToDictionary(n=>n.Key,n=>n.Position,StringComparer.Ordinal);
        foreach (var road in map.RoadEdges)
        {
            var middle=(nodes[road.SourceNodeKey]+nodes[road.DestinationNodeKey])/2;
            Assert.True(index.IsBlocked(Mm(middle.X),Mm(middle.Z),1000));
        }
        Assert.True(index.IsBlocked(-1,10,0));
        Assert.True(index.IsBlocked(Mm(map.Metadata.WidthMeters),1000,0));
        Assert.Equal(index.Sha256,new BattlefieldScatterClearance(map,access:access.Reverse()).Sha256);
        access[0]=new(ScatterAccessKind.Dock,Area(1000,1000));
        Assert.True(index.IsBlocked(700000,700000,1000));
    }

    [Fact]
    public void RealChunkScatterAvoidsAllAuthoredRoadsResourcesAndConstructionAreas()
    {
        var map = CentralDivideBattlefield.Create();
        var terrain = CentralDivideTerrainFactory.Create(map);
        var recipe = new WorldGenerationRecipe { Seed=17, WidthInChunks=12,HeightInChunks=12,
            ClimateProfileId=BiomeFieldProfile.TemperateProfileId };
        var clearances = new BattlefieldScatterClearance(map);
        var profile = EnvironmentScatterProfile.Temperate();
        var generator = new EnvironmentScatterGenerator(recipe,profile,new BiomeField(recipe,terrain),clearances);
        int count=0;
        foreach (var coordinate in new ChunkCoordinate[] {new(0,0),new(3,2),new(5,5),new(9,8)})
        foreach (var item in generator.GenerateChunk(coordinate).Instances)
        {
            long x=(long)coordinate.X*256000+item.LocalXMillimeters,z=(long)coordinate.Z*256000+item.LocalZMillimeters;
            Assert.False(clearances.IsBlocked(x,z,profile.Species[item.SpeciesIndex].RadiusMillimeters));
            count++;
        }
        Assert.True(count>0);
        Assert.Equal(map.WorldObjects.Count,CentralDivideBattlefield.Create().WorldObjects.Count);
        Assert.Equal(map.StaticNavigationObstacles.ToArray(),CentralDivideBattlefield.Create().StaticNavigationObstacles.ToArray());
    }

    [Fact]
    public void StrategicReservationRasterExcludesEveryOverlappedCell()
    {
        var recipe = new WorldGenerationRecipe { Seed=17, WidthInChunks=12,HeightInChunks=12,
            GeographyProfileId=ProceduralStrategicLayoutGenerator.GeographyProfile,
            RulesProfileId=ProceduralStrategicLayoutGenerator.RulesProfile };
        var settings = new WorldGridSettings();
        var chunks = new List<TerrainChunk>();
        for(int z=0;z<12;z++)for(int x=0;x<12;x++)
        {
            var heights=new float[65*65];
            for(int sz=0;sz<65;sz++)for(int sx=0;sx<65;sx++)heights[sz*65+sx]=(x*256+sx*4)*.01f;
            chunks.Add(new(new(x,z),new TerrainHeightfield(65,256,heights)));
        }
        var terrain = new TerrainWorld(settings,chunks);
        var result = ProceduralStrategicLayoutGenerator.Generate(terrain,recipe,cancellationToken:TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded);
        var layout = result.Layout!;
        var index = BattlefieldScatterClearance.FromLayout(layout);
        int tested=0;
        for(int z=0;z<layout.Reservations.Height;z+=5)
        for(int x=0;x<layout.Reservations.Width;x+=5)
            if(layout.Reservations.GetCell(x,z)!=StrategicReservation.None)
            { Assert.True(index.IsBlocked(x*16000L+8000,z*16000L+8000,900));tested++; }
        Assert.True(tested>0);
    }

    private static AxisAlignedBounds Area(float x,float z)=>new(new(x-2,-1,z-2),new(x+2,3,z+2));
    private static long Mm(float value)=>(long)Math.Round(value*1000.0,MidpointRounding.ToEven);
}
