using System.Globalization;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Game;

namespace ForgeLine.MapCompiler;

internal static class GeneratedMapCommand
{
    private static readonly JsonSerializerOptions RecipeOptions = new()
    { IncludeFields = true, MaxDepth = 24, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    private static readonly JsonSerializerOptions ReportOptions = new() { WriteIndented = true };

    internal static int Run(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool generate = args.Contains("--generate-map", StringComparer.Ordinal);
        var allowed = new HashSet<string>(["--load-generated-map", "--output", "--seed", "--width-chunks", "--height-chunks",
            "--recipe", "--asset-sha256", "--asset-root", "--expected-manifest-sha256", "--preview-output", "--qualification-output",
            "--scatter-density", "--scatter-limit", "--material-profile", "--scatter-profile"], StringComparer.Ordinal);
        bool seenGenerate = false;
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key == "--generate-map")
            {
                if (seenGenerate) throw new ArgumentException("Duplicate --generate-map.");
                seenGenerate = true; continue;
            }
            if (!allowed.Contains(key) || ++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(key, args[i]))
                throw new ArgumentException("Unknown, duplicate or missing generated-map option: " + key);
        }
        if (generate == values.ContainsKey("--load-generated-map"))
            throw new ArgumentException("Choose --generate-map or --load-generated-map.");
        string? packageDirectory = values.GetValueOrDefault(generate ? "--output" : "--load-generated-map");
        if (packageDirectory is null) throw new ArgumentException("Generated-map output or input directory is required.");
        var reserved = new HashSet<string>(new[] { GeneratedMapPackage.ManifestFileName, GeneratedMapPackage.GeographyFileName,
            GeneratedMapPackage.TerrainFileName }.Select(name => Path.GetFullPath(Path.Combine(packageDirectory, name))), StringComparer.OrdinalIgnoreCase);
        if (values.TryGetValue("--recipe", out string? recipeInput)) reserved.Add(Path.GetFullPath(recipeInput));
        foreach (string key in new[] { "--preview-output", "--qualification-output" })
            if (values.TryGetValue(key, out string? destination) && !reserved.Add(Path.GetFullPath(destination)))
                throw new ArgumentException("Diagnostic outputs must be distinct from package files, recipe input and each other.");
        string asset = values.GetValueOrDefault("--asset-sha256") ?? string.Empty;
        if (values.TryGetValue("--asset-root", out string? assetRoot))
        {
            if (asset.Length != 0) throw new ArgumentException("Choose --asset-root or --asset-sha256.");
            var catalog = RuntimeAssetCatalog.LoadCancellable(assetRoot, CancellationToken.None, requireIntegrity: true);
            asset = catalog.Manifest.ContentSignature;
        }
        if (asset.Length == 0) throw new ArgumentException("A host asset identity is required: --asset-root or --asset-sha256.");
        GeneratedMapPackage package;
        if (generate)
        {
            if (!values.TryGetValue("--output", out string? output)) throw new ArgumentException("--generate-map requires --output directory.");
            if (values.ContainsKey("--expected-manifest-sha256")) throw new ArgumentException("Expected manifest identity is a load option.");
            var recipe = new GeneratedMapRecipe();
            if (values.TryGetValue("--recipe", out string? path))
            {
                if (values.Keys.Any(k => k is "--seed" or "--width-chunks" or "--height-chunks" or "--scatter-density" or "--scatter-limit" or "--material-profile" or "--scatter-profile"))
                    throw new ArgumentException("Recipe input cannot be combined with recipe overrides.");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length is < 1 or > GeneratedMapPackage.MaximumManifestBytes) throw new InvalidDataException("Recipe exceeds its input budget.");
                var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes);
                recipe = JsonSerializer.Deserialize<GeneratedMapRecipe>(bytes, RecipeOptions)
                    ?? throw new InvalidDataException("Missing generated map recipe.");
            }
            else
            {
                var grid = recipe.Terrain.Generation;
                recipe = recipe with
                {
                    Terrain = recipe.Terrain with { Generation = grid with
                    {
                        Seed = values.TryGetValue("--seed", out string? seed) ? ulong.Parse(seed, NumberStyles.None, CultureInfo.InvariantCulture) : grid.Seed,
                        WidthInChunks = Integer("--width-chunks", grid.WidthInChunks), HeightInChunks = Integer("--height-chunks", grid.HeightInChunks)
                    } },
                    ScatterDensityPermille = Integer("--scatter-density", recipe.ScatterDensityPermille),
                    MaximumScatterInstances = Integer("--scatter-limit", recipe.MaximumScatterInstances),
                    MaterialProfileId = values.GetValueOrDefault("--material-profile", recipe.MaterialProfileId),
                    ScatterProfileId = values.GetValueOrDefault("--scatter-profile", recipe.ScatterProfileId)
                };
            }
            package = GeneratedMapCompiler.Compile(recipe, asset);
            package.Write(output);
        }
        else
        {
            if (values.Keys.Any(k => k is not ("--load-generated-map" or "--asset-root" or "--asset-sha256" or "--expected-manifest-sha256" or "--preview-output" or "--qualification-output")))
                throw new ArgumentException("Recipe and output options cannot be used when loading a generated map.");
            package = GeneratedMapPackage.Load(values["--load-generated-map"], asset, values.GetValueOrDefault("--expected-manifest-sha256"));
        }
        if (values.TryGetValue("--preview-output", out string? preview))
            BattlefieldMapPreview.Write(preview, package.Definition, package.CreateTerrain(), package.Audit);
        if (values.TryGetValue("--qualification-output", out string? qualification))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(qualification))!);
            File.WriteAllText(qualification, JsonSerializer.Serialize(new
            {
                Source = "generated", package.CompositionKey, package.ManifestSha256, package.Manifest,
                package.Geography, package.Audit, GraphicsAcceptance = "Headless qualification only"
            }, ReportOptions));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { package.CompositionKey, package.ManifestSha256, package.Manifest }));
        return 0;

        int Integer(string key, int fallback) => values.TryGetValue(key, out string? text)
            ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : fallback;
    }
}
