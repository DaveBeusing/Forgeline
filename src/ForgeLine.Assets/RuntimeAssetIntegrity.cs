using System.Security.Cryptography;
using System.Text.Json;

namespace ForgeLine.Assets;

/// <summary>Integrity of compiled outputs; does not authenticate their publisher.</summary>
public static class RuntimeAssetIntegrity
{
    public const int SupportedRuntimeVersion = 4;

    public static RuntimeAssetRecord SealRecord(string path, RuntimeAssetRecord record)
    {
        using var stream = File.OpenRead(path);
        return record with { RuntimeBytes = stream.Length, RuntimeHash = Hash(stream) };
    }

    public static RuntimeAssetManifest SealManifest(RuntimeAssetManifest manifest)
    {
        var sealedManifest = manifest with
        {
            IntegrityVersion = RuntimeAssetManifest.CurrentIntegrityVersion,
            ContentSignature = string.Empty,
        };
        return sealedManifest with { ContentSignature = Signature(sealedManifest) };
    }

    public static void ValidateManifest(RuntimeAssetManifest manifest)
    {
        if (manifest.Assets is null || string.IsNullOrWhiteSpace(manifest.CompilerVersion) ||
            manifest.IntegrityVersion != RuntimeAssetManifest.CurrentIntegrityVersion ||
            !IsHash(manifest.ContentSignature) ||
            !string.Equals(manifest.ContentSignature, Signature(manifest), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Runtime manifest integrity is missing, incompatible or damaged. Rebuild runtime assets or reinstall the complete package.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in manifest.Assets)
        {
            if (record is null || record.Dependencies is null || !ids.Add(record.Id) || !paths.Add(record.RuntimePath) ||
                !Enum.IsDefined(record.Type) || record.RuntimeVersion != SupportedRuntimeVersion ||
                record.CompilerVersion != manifest.CompilerVersion || string.IsNullOrWhiteSpace(record.CompilerVersion) ||
                record.RuntimeBytes < 20 || !IsHash(record.RuntimeHash) ||
                !IsHash(record.SourceHash) || !IsHash(record.BuildHash))
            {
                throw new InvalidDataException($"Runtime manifest contract is invalid for '{record?.Id}'. Rebuild runtime assets or reinstall the complete package.");
            }
        }

        foreach (var record in manifest.Assets)
        {
            foreach (var dependency in record.Dependencies)
            {
                if (!ids.Contains(dependency) || dependency == record.Id)
                {
                    throw new InvalidDataException($"Runtime asset '{record.Id}' has invalid dependency '{dependency}'.");
                }
            }
        }
    }

    public static bool Matches(string path, RuntimeAssetRecord record)
    {
        try
        {
            using var stream = File.OpenRead(path);
            ValidateFile(stream, record.RuntimeBytes, record.RuntimeHash, path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    internal static void ValidateFile(Stream stream, long bytes, string hash, string path)
    {
        if (!IsHash(hash) || stream.Length != bytes || Hash(stream) != hash)
        {
            throw new InvalidDataException($"Runtime asset '{path}' failed size/SHA-256 verification. Rebuild runtime assets or reinstall the complete package.");
        }
        stream.Position = 0;
    }

    private static string Signature(RuntimeAssetManifest manifest) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            manifest with { ContentSignature = string.Empty }, RuntimeAssetCatalog.CreateJsonOptions())));

    private static string Hash(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));

    private static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
