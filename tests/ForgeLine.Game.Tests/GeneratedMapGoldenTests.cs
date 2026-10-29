using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class GeneratedMapGoldenTests
{
    public static IEnumerable<object?[]> Cases()
    {
        using var document=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures","procedural-map-seeds.json")));
        Assert.Equal(1,document.RootElement.GetProperty("SchemaVersion").GetInt32());
        foreach(var item in document.RootElement.GetProperty("Cases").EnumerateArray())
            yield return [item.GetProperty("Id").GetString()!,item.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void VersionedSeedVectorsRemainExactAcrossIndependentCompiles(string id,string json)
    {
        Assert.False(string.IsNullOrEmpty(id));
        using var document=JsonDocument.Parse(json);
        var item=document.RootElement;var original=new GeneratedMapRecipe();
        var recipe=original with {Terrain=original.Terrain with
        {
            BaseHeightMillimeters=item.GetProperty("BaseHeightMillimeters").GetInt32(),
            Generation=original.Terrain.Generation with
            {
                Seed=ulong.Parse(item.GetProperty("Seed").GetString()!,CultureInfo.InvariantCulture),
                WidthInChunks=item.GetProperty("Side").GetInt32(),HeightInChunks=item.GetProperty("Side").GetInt32()
            }
        }};
        byte[]? first=null;string? firstError=null;
        for(int repeat=0;repeat<2;repeat++)
        {
            if(item.GetProperty("ExpectedOutcome").GetString()=="rejected")
            {
                var error=Assert.Throws<InvalidDataException>(()=>GeneratedMapCompiler.Compile(recipe,new string('a',64),TestContext.Current.CancellationToken));
                string hash=Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(error.Message.Trim().Replace("\r\n","\n",StringComparison.Ordinal))));
                Assert.Equal(item.GetProperty("ErrorSha256").GetString(),hash);
                if(firstError is not null)Assert.Equal(firstError,error.Message);
                firstError=error.Message;
                continue;
            }
            var map=GeneratedMapCompiler.Compile(recipe,new string('a',64),TestContext.Current.CancellationToken);
            var actual=new Dictionary<string,string>
            {
                ["Manifest"]=map.ManifestSha256,["Gameplay"]=map.Manifest.GameplaySha256,["Visual"]=map.Manifest.VisualSha256,
                ["Terrain"]=map.Manifest.Terrain.Sha256,["Geography"]=map.Manifest.Geography.Sha256,
                ["Biome"]=map.Manifest.BiomeSha256,["Decoration"]=map.Manifest.DecorationSha256
            };
            foreach(var expected in item.GetProperty("Fingerprints").EnumerateObject())Assert.Equal(expected.Value.GetString(),actual[expected.Name]);
            Assert.Equal(0,map.Audit.ErrorCount);Assert.Equal(0,map.Audit.WarningCount);
            Assert.True(map.Geography.ReachabilityChecks>0);
            var buildable=new BattlefieldBuildableAreaQuery(map.Definition);
            foreach(var start in map.Definition.Starts)
            {
                var center=(start.BuildArea.Minimum+start.BuildArea.Maximum)*.5f;
                Assert.True(buildable.IsBuildable(start.Player,new(center-new System.Numerics.Vector3(1,0,1),center+new System.Numerics.Vector3(1,0,1))));
            }
            if(first is not null)Assert.Equal(first,map.GetManifestBytes());
            first=map.GetManifestBytes();
        }
    }

    [Fact]
    public void CompletePackageVersionOneExplicitlyRejectsNegativeOrigins()
    {
        var original=new GeneratedMapRecipe();
        foreach(var origin in new[]{new ChunkCoordinate(-1,0),new(0,-1)})
            Assert.Throws<InvalidDataException>(()=>(original with {Terrain=original.Terrain with
                {Generation=original.Terrain.Generation with {Origin=origin}}}).Validate());
    }
}
