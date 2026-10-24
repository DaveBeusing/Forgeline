using System.Security.Cryptography;

namespace ForgeLine.Game;

// Registrations are application-owned data. Files cannot name types or introduce executable rules.
public sealed class ScenarioRegistry
{
    public const string SplitBasinMapSha256 = "d2e47e1ec4da0fe8e2b498d10d73f8b7a2fee6e0a5f76ab86514510ed6c6e861";
    public const string CentralDivideMapSha256 = "d60f7ae9d4d6b6771269bd53ccfada847615c1d3f261b87e6ba0223e55f276ef";
    public const string CentralDivideContentSha256 = "192624b7d2011b4ae6d0858d74d2fdfa9dc0d361c029444bf663f785556ffbd9";
    public const string CentralDivideMapPath = "central-divide.flmap.json";
    private readonly string _contentRoot;
    private readonly ScenarioDescriptor[] _descriptors;

    public static ScenarioRegistry Default => new(Path.Combine(AppContext.BaseDirectory, "maps"),
        [CentralDivideDescriptor, SplitBasinDescriptor]);

    public static ScenarioDescriptor CentralDivideDescriptor => new(
        CentralDivideScenario.CompositionKey, "Central Divide", CentralDivideMapPath,
        new("prototype.vertical_slice", 1, CentralDivideMapSha256, BattlefieldTerrainRecipe.CurrentVersion,
            ScenarioContentIdentity.DirectorateKey, CentralDivideContentSha256,
            ScenarioContentIdentity.StartRule, ScenarioContentIdentity.ObjectiveRule))
        { Description = "3.072 KM - RIDGE / TWO CROSSINGS" };

    public static ScenarioDescriptor SplitBasinDescriptor => new(
        SplitBasinBattlefield.CompositionKey, "Split Basin", "split-basin.flmap.json",
        new(SplitBasinBattlefield.MapKey, 1, SplitBasinMapSha256, BattlefieldTerrainRecipe.CurrentVersion,
            ScenarioContentIdentity.DirectorateKey, CentralDivideContentSha256,
            ScenarioContentIdentity.StartRule, ScenarioContentIdentity.ObjectiveRule))
        { Description = "2.048 KM - OPEN BASIN / OUTER ROUTE" };

    public ScenarioRegistry(string contentRoot, IReadOnlyList<ScenarioDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(descriptors);
        if (descriptors.Count is < 1 or > 16 || descriptors.Any(d => d is null || string.IsNullOrWhiteSpace(d.Key)) ||
            descriptors.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count() != descriptors.Count)
            throw new ArgumentException("Scenario registrations must have unique keys and a bounded count.", nameof(descriptors));
        _contentRoot = Path.GetFullPath(contentRoot);
        _descriptors = descriptors.ToArray();
        foreach (var descriptor in _descriptors) ResolvePath(descriptor.MapPath);
    }

    public IReadOnlyList<ScenarioDescriptor> Descriptors => Array.AsReadOnly(_descriptors);

    public IReadOnlyList<ScenarioAvailability> Inspect() => _descriptors.Select(descriptor =>
    {
        try { Resolve(descriptor.Key); return new ScenarioAvailability(descriptor, true, null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        { return new ScenarioAvailability(descriptor, false, error.Message); }
    }).ToArray();

    public MatchComposition Resolve(string key)
    {
        var descriptor = _descriptors.SingleOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal)) ??
            throw new InvalidOperationException($"Unknown scenario '{key}'.");
        var identity = descriptor.Identity;
        string path = ResolvePath(descriptor.MapPath);
        string integrityPath = ResolvePath(descriptor.MapPath + ".sha256");
        byte[] payload = ReadBounded(path, BattlefieldMapArtifact.MaximumInputBytes);
        string digest = Convert.ToHexStringLower(SHA256.HashData(payload));
        string sidecar = System.Text.Encoding.ASCII.GetString(ReadBounded(integrityPath, 128)).Trim();
        if (!string.Equals(digest, identity.MapSha256, StringComparison.Ordinal) || sidecar != digest)
            throw new InvalidOperationException($"Scenario '{key}' map integrity does not match its registered revision.");
        var artifact = BattlefieldMapArtifact.Deserialize(payload);
        var content = CentralDivideScenario.CreateContent();
        if (artifact.Metadata.Key != identity.MapKey || artifact.MapRevision != identity.MapRevision ||
            artifact.Terrain.Version != identity.TerrainVersion || artifact.ContentKey != identity.ContentKey ||
            artifact.ContentSha256 != identity.ContentSha256 || artifact.StartRule != identity.StartRule ||
            artifact.ObjectiveRule != identity.ObjectiveRule || identity.ContentSha256 != ScenarioContentIdentity.ComputeSha256(content))
            throw new InvalidOperationException($"Scenario '{key}' terrain, content or rules are incompatible with this runtime.");
        foreach (var deposit in artifact.Resources)
            if (!content.Resources.TryGet(deposit.ResourceId, out _))
                throw new InvalidOperationException($"Scenario '{key}' references an unknown resource.");
        var battlefield = artifact.ToDefinition();
        var terrain = artifact.Terrain.Copy();
        return new MatchComposition(key, battlefield, content,
            map => BattlefieldTerrainFactory.Create(map, terrain),
            static (entities, inventories, factory, world, start, catalogs, stock) =>
                CentralDivideStartingBaseFactory.Create(entities, inventories, factory, world, start, stock, catalogs.Units, catalogs.Buildings),
            static systems => systems) { Identity = identity };
    }

    private string ResolvePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new ArgumentException("Scenario paths must be relative to the content root.");
        string path = Path.GetFullPath(Path.Combine(_contentRoot, relative));
        if (!path.StartsWith(_contentRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scenario path escapes the content root.");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Scenario paths cannot traverse symbolic links or reparse points.");
            if (string.Equals(current, _contentRoot, StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    private static byte[] ReadBounded(string path, int limit)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 1 || stream.Length > limit) throw new IOException("Scenario file exceeds its input budget.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Scenario file changed while reading.");
        return bytes;
    }
}
