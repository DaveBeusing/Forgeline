using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

public static class AssetPipelineCompiler
{
    public const string CompilerVersion = "1.1.0";
    public const int RuntimeVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = RuntimeAssetCatalog.CreateJsonOptions();

    public static AssetCompilationResult Compile(
        string sourceRoot,
        string runtimeRoot,
        bool clean = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);

        var diagnostics = new List<AssetCompilerDiagnostic>();
        var normalizedSourceRoot = Path.GetFullPath(sourceRoot);
        var normalizedRuntimeRoot = Path.GetFullPath(runtimeRoot);

        if (!Directory.Exists(normalizedSourceRoot))
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET001",
                AssetCompilerDiagnosticSeverity.Error,
                $"Asset source root '{normalizedSourceRoot}' does not exist.",
                SourcePath: normalizedSourceRoot));

            return new AssetCompilationResult(false, 0, 0, diagnostics);
        }

        if (PathsOverlap(normalizedSourceRoot, normalizedRuntimeRoot))
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET002",
                AssetCompilerDiagnosticSeverity.Error,
                "Source and runtime asset roots must not overlap.",
                SourcePath: normalizedSourceRoot));

            return new AssetCompilationResult(false, 0, 0, diagnostics);
        }

        if (clean && Directory.Exists(normalizedRuntimeRoot))
        {
            Directory.Delete(normalizedRuntimeRoot, recursive: true);
        }

        Directory.CreateDirectory(normalizedRuntimeRoot);

        var nodes = DiscoverAssets(normalizedSourceRoot, diagnostics);
        if (HasErrors(diagnostics))
        {
            return new AssetCompilationResult(false, 0, 0, diagnostics);
        }

        ValidateReferences(nodes, diagnostics);
        var orderedNodes = TopologicallyOrder(nodes, diagnostics);
        if (HasErrors(diagnostics))
        {
            return new AssetCompilationResult(false, 0, 0, diagnostics);
        }

        var previousManifest = LoadPreviousManifest(normalizedRuntimeRoot, diagnostics);
        var previousById = previousManifest?.Assets.ToDictionary(
            static record => record.Id,
            StringComparer.Ordinal) ?? new Dictionary<string, RuntimeAssetRecord>(StringComparer.Ordinal);

        var recordsById = new Dictionary<AssetId, RuntimeAssetRecord>();
        var compiledCount = 0;
        var skippedCount = 0;

        foreach (var node in orderedNodes)
        {
            try
            {
                var sourceHash = AssetHashing.ComputeAssetSourceHash(
                    node.DefinitionPath,
                    node.SourcePath,
                    normalizedSourceRoot);

                var dependencyBuildHashes = node.Dependencies
                    .Select(id => recordsById[id].BuildHash)
                    .ToArray();

                var buildHash = AssetHashing.ComputeBuildHash(
                    sourceHash,
                    CompilerVersion,
                    RuntimeVersion,
                    dependencyBuildHashes);

                var runtimePath = GetRuntimePath(node.Id, node.Definition.Type);
                var runtimeFullPath = Path.Combine(
                    normalizedRuntimeRoot,
                    runtimePath.Replace('/', Path.DirectorySeparatorChar));

                var sourceRelativePath = ToRelativePath(normalizedSourceRoot, node.SourcePath);
                if (previousById.TryGetValue(node.Id.Value, out var previous) &&
                    string.Equals(previous.BuildHash, buildHash, StringComparison.Ordinal) &&
                    string.Equals(previous.RuntimePath, runtimePath, StringComparison.Ordinal) &&
                    string.Equals(previous.SourcePath, sourceRelativePath, StringComparison.Ordinal) &&
                    File.Exists(runtimeFullPath))
                {
                    recordsById[node.Id] = previous;
                    skippedCount++;
                    continue;
                }

                var imported = Import(node, normalizedSourceRoot);
                var record = CreateRuntimeRecord(
                    node,
                    normalizedSourceRoot,
                    sourceHash,
                    buildHash,
                    runtimePath,
                    imported.Bounds);
                var metadata = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
                WriteAtomically(runtimeFullPath, node.Definition.Type, metadata, imported.Payload);

                recordsById[node.Id] = record;
                compiledCount++;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                JsonException or
                ArgumentException or
                OverflowException)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET100",
                    AssetCompilerDiagnosticSeverity.Error,
                    exception.Message,
                    node.Id.Value,
                    ToRelativePath(normalizedSourceRoot, node.SourcePath)));

                return new AssetCompilationResult(false, compiledCount, skippedCount, diagnostics);
            }
        }

        var manifest = new RuntimeAssetManifest
        {
            CompilerVersion = CompilerVersion,
            Assets = recordsById
                .OrderBy(static pair => pair.Key.Value, StringComparer.Ordinal)
                .Select(static pair => pair.Value)
                .ToArray(),
        };

        WriteManifestAtomically(normalizedRuntimeRoot, manifest);
        return new AssetCompilationResult(true, compiledCount, skippedCount, diagnostics);
    }

    private static Dictionary<AssetId, AssetNode> DiscoverAssets(
        string sourceRoot,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        var nodes = new Dictionary<AssetId, AssetNode>();

        foreach (var definitionPath in Directory
                     .EnumerateFiles(sourceRoot, "*.asset.json", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            SourceAssetDefinition? definition;
            try
            {
                definition = JsonSerializer.Deserialize<SourceAssetDefinition>(
                    File.ReadAllText(definitionPath),
                    JsonOptions);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET003",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Could not read asset definition: {exception.Message}",
                    SourcePath: ToRelativePath(sourceRoot, definitionPath)));
                continue;
            }

            if (definition is null)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET004",
                    AssetCompilerDiagnosticSeverity.Error,
                    "Asset definition is empty.",
                    SourcePath: ToRelativePath(sourceRoot, definitionPath)));
                continue;
            }

            if (string.IsNullOrWhiteSpace(definition.Source))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET025",
                    AssetCompilerDiagnosticSeverity.Error,
                    "Asset definition must provide a non-empty source path.",
                    definition.Id,
                    ToRelativePath(sourceRoot, definitionPath)));
                continue;
            }

            AssetId id;
            try
            {
                id = AssetId.Parse(definition.Id);
            }
            catch (ArgumentException exception)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET005",
                    AssetCompilerDiagnosticSeverity.Error,
                    exception.Message,
                    definition.Id,
                    ToRelativePath(sourceRoot, definitionPath)));
                continue;
            }

            if (nodes.ContainsKey(id))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET006",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Duplicate stable asset ID '{id}'.",
                    id.Value,
                    ToRelativePath(sourceRoot, definitionPath)));
                continue;
            }

            string sourcePath;
            try
            {
                var definitionDirectory = Path.GetDirectoryName(definitionPath)
                    ?? throw new InvalidDataException("Asset definition directory could not be resolved.");

                sourcePath = AssetHashing.ResolveWithinRoot(
                    sourceRoot,
                    Path.Combine(
                        definitionDirectory,
                        definition.Source.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET007",
                    AssetCompilerDiagnosticSeverity.Error,
                    exception.Message,
                    id.Value,
                    ToRelativePath(sourceRoot, definitionPath)));
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET008",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Source file '{definition.Source}' does not exist.",
                    id.Value,
                    ToRelativePath(sourceRoot, definitionPath)));
                continue;
            }

            ValidateDefinition(definition, id, sourcePath, sourceRoot, diagnostics);
            var dependencies = CollectDependencies(definition, id, sourcePath, sourceRoot, diagnostics);

            nodes[id] = new AssetNode(
                id,
                definition,
                definitionPath,
                sourcePath,
                dependencies);
        }

        return nodes;
    }

    private static void ValidateDefinition(
        SourceAssetDefinition definition,
        AssetId id,
        string sourcePath,
        string sourceRoot,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var validExtension = definition.Type switch
        {
            RuntimeAssetType.Mesh => extension is ".gltf" or ".glb",
            RuntimeAssetType.Texture => extension is ".png" or ".tga",
            RuntimeAssetType.Material => extension == ".json",
            _ => false,
        };

        if (!validExtension)
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET009",
                AssetCompilerDiagnosticSeverity.Error,
                $"Source extension '{extension}' is not valid for asset type '{definition.Type}'.",
                id.Value,
                ToRelativePath(sourceRoot, sourcePath)));
        }

        if (definition.Type == RuntimeAssetType.Texture)
        {
            if (!Enum.IsDefined(definition.TextureUsage))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET026",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Texture usage '{definition.TextureUsage}' is not supported.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (!Enum.IsDefined(definition.TextureColorSpace))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET027",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Texture color space '{definition.TextureColorSpace}' is not supported.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (definition.TextureUsage == RuntimeTextureUsage.BaseColor &&
                definition.TextureColorSpace != RuntimeTextureColorSpace.Srgb)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET028",
                    AssetCompilerDiagnosticSeverity.Error,
                    "Base Color textures require sRGB color space.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (definition.TextureUsage is
                    RuntimeTextureUsage.Normal or
                    RuntimeTextureUsage.Orm or
                    RuntimeTextureUsage.GenericData or
                    RuntimeTextureUsage.TerrainControl &&
                definition.TextureColorSpace != RuntimeTextureColorSpace.Linear)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET029",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Texture usage '{definition.TextureUsage}' requires linear color space.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (definition.TextureMaxMipLevels is int maxMipLevels &&
                (maxMipLevels <= 0 || maxMipLevels > 32))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET030",
                    AssetCompilerDiagnosticSeverity.Error,
                    "textureMaxMipLevels must be between 1 and 32.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (!definition.TextureGenerateMipmaps &&
                definition.TextureMaxMipLevels is > 1)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET031",
                    AssetCompilerDiagnosticSeverity.Error,
                    "textureMaxMipLevels cannot exceed 1 when textureGenerateMipmaps is false.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }
        }

        if (!float.IsFinite(definition.Scale) || MathF.Abs(definition.Scale - 1f) > 0.0001f)
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET010",
                AssetCompilerDiagnosticSeverity.Error,
                "Asset scale must be 1.0; source content must use one engine unit per meter.",
                id.Value,
                ToRelativePath(sourceRoot, sourcePath)));
        }

        if (definition.CollisionRequired && string.IsNullOrWhiteSpace(definition.CollisionReference))
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET011",
                AssetCompilerDiagnosticSeverity.Error,
                "Asset requires collision but no collisionReference is defined.",
                id.Value,
                ToRelativePath(sourceRoot, sourcePath)));
        }

        if (definition.AnimationReferences.Count > 0)
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET012",
                AssetCompilerDiagnosticSeverity.Error,
                "Animation asset references are not supported by the current runtime asset baseline.",
                id.Value,
                ToRelativePath(sourceRoot, sourcePath)));
        }

        var socketNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var socket in definition.Sockets)
        {
            if (!socket.IsValid || !socketNames.Add(socket.Name))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET013",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Socket '{socket.Name}' is invalid or duplicated.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }
        }

        var lodLevels = new HashSet<int>();
        var lastDistance = 0f;
        foreach (var lod in definition.Lods.OrderBy(static item => item.Level))
        {
            if (lod.Level <= 0 ||
                !lodLevels.Add(lod.Level) ||
                !float.IsFinite(lod.MaxDistance) ||
                lod.MaxDistance <= lastDistance ||
                !AssetId.TryParse(lod.AssetId, out _))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET014",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"LOD reference level {lod.Level} is malformed. Levels must be unique positive integers with increasing positive maxDistance values and valid asset IDs.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            lastDistance = lod.MaxDistance;
        }
    }

    private static AssetId[] CollectDependencies(
        SourceAssetDefinition definition,
        AssetId ownerId,
        string sourcePath,
        string sourceRoot,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        var rawDependencies = new List<string>();
        rawDependencies.AddRange(definition.Dependencies);
        rawDependencies.AddRange(definition.MaterialReferences);
        rawDependencies.AddRange(definition.TextureReferences);
        rawDependencies.AddRange(definition.Lods.Select(static lod => lod.AssetId));

        if (!string.IsNullOrWhiteSpace(definition.CollisionReference))
        {
            rawDependencies.Add(definition.CollisionReference);
        }

        if (definition.Type == RuntimeAssetType.Material)
        {
            try
            {
                rawDependencies.AddRange(MaterialImporter.ReadDependencies(sourcePath));
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or ArgumentException)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET015",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Material definition is invalid: {exception.Message}",
                    ownerId.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }
        }

        var result = new SortedSet<AssetId>(Comparer<AssetId>.Create(
            static (left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value)));

        foreach (var rawDependency in rawDependencies)
        {
            if (!AssetId.TryParse(rawDependency, out var dependency))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET016",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Referenced asset ID '{rawDependency}' is invalid.",
                    ownerId.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
                continue;
            }

            if (dependency == ownerId)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET017",
                    AssetCompilerDiagnosticSeverity.Error,
                    "Asset cannot depend on itself.",
                    ownerId.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
                continue;
            }

            result.Add(dependency);
        }

        return result.ToArray();
    }

    private static void ValidateReferences(
        IReadOnlyDictionary<AssetId, AssetNode> nodes,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        foreach (var node in nodes.Values)
        {
            foreach (var dependency in node.Dependencies)
            {
                if (!nodes.ContainsKey(dependency))
                {
                    diagnostics.Add(new AssetCompilerDiagnostic(
                        "ASSET018",
                        AssetCompilerDiagnosticSeverity.Error,
                        $"Referenced asset '{dependency}' does not exist.",
                        node.Id.Value,
                        node.Definition.Source));
                }
            }

            ValidateTypedReferences(
                node,
                node.Definition.MaterialReferences,
                RuntimeAssetType.Material,
                "material",
                nodes,
                diagnostics);

            ValidateTypedReferences(
                node,
                node.Definition.TextureReferences,
                RuntimeAssetType.Texture,
                "texture",
                nodes,
                diagnostics);

            if (!string.IsNullOrWhiteSpace(node.Definition.CollisionReference) &&
                AssetId.TryParse(node.Definition.CollisionReference, out var collisionId) &&
                nodes.TryGetValue(collisionId, out var collisionNode) &&
                collisionNode.Definition.Type != RuntimeAssetType.Mesh)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET019",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Collision reference '{collisionId}' must target a mesh asset.",
                    node.Id.Value,
                    node.Definition.Source));
            }

            foreach (var lod in node.Definition.Lods)
            {
                if (AssetId.TryParse(lod.AssetId, out var lodId) &&
                    nodes.TryGetValue(lodId, out var lodNode) &&
                    lodNode.Definition.Type != RuntimeAssetType.Mesh)
                {
                    diagnostics.Add(new AssetCompilerDiagnostic(
                        "ASSET020",
                        AssetCompilerDiagnosticSeverity.Error,
                        $"LOD reference '{lodId}' must target a mesh asset.",
                        node.Id.Value,
                        node.Definition.Source));
                }
            }

            if (node.Definition.Type == RuntimeAssetType.Material)
            {
                IReadOnlyList<string> materialTextures;
                try
                {
                    materialTextures = MaterialImporter.ReadDependencies(node.SourcePath);
                }
                catch (Exception exception) when (
                    exception is IOException or
                    JsonException or
                    InvalidDataException or
                    ArgumentException)
                {
                    continue;
                }

                ValidateTypedReferences(
                    node,
                    materialTextures,
                    RuntimeAssetType.Texture,
                    "material texture",
                    nodes,
                    diagnostics);
            }
        }
    }

    private static void ValidateTypedReferences(
        AssetNode owner,
        IEnumerable<string> references,
        RuntimeAssetType expectedType,
        string referenceKind,
        IReadOnlyDictionary<AssetId, AssetNode> nodes,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        foreach (var rawReference in references)
        {
            if (!AssetId.TryParse(rawReference, out var id) ||
                !nodes.TryGetValue(id, out var referencedNode))
            {
                continue;
            }

            if (referencedNode.Definition.Type != expectedType)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET021",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"{referenceKind} reference '{id}' must target {expectedType}, but targets {referencedNode.Definition.Type}.",
                    owner.Id.Value,
                    owner.Definition.Source));
            }
        }
    }

    private static List<AssetNode> TopologicallyOrder(
        IReadOnlyDictionary<AssetId, AssetNode> nodes,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        var state = new Dictionary<AssetId, int>();
        var ordered = new List<AssetNode>(nodes.Count);
        var stack = new List<AssetId>();

        foreach (var id in nodes.Keys.OrderBy(static id => id.Value, StringComparer.Ordinal))
        {
            if (!Visit(id))
            {
                return [];
            }
        }

        return ordered;

        bool Visit(AssetId id)
        {
            if (state.TryGetValue(id, out var currentState))
            {
                if (currentState == 2)
                {
                    return true;
                }

                if (currentState == 1)
                {
                    var cycleStart = stack.IndexOf(id);
                    var cycle = cycleStart >= 0 ? stack[cycleStart..] : stack;
                    var description = string.Join(" -> ", cycle.Append(id).Select(static item => item.Value));
                    diagnostics.Add(new AssetCompilerDiagnostic(
                        "ASSET022",
                        AssetCompilerDiagnosticSeverity.Error,
                        $"Circular asset dependency detected: {description}.",
                        id.Value));
                    return false;
                }
            }

            state[id] = 1;
            stack.Add(id);

            foreach (var dependency in nodes[id].Dependencies)
            {
                if (nodes.ContainsKey(dependency) && !Visit(dependency))
                {
                    return false;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
            ordered.Add(nodes[id]);
            return true;
        }
    }

    private static ImportedAssetPayload Import(AssetNode node, string sourceRoot) =>
        node.Definition.Type switch
        {
            RuntimeAssetType.Mesh => GltfImporter.Import(node.SourcePath, sourceRoot),
            RuntimeAssetType.Texture => TextureImporter.Import(
                node.SourcePath,
                node.Definition.TextureColorSpace,
                node.Definition.TextureUsage),
            RuntimeAssetType.Material => MaterialImporter.Import(node.SourcePath),
            _ => throw new InvalidDataException($"Asset type '{node.Definition.Type}' is unsupported."),
        };

    private static RuntimeAssetRecord CreateRuntimeRecord(
        AssetNode node,
        string sourceRoot,
        string sourceHash,
        string buildHash,
        string runtimePath,
        AssetBounds? bounds)
    {
        var textureReferences = node.Definition.TextureReferences.AsEnumerable();
        if (node.Definition.Type == RuntimeAssetType.Material)
        {
            textureReferences = textureReferences.Concat(MaterialImporter.ReadDependencies(node.SourcePath));
        }

        return new RuntimeAssetRecord
        {
            Id = node.Id.Value,
            Type = node.Definition.Type,
            SourcePath = ToRelativePath(sourceRoot, node.SourcePath),
            RuntimePath = runtimePath,
            SourceHash = sourceHash,
            BuildHash = buildHash,
            RuntimeVersion = RuntimeVersion,
            CompilerVersion = CompilerVersion,
            Dependencies = node.Dependencies.Select(static id => id.Value).ToArray(),
            MaterialReferences = node.Definition.MaterialReferences
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            TextureReferences = textureReferences
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Lods = node.Definition.Lods
                .OrderBy(static lod => lod.Level)
                .ToArray(),
            CollisionReference = node.Definition.CollisionReference,
            AnimationReferences = [],
            Sockets = node.Definition.Sockets
                .OrderBy(static socket => socket.Name, StringComparer.Ordinal)
                .ToArray(),
            Bounds = bounds,
        };
    }

    private static RuntimeAssetManifest? LoadPreviousManifest(
        string runtimeRoot,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        var manifestPath = Path.Combine(runtimeRoot, RuntimeAssetCatalog.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<RuntimeAssetManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);

            if (manifest is null || manifest.FormatVersion != RuntimeAssetManifest.CurrentFormatVersion)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET023",
                    AssetCompilerDiagnosticSeverity.Warning,
                    "Existing runtime manifest is incompatible and will be replaced.",
                    SourcePath: manifestPath));
                return null;
            }

            return manifest;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET024",
                AssetCompilerDiagnosticSeverity.Warning,
                $"Existing runtime manifest could not be read and will be replaced: {exception.Message}",
                SourcePath: manifestPath));
            return null;
        }
    }

    private static string GetRuntimePath(AssetId id, RuntimeAssetType type)
    {
        var category = type switch
        {
            RuntimeAssetType.Mesh => "meshes",
            RuntimeAssetType.Texture => "textures",
            RuntimeAssetType.Material => "materials",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

        return $"{category}/{id.Value.Replace('.', '/')}.flasset";
    }

    private static void WriteAtomically(
        string finalPath,
        RuntimeAssetType type,
        byte[] metadata,
        byte[] payload)
    {
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidDataException($"Runtime path '{finalPath}' has no directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = finalPath + ".tmp";
        try
        {
            RuntimeAssetFile.Write(temporaryPath, type, metadata, payload);
            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteManifestAtomically(string runtimeRoot, RuntimeAssetManifest manifest)
    {
        var finalPath = Path.Combine(runtimeRoot, RuntimeAssetCatalog.ManifestFileName);
        var temporaryPath = finalPath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, JsonOptions));
            File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool PathsOverlap(string sourceRoot, string runtimeRoot)
    {
        var sourcePrefix = EnsureTrailingSeparator(sourceRoot);
        var runtimePrefix = EnsureTrailingSeparator(runtimeRoot);

        return sourcePrefix.StartsWith(runtimePrefix, StringComparison.OrdinalIgnoreCase) ||
               runtimePrefix.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool HasErrors(IEnumerable<AssetCompilerDiagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == AssetCompilerDiagnosticSeverity.Error);

    private static string ToRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private sealed record AssetNode(
        AssetId Id,
        SourceAssetDefinition Definition,
        string DefinitionPath,
        string SourcePath,
        IReadOnlyList<AssetId> Dependencies);
}
