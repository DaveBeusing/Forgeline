using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeLine.Assets;

public sealed class RuntimeAssetCatalog
{
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string runtimeRoot;
    private readonly Dictionary<AssetId, RuntimeAssetRecord> records;

    private RuntimeAssetCatalog(
        string runtimeRoot,
        RuntimeAssetManifest manifest,
        Dictionary<AssetId, RuntimeAssetRecord> records)
    {
        this.runtimeRoot = runtimeRoot;
        Manifest = manifest;
        this.records = records;
    }

    public RuntimeAssetManifest Manifest { get; }

    public IReadOnlyCollection<AssetId> AssetIds => records.Keys;

    public static RuntimeAssetCatalog Load(string runtimeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);

        var normalizedRoot = Path.GetFullPath(runtimeRoot);
        var manifestPath = Path.Combine(normalizedRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Runtime asset manifest was not found.", manifestPath);
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<RuntimeAssetManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Runtime asset manifest is empty.");

        if (manifest.FormatVersion != RuntimeAssetManifest.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Runtime asset manifest version {manifest.FormatVersion} is not supported.");
        }

        var records = new Dictionary<AssetId, RuntimeAssetRecord>();
        foreach (var record in manifest.Assets)
        {
            var id = AssetId.Parse(record.Id);
            if (!records.TryAdd(id, record))
            {
                throw new InvalidDataException($"Runtime asset manifest contains duplicate ID '{id}'.");
            }

            _ = ResolveRuntimePath(normalizedRoot, record.RuntimePath);
        }

        return new RuntimeAssetCatalog(normalizedRoot, manifest, records);
    }

    public bool Contains(AssetId id) => records.ContainsKey(id);

    public bool TryGet(AssetId id, out RuntimeAssetRecord? record) =>
        records.TryGetValue(id, out record);

    public RuntimeAssetRecord Get(AssetId id) =>
        records.TryGetValue(id, out var record)
            ? record
            : throw new KeyNotFoundException($"Runtime asset '{id}' was not found.");

    public RuntimeAssetContent Read(AssetId id)
    {
        var record = Get(id);
        var path = ResolveRuntimePath(runtimeRoot, record.RuntimePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Runtime asset '{id}' is missing.", path);
        }

        return RuntimeAssetFile.Read(path, record.Type);
    }

    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static string ResolveRuntimePath(string runtimeRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Runtime path '{relativePath}' must be a relative path.");
        }

        var fullPath = Path.GetFullPath(
            Path.Combine(runtimeRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var rootPrefix = runtimeRoot.EndsWith(Path.DirectorySeparatorChar)
            ? runtimeRoot
            : runtimeRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Runtime path '{relativePath}' escapes the runtime asset root.");
        }

        return fullPath;
    }
}
