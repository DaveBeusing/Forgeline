using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ForgeLine.AssetCompiler;

internal static class AssetHashing
{
    public static string ComputeAssetSourceHash(
        string definitionPath,
        string sourcePath,
        string sourceRoot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendFile(hash, definitionPath);
        AppendFile(hash, sourcePath);

        if (Path.GetExtension(sourcePath).Equals(".gltf", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var referencedFile in EnumerateExternalGltfBuffers(sourcePath, sourceRoot))
            {
                AppendFile(hash, referencedFile);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string ComputeBuildHash(
        string sourceHash,
        string compilerVersion,
        int runtimeVersion,
        IEnumerable<string> dependencyBuildHashes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, sourceHash);
        AppendText(hash, compilerVersion);
        AppendText(hash, runtimeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        foreach (var dependencyHash in dependencyBuildHashes.Order(StringComparer.Ordinal))
        {
            AppendText(hash, dependencyHash);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendFile(IncrementalHash hash, string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> buffer = stackalloc byte[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer[..read]);
        }
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    private static IEnumerable<string> EnumerateExternalGltfBuffers(string gltfPath, string sourceRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(gltfPath));
        if (!document.RootElement.TryGetProperty("buffers", out var buffers))
        {
            yield break;
        }

        var sourceDirectory = Path.GetDirectoryName(gltfPath)
            ?? throw new InvalidDataException($"Could not resolve source directory for '{gltfPath}'.");

        foreach (var buffer in buffers.EnumerateArray())
        {
            if (!buffer.TryGetProperty("uri", out var uriProperty))
            {
                continue;
            }

            var uri = uriProperty.GetString();
            if (string.IsNullOrWhiteSpace(uri) || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var decodedUri = Uri.UnescapeDataString(uri.Replace('/', Path.DirectorySeparatorChar));
            var resolved = ResolveWithinRoot(sourceRoot, Path.Combine(sourceDirectory, decodedUri));
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"glTF buffer '{uri}' was not found.", resolved);
            }

            yield return resolved;
        }
    }

    internal static string ResolveWithinRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var rootPrefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Path '{path}' escapes asset source root '{normalizedRoot}'.");
        }

        return fullPath;
    }
}
