using System.Numerics;
using System.Security.Cryptography;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.World.Tests;

public sealed class ProceduralQualificationSchedulingTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(17UL)]
    [InlineData(ulong.MaxValue)]
    public void SignedChunkSchedulingPreservesTerrainBiomeAndScatterBytes(ulong seed)
    {
        var recipe=new ProceduralTerrainRecipe {Generation=new()
        {
            Seed=seed,Origin=new(-2,-1),WidthInChunks=4,HeightInChunks=3,
            TerrainProfileId=ProceduralTerrainRecipe.ProfileId,ClimateProfileId=BiomeFieldProfile.TemperateProfileId
        }};
        var coordinates=Enumerable.Range(-1,3).SelectMany(z=>Enumerable.Range(-2,4).Select(x=>new ChunkCoordinate(x,z))).ToArray();
        string[] reference=Generate(1,false);
        Assert.Equal(reference,Generate(4,true));

        string[] Generate(int workers,bool reverse)
        {
            var generated=new TerrainChunk[coordinates.Length];
            Parallel.For(0,coordinates.Length,new ParallelOptions{MaxDegreeOfParallelism=workers},position=>
            {
                int index=reverse ? coordinates.Length-1-position : position;
                generated[index]=ProceduralTerrainFactory.CreateChunk(recipe,coordinates[index]);
            });
            var terrain=new TerrainWorld(new WorldGridSettings(),generated.Reverse());
            var field=new BiomeField(recipe.Generation,terrain);
            var scatter=new EnvironmentScatterGenerator(recipe.Generation,EnvironmentScatterProfile.Temperate(maximumTotalInstances:2000),
                field,new ScatterClearanceIndex([]));
            var hashes=new string[coordinates.Length];
            Parallel.For(0,coordinates.Length,new ParallelOptions{MaxDegreeOfParallelism=workers},position=>
            {
                int index=reverse ? coordinates.Length-1-position : position;
                var chunk=generated[index];var writer=new WorldCanonicalWriter();
                writer.WriteInt32(chunk.Coordinate.X);writer.WriteInt32(chunk.Coordinate.Z);
                for(int z=0;z<65;z++)for(int x=0;x<65;x++)writer.WriteInt32(BitConverter.SingleToInt32Bits(chunk.Heightfield.GetHeight(x,z)));
                writer.WriteBytes(Convert.FromHexString(BiomeFieldRaster.Create(field,chunk.Coordinate).ComputeFingerprint().Sha256));
                writer.WriteBytes(scatter.GenerateChunk(chunk.Coordinate).GetCanonicalBytes());
                hashes[index]=Convert.ToHexStringLower(SHA256.HashData(writer.ToArray()));
            });
            return hashes;
        }
    }

    [Theory]
    [InlineData(-50000)]
    [InlineData(50000)]
    public void StandaloneClimateExtremesRemainFiniteAndNormalized(int temperature)
    {
        var recipe=new ProceduralTerrainRecipe {Generation=new()
        {
            Origin=new(-1,-1),WidthInChunks=2,HeightInChunks=2,TerrainProfileId=ProceduralTerrainRecipe.ProfileId,
            ClimateProfileId=BiomeFieldProfile.TemperateProfileId
        }};
        var terrain=ProceduralTerrainFactory.Create(recipe).Terrain;
        var field=new BiomeField(recipe.Generation,terrain,new(){BaseTemperatureMilliCelsius=temperature,TemperatureAmplitudeMilliCelsius=0});
        for(long z=-250000;z<250000;z+=13777)
        for(long x=-250000;x<250000;x+=17311)
        {
            Assert.True(field.TrySample(x,z,out var sample));
            Assert.InRange(sample.TemperatureMilliCelsius,-50000,50000);
            Assert.Equal(65535,sample.Weights.Sum);
            Assert.InRange(sample.HeightMillimeters,-1000000,1000000);
        }
    }
}
