using System.Text.Json;
using ForgeLine.Assets;

namespace ForgeLine.AssetCompiler;

public static class AssetPipelineCompiler
{
    public const string CompilerVersion = "1.13.0";
    public const int RuntimeVersion = RuntimeAssetIntegrity.SupportedRuntimeVersion;

    private static readonly JsonSerializerOptions JsonOptions = RuntimeAssetCatalog.CreateJsonOptions();

    public static AssetCompilationResult Compile(
        string sourceRoot,
        string runtimeRoot,
        bool clean = false,
        Action<AssetCompilationProgress>? progress = null,
        Action? qualifyOutput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);

        using var reporter = new AssetCompilationReporter(progress);
        reporter.Report("compile", "started", detail: $"source={sourceRoot} runtime={runtimeRoot} clean={clean}");
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

        using var cacheLock = reporter.Measure("cache-lock",
            () => AssetCacheLock.Acquire(normalizedRuntimeRoot, TimeSpan.FromMinutes(5)));

        if (clean && Directory.Exists(normalizedRuntimeRoot))
        {
            Directory.Delete(normalizedRuntimeRoot, recursive: true);
        }

        Directory.CreateDirectory(normalizedRuntimeRoot);

        var nodes = reporter.Measure("discovery", () => DiscoverAssets(normalizedSourceRoot, diagnostics));
        if (HasErrors(diagnostics))
        {
            return new AssetCompilationResult(false, 0, 0, diagnostics);
        }

        reporter.Measure("reference-validation", () => { ValidateReferences(nodes, diagnostics); return true; });
        var orderedNodes = reporter.Measure("dependency-order", () => TopologicallyOrder(nodes, diagnostics));
        if (HasErrors(diagnostics))
        {
            return new AssetCompilationResult(false, 0, 0, diagnostics);
        }

        var previousManifest = reporter.Measure("previous-manifest", () => LoadPreviousManifest(normalizedRuntimeRoot, diagnostics));
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
                var sourceHash = reporter.Measure("source-hash", () => AssetHashing.ComputeAssetSourceHash(
                    node.DefinitionPath, node.SourcePath, normalizedSourceRoot, node.Definition.Type is RuntimeAssetType.Animation or RuntimeAssetType.Skin ? 8 * 1024 * 1024 : long.MaxValue),
                    node.Id.Value, node.SourcePath, $"sourceBytes={new FileInfo(node.SourcePath).Length}");

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
                    previous.Type == node.Definition.Type &&
                    previous.CompilerVersion == CompilerVersion && previous.RuntimeVersion == RuntimeVersion &&
                    string.Equals(previous.BuildHash, buildHash, StringComparison.Ordinal) &&
                    string.Equals(previous.RuntimePath, runtimePath, StringComparison.Ordinal) &&
                    string.Equals(previous.SourcePath, sourceRelativePath, StringComparison.Ordinal) &&
                    RuntimeAssetIntegrity.Matches(runtimeFullPath, previous))
                {
                    recordsById[node.Id] = previous;
                    skippedCount++;
                    reporter.Report("asset", "unchanged", id: node.Id.Value, path: node.SourcePath);
                    continue;
                }

                string rebuildReason = previous is null ? "new-or-missing-manifest"
                    : !File.Exists(runtimeFullPath) ? "missing-payload"
                    : !string.Equals(previous.SourceHash, sourceHash, StringComparison.Ordinal) ? "source-changed"
                    : previousManifest!.CompilerVersion != CompilerVersion ? "compiler-changed"
                    : !string.Equals(previous.BuildHash, buildHash, StringComparison.Ordinal) ? "dependency-changed"
                    : "invalid-payload-or-path";
                reporter.Report("asset", "rebuild", id: node.Id.Value, path: node.SourcePath,
                    detail: $"reason={rebuildReason} type={node.Definition.Type} format={node.Definition.TextureFormat} quality={node.Definition.TextureCompressionQuality}");
                var imported = reporter.Measure("import", () => Import(node, normalizedSourceRoot, nodes, recordsById, normalizedRuntimeRoot, reporter), node.Id.Value, node.SourcePath);
                AddCompilationDiagnostic(
                    node,
                    imported,
                    sourceRelativePath,
                    diagnostics);
                var record = CreateRuntimeRecord(
                    node,
                    normalizedSourceRoot,
                    sourceHash,
                    buildHash,
                    runtimePath,
                    imported.Bounds);
                var metadata = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
                reporter.Measure("runtime-write", () => { WriteAtomically(runtimeFullPath, node.Definition.Type, metadata, imported.Payload); return true; },
                    node.Id.Value, runtimeFullPath, $"payloadBytes={imported.Payload.LongLength}");

                recordsById[node.Id] = RuntimeAssetIntegrity.SealRecord(runtimeFullPath, record);
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

        reporter.Measure("manifest-write", () => { WriteManifestAtomically(normalizedRuntimeRoot, RuntimeAssetIntegrity.SealManifest(manifest)); return true; });
        reporter.Report("compile", "completed", detail: $"compiled={compiledCount} unchanged={skippedCount}");
        // Keep build qualification in the same lease: a following clean writer must not
        // delete this generation while the caller is checking its deployment contract.
        qualifyOutput?.Invoke();
        return new AssetCompilationResult(true, compiledCount, skippedCount, diagnostics);
    }

    private static Dictionary<AssetId, AssetNode> DiscoverAssets(
        string sourceRoot,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        var nodes = new Dictionary<AssetId, AssetNode>();
        foreach (string sidecar in Directory.EnumerateFiles(sourceRoot, "*.scatter.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            if (!File.Exists(sidecar[..^".scatter.json".Length] + ".asset.json"))
                diagnostics.Add(new("ASSET052", AssetCompilerDiagnosticSeverity.Error, "Scatter sidecar has no owning asset definition.", SourcePath: ToRelativePath(sourceRoot, sidecar)));

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
            catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
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

            string scatterPath = definitionPath[..^".asset.json".Length] + ".scatter.json";
            if (File.Exists(scatterPath))
            {
                try
                {
                    if (definition.Scatter is not null || new FileInfo(scatterPath).Length > 16384)
                        throw new InvalidDataException("Use one scatter declaration per asset, at most 16 KiB.");
                    definition = definition with { Scatter = JsonSerializer.Deserialize<ScatterAssetMetadata>(File.ReadAllText(scatterPath), JsonOptions)
                        ?? throw new InvalidDataException("Scatter sidecar is null.") };
                }
                catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
                {
                    diagnostics.Add(new("ASSET052", AssetCompilerDiagnosticSeverity.Error, exception.Message, definition.Id, ToRelativePath(sourceRoot, scatterPath)));
                    continue;
                }
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

            if (definition.AnimationReferences is null)
            {
                diagnostics.Add(new("ASSET012", AssetCompilerDiagnosticSeverity.Error,
                    "animationReferences must be an array, not null.", id.Value, ToRelativePath(sourceRoot, definitionPath)));
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
        if (definition.SkinSockets is null || definition.SkinReference is not null && definition.Type != RuntimeAssetType.Mesh ||
            definition.SkeletalAnimation && definition.Type != RuntimeAssetType.Animation || definition.AnimationClip is not null && definition.Type != RuntimeAssetType.Animation ||
            definition.Type != RuntimeAssetType.Skin && definition.SkinSockets.Count > 0)
            diagnostics.Add(new("ASSET051", AssetCompilerDiagnosticSeverity.Error, "Skin/clip metadata is null or attached to an incompatible asset type.", id.Value));
        try { ScatterAssetValidation.Validate(definition.Scatter, definition.Type); }
        catch (InvalidDataException exception)
        {
            diagnostics.Add(new("ASSET052", AssetCompilerDiagnosticSeverity.Error, exception.Message, id.Value));
        }
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var validExtension = definition.Type switch
        {
            RuntimeAssetType.Skin => extension is ".gltf" or ".glb",
            RuntimeAssetType.Animation => extension is ".gltf" or ".glb",
            RuntimeAssetType.Mesh => extension is ".gltf" or ".glb",
            RuntimeAssetType.Texture => extension is ".png" or ".tga",
            RuntimeAssetType.Material => extension == ".json",
            RuntimeAssetType.UiSkin => extension == ".json",
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

            if (definition.TextureUsage is RuntimeTextureUsage.BaseColor or RuntimeTextureUsage.Emissive &&
                definition.TextureColorSpace != RuntimeTextureColorSpace.Srgb)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET028",
                    AssetCompilerDiagnosticSeverity.Error,
                    "Base Color and Emissive textures require sRGB color space.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (definition.TextureUsage is
                    RuntimeTextureUsage.Normal or
                    RuntimeTextureUsage.Orm or
                    RuntimeTextureUsage.GenericData or
                    RuntimeTextureUsage.TerrainControl or
                    RuntimeTextureUsage.DisplayImage &&
                definition.TextureColorSpace != RuntimeTextureColorSpace.Linear)
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET029",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Texture usage '{definition.TextureUsage}' requires linear color space.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (definition.TextureMaxDimension is int maxDimension &&
                (maxDimension <= 0 || maxDimension > 16384))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET034",
                    AssetCompilerDiagnosticSeverity.Error,
                    "textureMaxDimension must be between 1 and 16384.",
                    id.Value,
                    ToRelativePath(sourceRoot, sourcePath)));
            }

            if (!Enum.IsDefined(definition.TextureFormat) ||
                (definition.TextureFormat == RuntimeTextureFormat.Bc7Unorm &&
                 definition.TextureUsage is RuntimeTextureUsage.TerrainControl or RuntimeTextureUsage.GenericData))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET035", AssetCompilerDiagnosticSeverity.Error,
                    $"Texture format {definition.TextureFormat} is invalid for usage {definition.TextureUsage}; control/data maps require RGBA8.",
                    id.Value, ToRelativePath(sourceRoot, sourcePath)));
            }

            if (!Enum.IsDefined(definition.TextureCompressionQuality))
            {
                diagnostics.Add(new AssetCompilerDiagnostic(
                    "ASSET036", AssetCompilerDiagnosticSeverity.Error,
                    $"Texture compression quality {definition.TextureCompressionQuality} is invalid; expected balanced or best.",
                    id.Value, ToRelativePath(sourceRoot, sourcePath)));
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

        if (definition.AnimationReferences.Count > (definition.Type == RuntimeAssetType.Skin ? 9 : 1) || definition.AnimationReferences.Count > 0 && definition.Type is not (RuntimeAssetType.Mesh or RuntimeAssetType.Skin))
        {
            diagnostics.Add(new AssetCompilerDiagnostic(
                "ASSET012",
                AssetCompilerDiagnosticSeverity.Error,
                "Only meshes may reference one optional rigid clip.",
                id.Value,
                ToRelativePath(sourceRoot, sourcePath)));
        }

        try
        {
            if (definition.MechanicalParts.Count > 0 && definition.Type != RuntimeAssetType.Mesh)
                throw new InvalidDataException("Only mesh assets can declare mechanical parts.");
            MechanicalMeshValidation.Validate(definition.MechanicalParts, definition.Sockets);
        }
        catch (Exception exception) when (exception is InvalidDataException or OverflowException)
        {
            diagnostics.Add(new AssetCompilerDiagnostic("ASSET050", AssetCompilerDiagnosticSeverity.Error,
                exception.Message, id.Value, ToRelativePath(sourceRoot, sourcePath)));
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
        if (definition.Scatter is { } scatter && scatter.FamilyId != definition.Id && AssetId.TryParse(scatter.FamilyId, out _))
            rawDependencies.Add(scatter.FamilyId);
        rawDependencies.AddRange(definition.Dependencies);
        if (definition.SkinReference is not null) rawDependencies.Add(definition.SkinReference);
        rawDependencies.AddRange(definition.AnimationReferences);
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

        if (definition.Type == RuntimeAssetType.UiSkin)
        {
            try { rawDependencies.AddRange(UiSkinImporter.Read(sourcePath).Pages.Select(p => p.Id)); }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or ArgumentException)
            { diagnostics.Add(new("ASSET015", AssetCompilerDiagnosticSeverity.Error, e.Message, ownerId.Value)); }
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
            if (node.Definition.SkinReference is not null) ValidateTypedReferences(node, [node.Definition.SkinReference], RuntimeAssetType.Skin, "skin", nodes, diagnostics);
            ValidateTypedReferences(node, node.Definition.AnimationReferences, RuntimeAssetType.Animation, "animation", nodes, diagnostics);
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
                    nodes.TryGetValue(lodId, out var lodNode))
                {
                    if (lodNode.Definition.Type != RuntimeAssetType.Mesh)
                    {
                        diagnostics.Add(new AssetCompilerDiagnostic(
                            "ASSET020",
                            AssetCompilerDiagnosticSeverity.Error,
                            $"LOD reference '{lodId}' must target a mesh asset.",
                            node.Id.Value,
                            node.Definition.Source));
                    }
                    else if (node.Definition.Type == RuntimeAssetType.Mesh &&
                             !node.Definition.MaterialReferences.SequenceEqual(
                                 lodNode.Definition.MaterialReferences,
                                 StringComparer.Ordinal))
                    {
                        diagnostics.Add(new AssetCompilerDiagnostic(
                            "ASSET033",
                            AssetCompilerDiagnosticSeverity.Error,
                            $"LOD reference '{lodId}' changes the mesh material-slot identity. LODs must preserve stable material references and slot order.",
                            node.Id.Value,
                            node.Definition.Source));
                    }
                }
            }

            if (node.Definition.Type == RuntimeAssetType.Material)
            {
                MaterialSourceDefinition material;
                try
                {
                    material =
                        MaterialImporter.ReadDefinition(
                            node.SourcePath);
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
                    material.ReferencedTextureIds,
                    RuntimeAssetType.Texture,
                    "material texture",
                    nodes,
                    diagnostics);
                ValidateMaterialTextureUsage(
                    node,
                    material.BaseColorTexture,
                    RuntimeTextureUsage.BaseColor,
                    "baseColorTexture",
                    nodes,
                    diagnostics);
                ValidateMaterialTextureUsage(
                    node,
                    material.NormalTexture,
                    RuntimeTextureUsage.Normal,
                    "normalTexture",
                    nodes,
                    diagnostics);
                ValidateMaterialTextureUsage(
                    node,
                    material.OrmTexture,
                    RuntimeTextureUsage.Orm,
                    "ormTexture",
                    nodes,
                    diagnostics);
                ValidateMaterialTextureUsage(
                    node,
                    material.EmissiveTexture,
                    RuntimeTextureUsage.Emissive,
                    "emissiveTexture",
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

    private static void ValidateMaterialTextureUsage(
        AssetNode owner,
        string? rawReference,
        RuntimeTextureUsage expectedUsage,
        string slotName,
        IReadOnlyDictionary<AssetId, AssetNode> nodes,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(
                rawReference) ||
            !AssetId.TryParse(
                rawReference,
                out AssetId id) ||
            !nodes.TryGetValue(
                id,
                out AssetNode? referencedNode) ||
            referencedNode is null ||
            referencedNode.Definition.Type !=
            RuntimeAssetType.Texture)
        {
            return;
        }

        if (referencedNode.Definition.TextureUsage !=
            expectedUsage)
        {
            diagnostics.Add(
                new AssetCompilerDiagnostic(
                    "ASSET032",
                    AssetCompilerDiagnosticSeverity.Error,
                    $"Material slot '{slotName}' requires texture usage '{TextureUsageName(expectedUsage)}', but '{id}' declares '{TextureUsageName(referencedNode.Definition.TextureUsage)}'.",
                    owner.Id.Value,
                    owner.Definition.Source));
        }
    }

    private static void AddCompilationDiagnostic(
        AssetNode node,
        ImportedAssetPayload imported,
        string sourceRelativePath,
        List<AssetCompilerDiagnostic> diagnostics)
    {
        if (node.Definition.Type ==
            RuntimeAssetType.Texture)
        {
            RuntimeTextureData texture =
                RuntimeTextureData.FromPayload(
                    imported.Payload);

            diagnostics.Add(
                new AssetCompilerDiagnostic(
                    "ASSETI001",
                    AssetCompilerDiagnosticSeverity.Information,
                    $"Texture compiled: usage={TextureUsageName(texture.Usage)}; colorSpace={texture.ColorSpace}; dimensions={texture.Width}x{texture.Height}; mips={texture.Mips.Count}; format={texture.Format}; residentBytes={texture.ResidentByteCount}; payloadBytes={imported.Payload.LongLength}.",
                    node.Id.Value,
                    sourceRelativePath));
            return;
        }

        if (node.Definition.Type != RuntimeAssetType.Mesh ||
            imported.MeshSummary is not { } mesh)
        {
            return;
        }

        diagnostics.Add(
            new AssetCompilerDiagnostic(
                "ASSETI002",
                AssetCompilerDiagnosticSeverity.Information,
                $"Mesh compiled: vertices={mesh.VertexCount}; indices={mesh.IndexCount}; sections={mesh.SectionCount}; materials={mesh.MaterialCount}; uv0={(mesh.HasUv0 ? "present" : "missing")}; tangents={(mesh.HasTangents ? "present" : "unavailable")}; generatedNormals={mesh.GeneratedNormalVertexCount}; generatedUvVertices={mesh.GeneratedUvVertexCount}; generatedTangents={mesh.GeneratedTangentVertexCount}; fallbackSections={mesh.FallbackSectionCount}.",
                node.Id.Value,
                sourceRelativePath));

        if (mesh.FallbackSectionCount > 0)
        {
            diagnostics.Add(
                new AssetCompilerDiagnostic(
                    "ASSETW002",
                    AssetCompilerDiagnosticSeverity.Warning,
                    $"{mesh.FallbackSectionCount} mesh section(s) cannot satisfy their textured material UV/tangent requirements and will use the development fallback.",
                    node.Id.Value,
                    sourceRelativePath));
        }
    }

    private static string TextureUsageName(
        RuntimeTextureUsage usage) =>
        usage == RuntimeTextureUsage.BaseColor
            ? nameof(RuntimeTextureUsage.BaseColor)
            : usage.ToString();

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

    private static ImportedAssetPayload Import(
        AssetNode node,
        string sourceRoot,
        IReadOnlyDictionary<AssetId, AssetNode> nodes, Dictionary<AssetId, RuntimeAssetRecord> records, string runtimeRoot, AssetCompilationReporter reporter)
    {
        if (node.Definition.Type == RuntimeAssetType.Mesh)
        {
            MeshMaterialRequirement[] requirements =
                GetMeshMaterialRequirements(
                    node,
                    nodes);
            ImportedAssetPayload imported = GltfImporter.Import(
                node.SourcePath,
                sourceRoot,
                node.Definition.MaterialReferences,
                requirements, allowSkin: node.Definition.SkinReference is not null);
            MechanicalMeshValidation.Validate(node.Definition.MechanicalParts, node.Definition.Sockets,
                RuntimeMeshData.FromPayload(imported.Payload).Indices.Count);
            if (node.Definition.SkinReference is { } skinReference)
            {
                if (node.Definition.MechanicalParts.Count > 0 || node.Definition.AnimationReferences.Count > 0)
                    throw new InvalidDataException("A skinned mesh cannot also use rigid mechanical/clip motion.");
                var skinRecord = records[AssetId.Parse(skinReference)];
                var skin = RuntimeSkinData.FromPayload(RuntimeAssetFile.Read(Path.Combine(runtimeRoot, skinRecord.RuntimePath), RuntimeAssetType.Skin, skinRecord.RuntimeBytes, skinRecord.RuntimeHash).Payload);
                if (!skin.Mesh.ToPayload().AsSpan().SequenceEqual(imported.Payload)) throw new InvalidDataException("Skin and mesh compiled vertex order/materials differ.");
                foreach (var socket in skin.Sockets)
                    if (!node.Definition.Sockets.Any(s => s.Name == socket.Name && System.Numerics.Vector3.Distance(new(s.X,s.Y,s.Z), socket.BindPosition) < .00001f))
                        throw new InvalidDataException("Skin and mesh canonical socket bindings differ.");
            }
            foreach (string clipId in node.Definition.AnimationReferences)
            {
                RuntimeAssetRecord clipRecord = records[AssetId.Parse(clipId)];
                var clip = RuntimeAnimationData.FromPayload(RuntimeAssetFile.Read(Path.Combine(runtimeRoot, clipRecord.RuntimePath), RuntimeAssetType.Animation,
                    clipRecord.RuntimeBytes, clipRecord.RuntimeHash).Payload);
                RigidAnimationBinding.Validate(clip, node.Definition.MechanicalParts, node.Definition.Sockets, RuntimeMeshData.FromPayload(imported.Payload));
            }
            return imported;
        }

        if (node.Definition.Type == RuntimeAssetType.Skin)
        {
            var imported = SkinImporter.Import(node.SourcePath, sourceRoot, node.Definition, GetMeshMaterialRequirements(node, nodes));
            var skin = RuntimeSkinData.FromPayload(imported.Payload);
            foreach (string clipId in node.Definition.AnimationReferences)
            {
                var record = records[AssetId.Parse(clipId)];
                _ = skin.Bind(RuntimeAnimationData.FromPayload(RuntimeAssetFile.Read(Path.Combine(runtimeRoot, record.RuntimePath), RuntimeAssetType.Animation, record.RuntimeBytes, record.RuntimeHash).Payload));
            }
            return imported;
        }
        return node.Definition.Type switch
        {
            RuntimeAssetType.Animation => AnimationImporter.Import(node.SourcePath, sourceRoot, node.Definition.SkeletalAnimation, node.Definition.AnimationClip),
            RuntimeAssetType.Texture => TextureImporter.Import(
                node.SourcePath,
                node.Definition.TextureColorSpace,
                node.Definition.TextureUsage,
                node.Definition.TextureGenerateMipmaps,
                node.Definition.TextureMaxMipLevels,
                node.Definition.TextureMaxDimension,
                node.Definition.TextureFormat,
                node.Definition.TextureCompressionQuality, reporter, node.Id.Value),
            RuntimeAssetType.Material => MaterialImporter.Import(node.SourcePath),
            RuntimeAssetType.UiSkin => UiSkinImporter.Import(node.SourcePath, records, runtimeRoot),
            _ => throw new InvalidDataException(
                $"Asset type '{node.Definition.Type}' is unsupported."),
        };
    }

    private static MeshMaterialRequirement[] GetMeshMaterialRequirements(
        AssetNode node,
        IReadOnlyDictionary<AssetId, AssetNode> nodes)
    {
        var requirements =
            new MeshMaterialRequirement[
                node.Definition.MaterialReferences.Count];

        for (int index = 0;
             index < node.Definition.MaterialReferences.Count;
             index++)
        {
            AssetId materialId =
                AssetId.Parse(
                    node.Definition.MaterialReferences[index]);
            if (!nodes.TryGetValue(
                    materialId,
                    out AssetNode? materialNode) ||
                materialNode.Definition.Type != RuntimeAssetType.Material)
            {
                throw new InvalidDataException(
                    $"Mesh material reference '{materialId}' does not resolve to a material asset.");
            }

            MaterialSourceDefinition material =
                MaterialImporter.ReadDefinition(
                    materialNode.SourcePath);
            requirements[index] =
                new MeshMaterialRequirement(
                    material.ReferencedTextureIds.Count > 0,
                    !string.IsNullOrWhiteSpace(
                        material.NormalTexture));
        }

        return requirements;
    }

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

        ScatterAssetValidation.Validate(node.Definition.Scatter, node.Definition.Type, bounds);
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
            // Material slots are indexed by mesh sections; order and repeated IDs are semantic.
            MaterialReferences = node.Definition.MaterialReferences.ToArray(),
            TextureReferences = textureReferences
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Lods = node.Definition.Lods
                .OrderBy(static lod => lod.Level)
                .ToArray(),
            CollisionReference = node.Definition.CollisionReference,
            AnimationReferences = node.Definition.AnimationReferences.ToArray(),
            SkinReference = node.Definition.SkinReference,
            MechanicalParts = node.Definition.MechanicalParts.ToArray(),
            Sockets = node.Definition.Sockets
                .OrderBy(static socket => socket.Name, StringComparer.Ordinal)
                .ToArray(),
            Bounds = bounds,
            Scatter = node.Definition.Scatter,
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
            return RuntimeAssetCatalog.LoadCancellable(runtimeRoot, CancellationToken.None,
                requireIntegrity: true).Manifest;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
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
            RuntimeAssetType.Animation => "animations",
            RuntimeAssetType.Skin => "skins",
            RuntimeAssetType.Texture => "textures",
            RuntimeAssetType.Material => "materials",
            RuntimeAssetType.UiSkin => "ui-skins",
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

        var temporaryPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
        var temporaryPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
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
