using System.Diagnostics;
using System.Text.Json;

namespace ForgeLine.Assets;

public enum RuntimeAssetQualificationSeverity
{
    Warning = 0,
    Error = 1
}

public sealed record RuntimeAssetQualificationIssue(
    string Code,
    RuntimeAssetQualificationSeverity Severity,
    string Message,
    string? AssetId = null,
    string? RuntimePath = null);

public sealed record RuntimeAssetFootprintEntry(
    string AssetId,
    RuntimeAssetType Type,
    long RuntimeBytes);

public sealed record RuntimeAssetQualificationReport
{
    public required int AssetCount { get; init; }

    public required int MeshCount { get; init; }

    public required int TextureCount { get; init; }

    public required int MaterialCount { get; init; }

    public required long TotalRuntimeBytes { get; init; }

    public required long MeshRuntimeBytes { get; init; }

    public required long TextureRuntimeBytes { get; init; }

    public required long MaterialRuntimeBytes { get; init; }

    public required TimeSpan CatalogLoadDuration { get; init; }

    public required TimeSpan AssetReadDuration { get; init; }

    public required IReadOnlyList<RuntimeAssetFootprintEntry> LargestAssets { get; init; }

    public required IReadOnlyList<RuntimeAssetQualificationIssue> Issues { get; init; }

    public bool Success =>
        Issues.All(static issue =>
            issue.Severity != RuntimeAssetQualificationSeverity.Error);
}

public static class RuntimeAssetQualification
{
    private const int LargestAssetCount = 12;

    public static RuntimeAssetQualificationReport Run(string runtimeRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeRoot);

        string normalizedRoot =
            Path.GetFullPath(runtimeRoot);
        var issues =
            new List<RuntimeAssetQualificationIssue>();

        RuntimeAssetCatalog catalog;
        var loadWatch =
            Stopwatch.StartNew();

        try
        {
            catalog =
                RuntimeAssetCatalog.Load(
                    normalizedRoot);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            System.Text.Json.JsonException)
        {
            loadWatch.Stop();
            issues.Add(
                new RuntimeAssetQualificationIssue(
                    "ASSETQ000",
                    RuntimeAssetQualificationSeverity.Error,
                    $"Runtime asset catalog could not be loaded: {exception.Message}"));

            return EmptyReport(
                loadWatch.Elapsed,
                issues);
        }

        loadWatch.Stop();

        long totalBytes = 0;
        long meshBytes = 0;
        long textureBytes = 0;
        long materialBytes = 0;
        int meshCount = 0;
        int textureCount = 0;
        int materialCount = 0;
        var entries =
            new List<RuntimeAssetFootprintEntry>(
                catalog.Manifest.Assets.Count);
        var readWatch =
            Stopwatch.StartNew();

        foreach (RuntimeAssetRecord record in
                 catalog.Manifest.Assets)
        {
            AssetId id =
                AssetId.Parse(
                    record.Id);
            string fullPath =
                ResolveRuntimePath(
                    normalizedRoot,
                    record.RuntimePath);

            long runtimeBytes = 0;

            try
            {
                var info =
                    new FileInfo(
                        fullPath);
                if (!info.Exists)
                {
                    throw new FileNotFoundException(
                        "Runtime asset payload was not found.",
                        fullPath);
                }

                runtimeBytes =
                    info.Length;
                RuntimeAssetContent content =
                    catalog.Read(
                        id);

                switch (record.Type)
                {
                    case RuntimeAssetType.Mesh:
                        _ =
                            RuntimeMeshData.FromPayload(
                                content.Payload);
                        break;

                    case RuntimeAssetType.Texture:
                        _ =
                            RuntimeTextureData.FromPayload(
                                content.Payload);
                        break;

                    case RuntimeAssetType.Material:
                        _ =
                            RuntimeMaterialData.FromPayload(
                                content.Payload);
                        break;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                JsonException)
            {
                issues.Add(
                    new RuntimeAssetQualificationIssue(
                        "ASSETQ001",
                        RuntimeAssetQualificationSeverity.Error,
                        $"Runtime payload could not be read: {exception.Message}",
                        record.Id,
                        record.RuntimePath));
            }

            entries.Add(
                new RuntimeAssetFootprintEntry(
                    record.Id,
                    record.Type,
                    runtimeBytes));
            totalBytes =
                checked(
                    totalBytes +
                    runtimeBytes);

            switch (record.Type)
            {
                case RuntimeAssetType.Mesh:
                    meshCount++;
                    meshBytes =
                        checked(
                            meshBytes +
                            runtimeBytes);
                    break;

                case RuntimeAssetType.Texture:
                    textureCount++;
                    textureBytes =
                        checked(
                            textureBytes +
                            runtimeBytes);
                    break;

                case RuntimeAssetType.Material:
                    materialCount++;
                    materialBytes =
                        checked(
                            materialBytes +
                            runtimeBytes);
                    break;
            }

            ValidateRecord(
                record,
                catalog,
                issues);
        }

        readWatch.Stop();

        return new RuntimeAssetQualificationReport
        {
            AssetCount =
                catalog.Manifest.Assets.Count,
            MeshCount =
                meshCount,
            TextureCount =
                textureCount,
            MaterialCount =
                materialCount,
            TotalRuntimeBytes =
                totalBytes,
            MeshRuntimeBytes =
                meshBytes,
            TextureRuntimeBytes =
                textureBytes,
            MaterialRuntimeBytes =
                materialBytes,
            CatalogLoadDuration =
                loadWatch.Elapsed,
            AssetReadDuration =
                readWatch.Elapsed,
            LargestAssets =
                entries
                    .OrderByDescending(
                        static entry =>
                            entry.RuntimeBytes)
                    .ThenBy(
                        static entry =>
                            entry.AssetId,
                        StringComparer.Ordinal)
                    .Take(
                        LargestAssetCount)
                    .ToArray(),
            Issues =
                issues
        };
    }

    private static void ValidateRecord(
        RuntimeAssetRecord record,
        RuntimeAssetCatalog catalog,
        List<RuntimeAssetQualificationIssue> issues)
    {
        if (record.Bounds is AssetBounds bounds &&
            !bounds.IsValid)
        {
            issues.Add(
                new RuntimeAssetQualificationIssue(
                    "ASSETQ002",
                    RuntimeAssetQualificationSeverity.Error,
                    "Asset bounds are invalid.",
                    record.Id,
                    record.RuntimePath));
        }

        ValidateReferences(
            record.Dependencies,
            expectedType: null,
            "dependency",
            record,
            catalog,
            issues);
        ValidateReferences(
            record.MaterialReferences,
            RuntimeAssetType.Material,
            "material",
            record,
            catalog,
            issues);
        ValidateReferences(
            record.TextureReferences,
            RuntimeAssetType.Texture,
            "texture",
            record,
            catalog,
            issues);
        ValidateReferences(
            record.AnimationReferences,
            expectedType: null,
            "animation",
            record,
            catalog,
            issues);

        if (!string.IsNullOrWhiteSpace(
                record.CollisionReference))
        {
            ValidateReference(
                record.CollisionReference,
                RuntimeAssetType.Mesh,
                "collision",
                record,
                catalog,
                issues);
        }

        ValidateLods(
            record,
            catalog,
            issues);
    }

    private static void ValidateLods(
        RuntimeAssetRecord record,
        RuntimeAssetCatalog catalog,
        List<RuntimeAssetQualificationIssue> issues)
    {
        if (record.Lods.Count == 0)
        {
            return;
        }

        var levels =
            new HashSet<int>();
        float previousDistance = 0.0f;

        foreach (AssetLodReference lod in
                 record.Lods.OrderBy(
                     static lod =>
                         lod.Level))
        {
            if (lod.Level <= 0 ||
                !levels.Add(
                    lod.Level))
            {
                issues.Add(
                    new RuntimeAssetQualificationIssue(
                        "ASSETQ003",
                        RuntimeAssetQualificationSeverity.Error,
                        $"LOD level '{lod.Level}' must be positive and unique.",
                        record.Id,
                        record.RuntimePath));
            }

            if (!float.IsFinite(
                    lod.MaxDistance) ||
                lod.MaxDistance <= 0.0f ||
                lod.MaxDistance <= previousDistance)
            {
                issues.Add(
                    new RuntimeAssetQualificationIssue(
                        "ASSETQ004",
                        RuntimeAssetQualificationSeverity.Error,
                        $"LOD level '{lod.Level}' has invalid or non-increasing maxDistance '{lod.MaxDistance}'.",
                        record.Id,
                        record.RuntimePath));
            }

            previousDistance =
                Math.Max(
                    previousDistance,
                    lod.MaxDistance);

            ValidateReference(
                lod.AssetId,
                RuntimeAssetType.Mesh,
                $"LOD{lod.Level}",
                record,
                catalog,
                issues);
        }
    }

    private static void ValidateReferences(
        IReadOnlyList<string> references,
        RuntimeAssetType? expectedType,
        string relation,
        RuntimeAssetRecord owner,
        RuntimeAssetCatalog catalog,
        List<RuntimeAssetQualificationIssue> issues)
    {
        for (int index = 0;
             index < references.Count;
             index++)
        {
            ValidateReference(
                references[index],
                expectedType,
                relation,
                owner,
                catalog,
                issues);
        }
    }

    private static void ValidateReference(
        string reference,
        RuntimeAssetType? expectedType,
        string relation,
        RuntimeAssetRecord owner,
        RuntimeAssetCatalog catalog,
        List<RuntimeAssetQualificationIssue> issues)
    {
        AssetId referencedId;

        try
        {
            referencedId =
                AssetId.Parse(
                    reference);
        }
        catch (ArgumentException exception)
        {
            issues.Add(
                new RuntimeAssetQualificationIssue(
                    "ASSETQ005",
                    RuntimeAssetQualificationSeverity.Error,
                    $"Invalid {relation} reference '{reference}': {exception.Message}",
                    owner.Id,
                    owner.RuntimePath));
            return;
        }

        if (!catalog.TryGet(
                referencedId,
                out RuntimeAssetRecord? referenced) ||
            referenced is null)
        {
            issues.Add(
                new RuntimeAssetQualificationIssue(
                    "ASSETQ006",
                    RuntimeAssetQualificationSeverity.Error,
                    $"Missing {relation} asset '{reference}'.",
                    owner.Id,
                    owner.RuntimePath));
            return;
        }

        if (expectedType is not null &&
            referenced.Type != expectedType.Value)
        {
            issues.Add(
                new RuntimeAssetQualificationIssue(
                    "ASSETQ007",
                    RuntimeAssetQualificationSeverity.Error,
                    $"{relation} asset '{reference}' has type '{referenced.Type}' instead of '{expectedType.Value}'.",
                    owner.Id,
                    owner.RuntimePath));
        }
    }

    private static string ResolveRuntimePath(
        string runtimeRoot,
        string relativePath)
    {
        string fullPath =
            Path.GetFullPath(
                Path.Combine(
                    runtimeRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
        string rootPrefix =
            runtimeRoot.EndsWith(
                Path.DirectorySeparatorChar)
                ? runtimeRoot
                : runtimeRoot +
                  Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Runtime path '{relativePath}' escapes the runtime asset root.");
        }

        return fullPath;
    }

    private static RuntimeAssetQualificationReport EmptyReport(
        TimeSpan catalogLoadDuration,
        IReadOnlyList<RuntimeAssetQualificationIssue> issues) =>
        new()
        {
            AssetCount = 0,
            MeshCount = 0,
            TextureCount = 0,
            MaterialCount = 0,
            TotalRuntimeBytes = 0,
            MeshRuntimeBytes = 0,
            TextureRuntimeBytes = 0,
            MaterialRuntimeBytes = 0,
            CatalogLoadDuration = catalogLoadDuration,
            AssetReadDuration = TimeSpan.Zero,
            LargestAssets = [],
            Issues = issues
        };
}
