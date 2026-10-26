#if FORGELINE_DEVELOPER_TOOLS
using System.Text;
using System.Text.Json;
using ForgeLine.Core;

namespace ForgeLine.Game;

public sealed class DeveloperPresetException(DeveloperFailureReason failure, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public DeveloperFailureReason Failure { get; } = failure;
}

/// <summary>A bounded, versioned start profile. It contains no executable names or host capability.</summary>
public sealed class DeveloperPresetDocument
{
    public const int CurrentSchema = 1;
    public const int MaximumKeyLength = 64;
    public const int MaximumDisplayNameLength = 96;
    public const int MaximumOverrides = 64;
    private readonly int _schemaVersion;

    public DeveloperPresetDocument(string key, string displayName, DeveloperInitialProfile profile,
        string buildRevision, DateTimeOffset exportedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!DeveloperPresetCodec.IsPresetKey(key) || string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaximumDisplayNameLength ||
            displayName.Any(char.IsControl) || profile.Parameters.Count > MaximumOverrides ||
            string.IsNullOrWhiteSpace(buildRevision) || buildRevision.Length > 64 || exportedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Preset metadata exceeds its supported limits.");
        _schemaVersion = CurrentSchema;
        Key = key; DisplayName = displayName.Trim(); Profile = profile; BuildRevision = buildRevision; ExportedAtUtc = exportedAtUtc;
    }

    public int SchemaVersion => _schemaVersion;
    public string Key { get; }
    public string DisplayName { get; }
    public DeveloperInitialProfile Profile { get; }
    public string BuildRevision { get; }
    public DateTimeOffset ExportedAtUtc { get; }
}

/// <summary>Strict explicit JSON codec shared by the interactive client and headless host.</summary>
public static class DeveloperPresetCodec
{
    public const int MaximumDocumentBytes = 64 * 1024;
    private const int MaximumDepth = 12;
    private const int MaximumScenarioKeyLength = 128;
    private const int MaximumIdentityTextLength = 128;

    public static DeveloperPresetDocument Create(string key, string displayName, DeveloperInitialProfile profile) =>
        new(key, displayName, profile, typeof(DeveloperPresetCodec).Assembly.GetName().Version?.ToString() ?? "unknown",
            DateTimeOffset.UtcNow);

    public static bool IsPresetKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > DeveloperPresetDocument.MaximumKeyLength || !char.IsAsciiLetterOrDigit(key[0])) return false;
        foreach (char c in key)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return false;
        return true;
    }

    public static byte[] Serialize(DeveloperPresetDocument document, Func<string, MatchComposition>? resolveComposition = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateAndBuildPlan(document.Profile, resolveComposition);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, MaxDepth = MaximumDepth }))
        {
            var metadata = document.Profile.Metadata;
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", DeveloperPresetDocument.CurrentSchema);
            writer.WriteString("key", document.Key);
            writer.WriteString("displayName", document.DisplayName);
            writer.WriteString("buildRevision", document.BuildRevision);
            writer.WriteString("exportedAtUtc", document.ExportedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteString("baseScenarioKey", metadata.CompositionKey);
            writer.WritePropertyName("baseScenarioIdentity");
            writer.WriteStartObject();
            var identity = metadata.BaseIdentity;
            writer.WriteString("mapKey", identity.MapKey); writer.WriteNumber("mapRevision", identity.MapRevision);
            writer.WriteString("mapSha256", identity.MapSha256); writer.WriteNumber("terrainVersion", identity.TerrainVersion);
            writer.WriteString("contentKey", identity.ContentKey); writer.WriteString("contentSha256", identity.ContentSha256);
            writer.WriteString("startRule", identity.StartRule); writer.WriteString("objectiveRule", identity.ObjectiveRule);
            writer.WriteEndObject();
            writer.WriteString("baseProfile", metadata.Profile.ToString());
            writer.WriteNumber("seed", metadata.Seed);
            writer.WritePropertyName("participants"); writer.WriteStartArray();
            foreach (var participant in metadata.Participants.OrderBy(p => p.Player.Value))
            {
                writer.WriteStartObject(); writer.WriteNumber("playerId", participant.Player.Value);
                writer.WriteNumber("factionId", participant.Faction.Value); writer.WriteNumber("startIndex", participant.StartIndex);
                writer.WriteBoolean("computerControlled", participant.IsComputerControlled); writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("initialOverrides"); writer.WriteStartArray();
            foreach (var parameter in document.Profile.Parameters.OrderBy(p => p.Id).ThenBy(p => p.Target.Player.Value))
            {
                if (!DeveloperGameplayTuningSchema.TryGet(parameter.Id, out var descriptor))
                    throw new DeveloperPresetException(DeveloperFailureReason.UnsupportedParameter, "Preset contains an unsupported parameter.");
                writer.WriteStartObject(); writer.WriteString("key", descriptor.Contract.Key);
                writer.WriteString("kind", parameter.Value.Kind.ToString());
                writer.WriteString("scope", parameter.Target.Kind == DeveloperTargetKinds.Global ? "global" : "player");
                if (parameter.Target.Kind == DeveloperTargetKinds.Player) writer.WriteNumber("playerId", parameter.Target.Player.Value);
                else writer.WriteNull("playerId");
                writer.WritePropertyName("value"); WriteValue(writer, parameter.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject(); writer.Flush();
        }
        if (stream.Length is < 1 or > MaximumDocumentBytes)
            throw new DeveloperPresetException(DeveloperFailureReason.InvalidValue, "Preset JSON exceeds the 64 KiB limit.");
        return stream.ToArray();
    }

    public static DeveloperPresetDocument Deserialize(ReadOnlySpan<byte> utf8, Func<string, MatchComposition>? resolveComposition = null)
    {
        if (utf8.Length is < 1 or > MaximumDocumentBytes)
            throw new DeveloperPresetException(DeveloperFailureReason.InvalidValue, "Preset file must be between 1 byte and 64 KiB.");
        try
        {
            using JsonDocument json = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions
                { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = MaximumDepth });
            JsonElement root = json.RootElement;
            var fields = Properties(root, "schemaVersion", "key", "displayName", "buildRevision", "exportedAtUtc",
                "baseScenarioKey", "baseScenarioIdentity", "baseProfile", "seed", "participants", "initialOverrides");
            if (Int32(fields["schemaVersion"], "schemaVersion") != DeveloperPresetDocument.CurrentSchema)
                throw Invalid("Unknown preset schema version.");
            string key = Text(fields["key"], "key", DeveloperPresetDocument.MaximumKeyLength);
            string name = Text(fields["displayName"], "displayName", DeveloperPresetDocument.MaximumDisplayNameLength);
            string build = Text(fields["buildRevision"], "buildRevision", 64);
            string exported = Text(fields["exportedAtUtc"], "exportedAtUtc", 40);
            if (!DeveloperPresetCodec.IsPresetKey(key)) throw Invalid("Preset key is invalid.");
            if (!DateTimeOffset.TryParseExact(exported, "O", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp) || timestamp.Offset != TimeSpan.Zero)
                throw Invalid("Preset export timestamp is invalid.");
            string scenarioKey = Text(fields["baseScenarioKey"], "baseScenarioKey", MaximumScenarioKeyLength);
            ScenarioIdentity identity = ReadIdentity(fields["baseScenarioIdentity"]);
            MatchScenarioProfile profile = Text(fields["baseProfile"], "baseProfile", 24) switch
            {
                "Gameplay" => MatchScenarioProfile.Gameplay,
                "Validation" => MatchScenarioProfile.Validation,
                _ => throw Invalid("Unknown base profile.")
            };
            ulong seed = UInt64(fields["seed"], "seed");
            MatchParticipantConfiguration[] participants = ReadParticipants(fields["participants"]);
            var overrides = ReadOverrides(fields["initialOverrides"], participants);
            var metadata = new DeveloperPresetMetadata(DeveloperPresetMetadata.CurrentSchema, scenarioKey, identity,
                seed, profile, participants, overrides.Length != 0);
            var initial = new DeveloperInitialProfile(metadata, overrides.Select(value =>
                new DeveloperEffectiveParameter(value.Id, value.Target, value.Value,
                    new(DeveloperRevisionDomain.Parameters, 0))).ToArray());
            var document = new DeveloperPresetDocument(key, name, initial, build, timestamp);
            ValidateAndBuildPlan(initial, resolveComposition);
            return document;
        }
        catch (DeveloperPresetException) { throw; }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        { throw new DeveloperPresetException(DeveloperFailureReason.InvalidValue, "Preset is invalid or incompatible with this build.", error); }
    }

    public static DeveloperRestartPlan ValidateAndBuildPlan(DeveloperInitialProfile profile,
        Func<string, MatchComposition>? resolveComposition = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var metadata = profile.Metadata;
        if (profile.Parameters.Count > DeveloperPresetDocument.MaximumOverrides)
            throw Invalid("Preset supports at most 64 initial overrides.");
        MatchComposition composition;
        try { composition = (resolveComposition ?? ScenarioRegistry.Default.Resolve)(metadata.CompositionKey); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException)
        { throw new DeveloperPresetException(DeveloperFailureReason.InvalidTarget, "Registered base scenario or content is unavailable or incompatible.", error); }
        if (composition.Key != metadata.CompositionKey || composition.Identity != metadata.BaseIdentity)
            throw new DeveloperPresetException(DeveloperFailureReason.InvalidTarget, "Preset base identity differs from the registered scenario.");
        var basis = CentralDivideScenario.CreateHeadless(metadata.Profile, metadata.Seed, composition: composition) with
        { Participants = Array.AsReadOnly(metadata.Participants.ToArray()) };
        var overrides = profile.Parameters.Select(value => new DeveloperTuningOverride(value.Id, value.Target, value.Value)).ToArray();
        if (!DeveloperRestartBuilder.TryBuild(basis, metadata, overrides, out var plan, out var failure))
            throw new DeveloperPresetException(failure == DeveloperFailureReason.None ? DeveloperFailureReason.InvalidValue : failure,
                "Preset parameter profile is invalid for its participants or registered scenario.");
        return plan!;
    }

    private static DeveloperTuningOverride[] ReadOverrides(JsonElement element, MatchParticipantConfiguration[] participants)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > DeveloperPresetDocument.MaximumOverrides)
            throw Invalid("Preset supports at most 64 overrides.");
        var result = new List<DeveloperTuningOverride>(element.GetArrayLength());
        var seen = new HashSet<(DeveloperParameterId, DeveloperTarget)>();
        foreach (var item in element.EnumerateArray())
        {
            var fields = Properties(item, "key", "kind", "scope", "playerId", "value");
            string key = Text(fields["key"], "override key", 96);
            var descriptor = DeveloperGameplayTuningSchema.Entries.SingleOrDefault(entry => entry.Contract.Key == key)
                ?? throw new DeveloperPresetException(DeveloperFailureReason.UnsupportedParameter, $"Unknown developer parameter '{key}'.");
            DeveloperValueKind kind = Text(fields["kind"], "kind", 24) switch
            {
                "Boolean" => DeveloperValueKind.Boolean,
                "UnsignedNumber" => DeveloperValueKind.UnsignedNumber,
                "RealNumber" => DeveloperValueKind.RealNumber,
                _ => throw Invalid("Unknown parameter value kind.")
            };
            if (kind != descriptor.Contract.Default.Kind) throw Invalid("Parameter value kind does not match its registered contract.");
            string scope = Text(fields["scope"], "scope", 12);
            DeveloperTarget target = scope switch
            {
                "global" when fields["playerId"].ValueKind == JsonValueKind.Null => DeveloperTarget.Global,
                "player" when fields["playerId"].ValueKind == JsonValueKind.Number => DeveloperTarget.ForPlayer(new PlayerId(UInt64(fields["playerId"], "playerId"))),
                "global" or "player" => throw Invalid("Preset parameter scope fields are inconsistent."),
                _ => throw Invalid("Unknown parameter scope.")
            };
            DeveloperValue value = ReadValue(fields["value"], kind);
            var tuning = new DeveloperTuningOverride(descriptor.Contract.Id, target, value);
            DeveloperFailureReason failure = DeveloperGameplayTuningSchema.Validate(tuning, participants);
            if (failure != DeveloperFailureReason.None)
                throw new DeveloperPresetException(failure, "Preset parameter value or scope violates its registered bounds.");
            if (!seen.Add((tuning.Id, tuning.Target))) throw Invalid("Preset contains duplicate parameter targets.");
            result.Add(tuning);
        }
        return result.ToArray();
    }

    private static MatchParticipantConfiguration[] ReadParticipants(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2)
            throw Invalid("Preset must contain exactly two registered participants.");
        var participants = new List<MatchParticipantConfiguration>(2);
        foreach (var item in element.EnumerateArray())
        {
            var fields = Properties(item, "playerId", "factionId", "startIndex", "computerControlled");
            ulong player = UInt64(fields["playerId"], "playerId");
            ulong faction = UInt64(fields["factionId"], "factionId");
            int start = Int32(fields["startIndex"], "startIndex");
            bool computer = Boolean(fields["computerControlled"], "computerControlled");
            if (player is < 1 or > 2 || faction is < 1 or > 2 || start is < 0 or > 1 || player != faction)
                throw Invalid("Participant is not permitted by the registered scenario contract.");
            participants.Add(new(new PlayerId(player), new FactionId((uint)faction), start, computer));
        }
        if (participants.Select(p => p.Player).Distinct().Count() != 2 || participants.Select(p => p.Faction).Distinct().Count() != 2 ||
            participants.Select(p => p.StartIndex).Distinct().Count() != 2)
            throw Invalid("Preset contains duplicate participants or starts.");
        return participants.OrderBy(p => p.StartIndex).ToArray();
    }

    private static ScenarioIdentity ReadIdentity(JsonElement element)
    {
        var fields = Properties(element, "mapKey", "mapRevision", "mapSha256", "terrainVersion", "contentKey", "contentSha256", "startRule", "objectiveRule");
        string mapKey = Text(fields["mapKey"], "mapKey", MaximumIdentityTextLength);
        int mapRevision = Int32(fields["mapRevision"], "mapRevision");
        string mapHash = Text(fields["mapSha256"], "mapSha256", 64);
        int terrainVersion = Int32(fields["terrainVersion"], "terrainVersion");
        string contentKey = Text(fields["contentKey"], "contentKey", MaximumIdentityTextLength);
        string contentHash = Text(fields["contentSha256"], "contentSha256", 64);
        string startRule = Text(fields["startRule"], "startRule", MaximumIdentityTextLength);
        string objectiveRule = Text(fields["objectiveRule"], "objectiveRule", MaximumIdentityTextLength);
        if (mapRevision <= 0 || terrainVersion <= 0 || !IsSha256(mapHash) || !IsSha256(contentHash)) throw Invalid("Scenario identity fields are invalid.");
        return new(mapKey, mapRevision, mapHash, terrainVersion, contentKey, contentHash, startRule, objectiveRule);
    }

    private static void WriteValue(Utf8JsonWriter writer, DeveloperValue value)
    {
        switch (value.Kind)
        {
            case DeveloperValueKind.Boolean: writer.WriteBooleanValue(value.AsBoolean()); break;
            case DeveloperValueKind.UnsignedNumber: writer.WriteNumberValue(value.AsUInt32()); break;
            case DeveloperValueKind.RealNumber:
                double number = value.AsDouble();
                if (!double.IsFinite(number)) throw Invalid("Preset values must be finite.");
                writer.WriteNumberValue(number); break;
            default: throw Invalid("Unsupported preset value kind.");
        }
    }

    private static DeveloperValue ReadValue(JsonElement element, DeveloperValueKind kind) => kind switch
    {
        DeveloperValueKind.Boolean => DeveloperValue.FromBoolean(Boolean(element, "value")),
        DeveloperValueKind.UnsignedNumber => DeveloperValue.FromUInt32(UInt32(element, "value")),
        DeveloperValueKind.RealNumber when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double value) && double.IsFinite(value) => DeveloperValue.FromDouble(value),
        DeveloperValueKind.RealNumber => throw Invalid("Real parameter must be a finite JSON number."),
        _ => throw Invalid("Unsupported preset value kind.")
    };

    private static Dictionary<string, JsonElement> Properties(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid("Expected a JSON object.");
        var allowed = required.ToHashSet(StringComparer.Ordinal);
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)) throw Invalid($"Unknown preset field '{property.Name}'.");
            if (!fields.TryAdd(property.Name, property.Value)) throw Invalid($"Duplicate preset field '{property.Name}'.");
        }
        if (fields.Count != required.Length || required.Any(name => !fields.ContainsKey(name))) throw Invalid("Preset is missing a required field.");
        return fields;
    }

    private static string Text(JsonElement element, string field, int maxLength)
    {
        if (element.ValueKind != JsonValueKind.String) throw Invalid($"'{field}' must be a string.");
        string value = element.GetString()!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl)) throw Invalid($"'{field}' is empty or exceeds its limit.");
        return value;
    }

    private static int Int32(JsonElement element, string field) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value) ? value : throw Invalid($"'{field}' must be an integer.");
    private static uint UInt32(JsonElement element, string field) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetUInt32(out uint value) ? value : throw Invalid($"'{field}' must be an unsigned 32-bit integer.");
    private static ulong UInt64(JsonElement element, string field) =>
        element.ValueKind == JsonValueKind.Number && element.TryGetUInt64(out ulong value) ? value : throw Invalid($"'{field}' must be an unsigned integer.");
    private static bool Boolean(JsonElement element, string field) =>
        element.ValueKind is JsonValueKind.True or JsonValueKind.False ? element.GetBoolean() : throw Invalid($"'{field}' must be a boolean.");
    private static bool IsSha256(string text) => text.Length == 64 && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static DeveloperPresetException Invalid(string message) => new(DeveloperFailureReason.InvalidValue, message);
}
#endif
