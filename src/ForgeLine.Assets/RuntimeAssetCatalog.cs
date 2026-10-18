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

    public static RuntimeAssetCatalog Load(string runtimeRoot) =>
        LoadCancellable(runtimeRoot, CancellationToken.None);

    public static RuntimeAssetCatalog LoadCancellable(string runtimeRoot, CancellationToken cancellationToken, bool requireIntegrity = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedRoot = Path.GetFullPath(runtimeRoot);
        var manifestPath = Path.Combine(normalizedRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Runtime asset manifest was not found.", manifestPath);
        }

        var json = File.ReadAllText(manifestPath);
        cancellationToken.ThrowIfCancellationRequested();
        var manifest = JsonSerializer.Deserialize<RuntimeAssetManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("Runtime asset manifest is empty.");

        if (manifest.FormatVersion != RuntimeAssetManifest.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Runtime asset manifest version {manifest.FormatVersion} is not supported.");
        }

        // Legacy manifests remain inspectable by tooling; players and build gates require integrity.
        if (requireIntegrity || manifest.IntegrityVersion != 0 || !string.IsNullOrEmpty(manifest.ContentSignature))
        {
            RuntimeAssetIntegrity.ValidateManifest(manifest);
        }

        var records = new Dictionary<AssetId, RuntimeAssetRecord>();
        foreach (var record in manifest.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.AnimationReferences is null || record.AnimationReferences.Any(reference => !AssetId.TryParse(reference, out _)))
                throw new InvalidDataException("Runtime animation references require an array of valid stable IDs.");
            MechanicalMeshValidation.Validate(record.MechanicalParts, record.Sockets);
            var id = AssetId.Parse(record.Id);
            if (!records.TryAdd(id, record))
            {
                throw new InvalidDataException($"Runtime asset manifest contains duplicate ID '{id}'.");
            }

            _ = ResolveRuntimePath(normalizedRoot, record.RuntimePath);
        }

        foreach (RuntimeAssetRecord record in manifest.Assets)
        {
            if (record.AnimationReferences.Count > (record.Type == RuntimeAssetType.Skin ? 9 : 1) || record.AnimationReferences.Count > 0 && record.Type is not (RuntimeAssetType.Mesh or RuntimeAssetType.Skin))
                throw new InvalidDataException("Mesh/skin clip reference count or owner type is invalid.");
            if (record.SkinReference is { } skinReference && (record.Type != RuntimeAssetType.Mesh || !record.Dependencies.Contains(skinReference, StringComparer.Ordinal) ||
                !records.TryGetValue(AssetId.Parse(skinReference), out var skin) || skin.Type != RuntimeAssetType.Skin))
                throw new InvalidDataException("Skin reference must be an explicit mesh-to-skin dependency.");
            foreach (string reference in record.AnimationReferences)
                if (!record.Dependencies.Contains(reference, StringComparer.Ordinal) ||
                    !records.TryGetValue(AssetId.Parse(reference), out var clip) || clip.Type != RuntimeAssetType.Animation)
                    throw new InvalidDataException($"Mesh '{record.Id}' has an invalid typed clip dependency '{reference}'.");
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

        return RuntimeAssetFile.Read(path, record.Type,
            Manifest.IntegrityVersion == 0 ? null : record.RuntimeBytes, record.RuntimeHash);
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
