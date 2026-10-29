namespace ForgeLine.Game;

public sealed record ScenarioIdentity(
    string MapKey, int MapRevision, string MapSha256, int TerrainVersion,
    string ContentKey, string ContentSha256, string StartRule, string ObjectiveRule)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public GeneratedScenarioIdentity? Generated { get; init; }
}

public sealed record GeneratedScenarioIdentity(int GeneratorVersion, ulong Seed, string GameplaySha256,
    string VisualSha256, string ManifestSha256, string AssetSha256);

public sealed record ScenarioDescriptor(string Key, string DisplayName, string MapPath, ScenarioIdentity Identity)
{
    public string Description { get; init; } = string.Empty;
}
public sealed record ScenarioAvailability(ScenarioDescriptor Descriptor, bool Available, string? Error);

