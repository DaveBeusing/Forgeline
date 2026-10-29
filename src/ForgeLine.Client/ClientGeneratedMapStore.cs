using ForgeLine.Assets;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.UI;
using ForgeLine.World;

namespace ForgeLine.Client;

internal sealed class ClientGeneratedMapException(Exception cause) : IOException("The required generated map is missing or incompatible.", cause);

/// <summary>Host-owned local packages, addressed exclusively by verified manifest identity.</summary>
internal sealed class ClientGeneratedMapStore(string root, string assetSha256, RuntimeAssetCatalog? assets = null)
{
    private readonly string _root = Path.GetFullPath(root);
    internal RuntimeAssetCatalog? Assets { get; } = assets;

    internal GeneratedMapPackage Prepare(GeneratedMapRecipe recipe, Action verifying, CancellationToken token)
    {
        var package = GeneratedMapCompiler.Compile(recipe, assetSha256, token);
        token.ThrowIfCancellationRequested();
        verifying();
        string target = PathFor(package.ManifestSha256);
        if (!Directory.Exists(target))
        {
            string temporary = Path.Combine(_root, ".preparing-" + Guid.NewGuid().ToString("N"));
            try
            {
                package.Write(temporary);
                token.ThrowIfCancellationRequested();
                try { Directory.Move(temporary, target); }
                catch (IOException) when (Directory.Exists(target)) { /* Another preparation published this identity. Verify it below. */ }
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
        }
        var verified = Load(package.ManifestSha256, token);
        token.ThrowIfCancellationRequested();
        _ = CreateTerrainProfile(verified, Assets);
        token.ThrowIfCancellationRequested();
        return verified;
    }

    internal GeneratedMapPackage Load(string manifestSha256, CancellationToken token) =>
        GeneratedMapPackage.Load(PathFor(manifestSha256), assetSha256, manifestSha256, token);

    internal GeneratedMapPackage? Resolve(ClientSessionRequest request, CancellationToken token)
    {
        string? manifest = request.GeneratedManifestSha256 ?? request.Identity?.Generated?.ManifestSha256;
        if (request.Kind == ClientSessionRequestKind.LoadGame && request.Save is { } entry)
            manifest = MatchPersistenceSerializer.ReadSave(entry.Path).Configuration.Identity?.Generated?.ManifestSha256;
        if (manifest is null) return null;
        try { return Load(manifest, token); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        { throw new ClientGeneratedMapException(error); }
    }

    private string PathFor(string hash)
    {
        if (hash is not { Length: 64 } || hash.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Invalid generated map manifest identity.");
        return Path.Combine(_root, hash);
    }

    internal static TerrainPresentationProfile CreateTerrainProfile(GeneratedMapPackage package, RuntimeAssetCatalog? assets)
    {
        var world = package.CreateTerrain();
        var environment = TerrainVisualProfileResolver.CreateEnvironment(world, package.Manifest.Recipe.Terrain.Generation);
        try { return TerrainVisualProfileResolver.Resolve(package.Definition.TerrainVisual, world, environment, runtimeAssets: assets); }
        catch (InvalidOperationException error)
        { throw new InvalidDataException("Generated map qualification failed: terrain material profile is unsupported for this map.", error); }
    }

    internal static IChunkDecorationProvider CreateDecorations(GeneratedMapPackage package, CancellationToken token) =>
        new ChunkDecorationProvider(new ChunkDecorationSnapshot(Chunks(package, token)));

    // Iterator is evaluated on the construction owner by the snapshot constructor, never on the platform owner.
    private static IEnumerable<EnvironmentScatterChunk> Chunks(GeneratedMapPackage package, CancellationToken token)
    {
        var grid = package.Manifest.Recipe.Terrain.Generation;
        for (int z = 0; z < grid.HeightInChunks; z++) for (int x = 0; x < grid.WidthInChunks; x++)
        {
            token.ThrowIfCancellationRequested();
            yield return package.GenerateDecoration(new(x, z));
        }
    }
}
