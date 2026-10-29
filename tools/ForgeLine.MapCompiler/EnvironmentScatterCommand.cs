using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.MapCompiler;

internal static class EnvironmentScatterCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        string output = "artifacts/environment-scatter/report.json";
        string? plots = null;
        ulong seed = 17;
        int maximum = 100_000, density = 650;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length && !string.IsNullOrWhiteSpace(args[i]) && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? args[i] : throw new ArgumentException("Scatter option requires a value.");
            switch (args[i])
            {
                case "--scatter-diagnostics": break;
                case "--qualification-output": output = Value(); break;
                case "--reference-plots": plots = Value(); break;
                case "--seed": seed = ulong.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--maximum-instances": maximum = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--density": density = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                default: throw new ArgumentException($"Unknown scatter option '{args[i]}'.");
            }
        }
        var profile = EnvironmentScatterProfile.Temperate(density, maximum);
        var map = CentralDivideBattlefield.Create();
        var terrain = CentralDivideTerrainFactory.Create(map);
        var recipe = new WorldGenerationRecipe { Seed = seed, WidthInChunks = 12, HeightInChunks = 12,
            ClimateProfileId = BiomeFieldProfile.TemperateProfileId };
        var clearances = new BattlefieldScatterClearance(map);
        var generator = new EnvironmentScatterGenerator(recipe, profile, new BiomeField(recipe, terrain), clearances);
        byte[] mapDigest = SHA256.HashData(BattlefieldMapArtifact.Capture(map).Serialize());
        using var terrainHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var chunk in terrain.Chunks)
        for (int z = 0; z < 65; z++) for (int x = 0; x < 65; x++)
        { BinaryPrimitives.WriteSingleLittleEndian(bytes, chunk.Heightfield.GetHeight(x,z)); terrainHash.AppendData(bytes); }
        var source = new WorldCanonicalWriter(); source.WriteBytes(mapDigest); source.WriteBytes(terrainHash.GetHashAndReset());
        var gameplay = WorldGenerationFingerprint.Gameplay(recipe, [new(new(0,0),0x53435301,1,0,source.ToArray())]);
        using var scene = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        scene.AppendData(Convert.FromHexString(gameplay.Sha256));
        var reports = new List<object>();
        var distribution = profile.Species.ToDictionary(s => s.AssetId, _ => 0, StringComparer.Ordinal);
        long instances = 0, blocked = 0;
        for (int z = 0; z < 12; z++) for (int x = 0; x < 12; x++)
        {
            var chunk = generator.GenerateChunk(new(x,z));
            instances += chunk.Instances.Length;
            var decoration = chunk.CreateDecorationFingerprint(gameplay);
            scene.AppendData(Convert.FromHexString(decoration.Sha256));
            foreach (var item in chunk.Instances)
            {
                var species = profile.Species[item.SpeciesIndex];
                distribution[species.AssetId]++;
                if (clearances.IsBlocked(x*256000L+item.LocalXMillimeters,z*256000L+item.LocalZMillimeters,species.RadiusMillimeters)) blocked++;
            }
            reports.Add(new { X=x, Z=z, Count=chunk.Instances.Length, chunk.Sha256, DecorationSha256=decoration.Sha256 });
        }
        string sceneHash = Convert.ToHexStringLower(scene.GetHashAndReset());
        var report = new { Version=1, Recipe=recipe, Profile=profile.Id, profile.DensityPermille,
            profile.MaximumInstancesPerChunk, profile.MaximumTotalInstances, Clearances=clearances.Sha256,
            GameplaySourceSha256=gameplay.Sha256, SceneSha256=sceneHash, InstanceCount=instances,
            BlockedFootprintCount=blocked, Distribution=distribution, Chunks=reports,
            Qualification="Offline decoration over authored terrain; no ECS, renderer, fog or persistence integration." };
        Write(output,JsonSerializer.Serialize(report,JsonOptions));
        if (plots is not null) ReferencePlots(plots,seed);
        Console.WriteLine($"Scatter SHA-256: {sceneHash}; instances={instances}; blocked={blocked}.");
        return blocked == 0 && instances <= maximum ? 0 : 1;
    }

    private static void ReferencePlots(string directory, ulong seed)
    {
        foreach (string name in new[] { "dense-forest", "clearing", "forest-edge", "technical-yard" })
        {
            var recipe = new WorldGenerationRecipe { Seed=seed, WidthInChunks=2,HeightInChunks=2 };
            var profile = EnvironmentScatterProfile.Temperate(name == "clearing" ? 500 : 1000, 10000);
            ScatterArea[] areas = name == "technical-yard" ?
                [new(ScatterAreaKind.Yard,128000,128000,384000,384000),new(ScatterAreaKind.Corridor,256000,128000,256000,512000,12000)] : [];
            var generator = new EnvironmentScatterGenerator(recipe,profile,new ReferenceEnvironment(name),new ScatterClearanceIndex(areas));
            var chunks = new List<EnvironmentScatterChunk>();
            for(int z=0;z<2;z++)for(int x=0;x<2;x++)chunks.Add(generator.GenerateChunk(new(x,z)));
            Write(Path.Combine(directory,name+".svg"),Plot(name,chunks,areas,seed));
        }
    }

    private static string Plot(string name, IReadOnlyList<EnvironmentScatterChunk> chunks, IEnumerable<ScatterArea> areas, ulong seed)
    {
        var text = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1024\" height=\"1140\" viewBox=\"-16000 -36000 544000 604000\"><rect x=\"-16000\" y=\"-36000\" width=\"544000\" height=\"604000\" fill=\"#f7f4ec\"/>");
        text.Append(CultureInfo.InvariantCulture,$"<desc>Reference environment fixture v1; seed={seed}; chunks={string.Join(',',chunks.Select(c=>c.Sha256))}</desc>");
        text.Append(CultureInfo.InvariantCulture,$"<text x=\"0\" y=\"-13000\" font-family=\"sans-serif\" font-size=\"14000\">{name} · 512 m × 512 m</text>");
        foreach(var a in areas)
        {
            if(a.Kind==ScatterAreaKind.Yard)text.Append(CultureInfo.InvariantCulture,$"<rect x=\"{a.X0}\" y=\"{a.Z0}\" width=\"{a.X1-a.X0}\" height=\"{a.Z1-a.Z0}\" fill=\"#e4d2b5\" stroke=\"#98692f\" stroke-width=\"1000\"/>");
            else text.Append(CultureInfo.InvariantCulture,$"<line x1=\"{a.X0}\" y1=\"{a.Z0}\" x2=\"{a.X1}\" y2=\"{a.Z1}\" stroke=\"#d8d4ca\" stroke-width=\"{a.PaddingMillimeters*2}\"/>");
        }
        int count=0;
        foreach(var chunk in chunks)
        {
            foreach(var i in chunk.Instances)
            {
                var species=chunk.Profile.Species[i.SpeciesIndex];
                string color=i.Classification switch { ScatterClassification.Tree=>"#28664d",ScatterClassification.Shrub=>"#7b9c51",
                    ScatterClassification.Grass=>"#bdca8c",ScatterClassification.Rock=>"#77726c",_=>"#b47642" };
                long x=(long)chunk.Coordinate.X*256000+i.LocalXMillimeters,z=(long)chunk.Coordinate.Z*256000+i.LocalZMillimeters;
                text.Append(CultureInfo.InvariantCulture,$"<circle cx=\"{x}\" cy=\"{z}\" r=\"{species.RadiusMillimeters}\" fill=\"{color}\" opacity=\"0.82\"><title>{species.AssetId}; {i.StableId}</title></circle>");
                count++;
            }
        }
        text.Append(CultureInfo.InvariantCulture,$"<text x=\"0\" y=\"540000\" font-family=\"sans-serif\" font-size=\"9000\">{count} instances · footprint circles · immutable fixture v1</text><text x=\"0\" y=\"556000\" font-family=\"sans-serif\" font-size=\"8500\">Headless placement reference; no native graphics acceptance</text></svg>");
        return text.ToString();
    }

    private static void Write(string file,string content)
    { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);File.WriteAllText(file,content); }

    private sealed class ReferenceEnvironment(string scene) : IEnvironmentQuery
    {
        public bool TrySample(long worldXMillimeters,long worldZMillimeters,out EnvironmentSample sample,EnvironmentSurface surface=EnvironmentSurface.Natural)
        {
            int forest=scene=="dense-forest"?65535:scene=="clearing"?0:32767;
            if(scene=="forest-edge")
            {
                long t=Math.Clamp(worldXMillimeters*65535/512000,0,65535);
                forest=(int)(t*t*(3*65535-2*t)/(65535L*65535));
            }
            bool built=scene=="technical-yard" && worldXMillimeters>=128000 && worldXMillimeters<=384000 &&
                worldZMillimeters>=128000 && worldZMillimeters<=384000;
            sample=new(10000,16000,0,40000,(ushort)(built?0:50000),built?EnvironmentSurface.Built:EnvironmentSurface.Natural,
                built?default:new((ushort)forest,(ushort)(65535-forest),0,0,0));return true;
        }
    }
}
