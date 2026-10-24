namespace ForgeLine.Game;

public sealed record ScenarioIdentity(
    string MapKey, int MapRevision, string MapSha256, int TerrainVersion,
    string ContentKey, string ContentSha256, string StartRule, string ObjectiveRule);

public sealed record ScenarioDescriptor(string Key, string DisplayName, string MapPath, ScenarioIdentity Identity);
public sealed record ScenarioAvailability(ScenarioDescriptor Descriptor, bool Available, string? Error);

