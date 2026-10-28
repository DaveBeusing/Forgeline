using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.World;

namespace ForgeLine.MapCompiler;

internal static class ProceduralTerrainCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static int Run(string[] args)
    {
        string output = "artifacts/procedural-terrain.json";
        string? input = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length && !string.IsNullOrWhiteSpace(args[i]) &&
                !args[i].StartsWith("--", StringComparison.Ordinal)
                ? args[i] : throw new ArgumentException("Terrain option requires a value.");
            switch (args[i])
            {
                case "--generate-terrain": break;
                case "--recipe-input": input = Value(); break;
                case "--qualification-output": output = Value(); break;
                default: throw new ArgumentException($"Unknown terrain argument '{args[i]}'.");
            }
        }
        var recipe = new ProceduralTerrainRecipe
        {
            Generation = new() { TerrainProfileId = ProceduralTerrainRecipe.ProfileId,
                Seed = 17, Origin = new(-4, -4), WidthInChunks = 8, HeightInChunks = 8 }
        };
        if (input is not null)
        {
            using var stream = File.OpenRead(input);
            if (stream.Length > 65_536) throw new ArgumentException("Terrain recipe exceeds 64 KiB.");
            recipe = JsonSerializer.Deserialize<ProceduralTerrainRecipe>(stream, JsonOptions)
                ?? throw new ArgumentException("Terrain recipe must be an object.");
        }
        var result = ProceduralTerrainFactory.Create(recipe);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            Recipe = recipe,
            result.Diagnostics,
            Qualification = "Headless heightfield diagnostics; drainage is a topology mask. No water, navigation or graphics acceptance."
        }, JsonOptions));
        Console.WriteLine($"Terrain SHA-256: {result.Diagnostics.TerrainSha256}; chunks={result.Diagnostics.ChunkCount}; seams={result.Diagnostics.SeamMismatchCount}.");
        return result.Diagnostics.SeamMismatchCount == 0 &&
            result.Diagnostics.MaximumSampleGradient <= recipe.MaximumGradient ? 0 : 1;
    }
}
