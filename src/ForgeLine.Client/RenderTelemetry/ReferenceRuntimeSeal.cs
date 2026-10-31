using ForgeLine.Assets;

namespace ForgeLine.Client;

// Opt-in evidence producers require integrity even though general catalog inspection permits legacy files.
internal static class ReferenceRuntimeSeal
{
    internal static RuntimeAssetCatalog Load(string root, out object seal)
    {
        string manifest = Path.Combine(root, RuntimeAssetCatalog.ManifestFileName);
        byte[] before = File.ReadAllBytes(manifest);
        var catalog = RuntimeAssetCatalog.LoadCancellable(root, CancellationToken.None, requireIntegrity: true);
        seal = Verify(catalog);
        if (!before.AsSpan().SequenceEqual(File.ReadAllBytes(manifest)))
            throw new InvalidDataException("Runtime manifest changed while loading/verifying the actual reference catalog.");
        return catalog;
    }
    internal static object VerifyLoaded(RuntimeAssetCatalog catalog)
    {
        string manifest = Path.Combine(catalog.RuntimeRoot, RuntimeAssetCatalog.ManifestFileName);
        byte[] before = File.ReadAllBytes(manifest);
        var options = System.Text.Json.JsonSerializerOptions.Default;
        var disk = System.Text.Json.JsonSerializer.Deserialize<RuntimeAssetManifest>(before, RuntimeAssetCatalog.CreateJsonOptions())
            ?? throw new InvalidDataException("Empty runtime manifest.");
        if (!System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(disk, options).AsSpan().SequenceEqual(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalog.Manifest, options)))
            throw new InvalidDataException("Actual loaded catalog differs from current exact sealed manifest.");
        object seal = Verify(catalog);
        if (!before.AsSpan().SequenceEqual(File.ReadAllBytes(manifest))) throw new InvalidDataException("Runtime manifest changed while verifying the actual loaded catalog.");
        return seal;
    }
    private static object Verify(RuntimeAssetCatalog catalog)
    {
        RuntimeAssetIntegrity.ValidateManifest(catalog.Manifest);
        long totalBytes = 0;
        foreach (var record in catalog.Manifest.Assets)
        {
            string path = Path.GetFullPath(Path.Combine(catalog.RuntimeRoot, record.RuntimePath));
            string relative = Path.GetRelativePath(catalog.RuntimeRoot, path);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ||
                !RuntimeAssetIntegrity.Matches(path, record))
                throw new InvalidDataException($"Sealed reference runtime asset is missing or changed: {record.Id}");
            totalBytes = checked(totalBytes + record.RuntimeBytes);
        }
        return new { integrityVersion = catalog.Manifest.IntegrityVersion, catalog.Manifest.ContentSignature,
            manifestSha256 = VisualReferenceCapture.FileHash(Path.Combine(catalog.RuntimeRoot, RuntimeAssetCatalog.ManifestFileName)),
            assetCount = catalog.Manifest.Assets.Count, totalBytes, allRecordSizesAndHashesVerified = true,
            scope = "One actual integrity-required catalog; all listed runtime files checked outside measured sample window; included in startup/setup; complete directory fingerprint before/after" };
    }
}
