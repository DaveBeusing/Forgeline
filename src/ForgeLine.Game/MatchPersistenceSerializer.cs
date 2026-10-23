using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ForgeLine.Game;

public static class MatchPersistenceSerializer
{
    public const int CurrentFormatVersion = 1;
    public const int CurrentSchemaVersion = 6;
    public const string Magic = "FORGELINE_MATCH";

    public const long MaximumDocumentBytes =
        128L * 1024L * 1024L;

    private static readonly JsonSerializerOptions s_jsonOptions =
        new()
        {
            IncludeFields = true,
            WriteIndented = true,
            NumberHandling =
                JsonNumberHandling
                    .AllowNamedFloatingPointLiterals
        };

    public static string SerializeSave(
        MatchSaveData save) =>
        Serialize(
            MatchPersistenceDocumentKind.Save,
            save);

    public static string SerializeReplay(
        MatchReplayData replay) =>
        Serialize(
            MatchPersistenceDocumentKind.Replay,
            replay);

    internal static string SerializeSave(MatchSaveData save, long maximumBytes) =>
        Serialize(MatchPersistenceDocumentKind.Save, save, maximumBytes);

    internal static string SerializeReplay(MatchReplayData replay, long maximumBytes) =>
        Serialize(MatchPersistenceDocumentKind.Replay, replay, maximumBytes);

    internal static MatchSaveData DeserializeSave(string document, long maximumBytes) =>
        Deserialize<MatchSaveData>(document, MatchPersistenceDocumentKind.Save, maximumBytes);

    internal static MatchReplayData DeserializeReplay(string document, long maximumBytes) =>
        Deserialize<MatchReplayData>(document, MatchPersistenceDocumentKind.Replay, maximumBytes);

    public static MatchSaveData DeserializeSave(
        string document) =>
        Deserialize<MatchSaveData>(
            document,
            MatchPersistenceDocumentKind.Save);

    public static MatchReplayData DeserializeReplay(
        string document) =>
        Deserialize<MatchReplayData>(
            document,
            MatchPersistenceDocumentKind.Replay);

    public static void WriteSave(string path, MatchSaveData save) =>
        WriteSaveCancellable(path, save, CancellationToken.None);

    public static void WriteSaveCancellable(
        string path,
        MatchSaveData save,
        CancellationToken cancellationToken) =>
        WriteSaveWithBudget(path, save, MaximumDocumentBytes, cancellationToken);

    internal static void WriteSaveWithBudget(string path, MatchSaveData save, long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MatchPersistenceService.ValidateSave(save);
        WriteDocument(path, SerializeSave(save, maximumBytes), cancellationToken);
    }

    public static void WriteReplay(string path, MatchReplayData replay) =>
        WriteReplayCancellable(path, replay, CancellationToken.None);

    public static void WriteReplayCancellable(
        string path,
        MatchReplayData replay,
        CancellationToken cancellationToken) =>
        WriteReplayWithBudget(path, replay, MaximumDocumentBytes, cancellationToken);

    internal static void WriteReplayWithBudget(string path, MatchReplayData replay, long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MatchPersistenceService.ValidateReplay(replay);
        WriteDocument(path, SerializeReplay(replay, maximumBytes), cancellationToken);
    }

    public static MatchSaveData ReadSave(
        string path) =>
        DeserializeSave(
            ReadDocument(
                path));

    public static MatchReplayData ReadReplay(
        string path) =>
        DeserializeReplay(
            ReadDocument(
                path));

    private static string Serialize<T>(
        MatchPersistenceDocumentKind kind,
        T payload,
        long maximumBytes = MaximumDocumentBytes)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string payloadJson =
            JsonSerializer.Serialize(
                payload,
                s_jsonOptions);
        string payloadHash =
            ComputeSha256(
                payloadJson);
        var envelope =
            new MatchPersistenceEnvelope(
                Magic,
                CurrentFormatVersion,
                kind,
                payloadJson,
                payloadHash);

        string document = JsonSerializer.Serialize(
            envelope,
            s_jsonOptions);
        CheckSize(document, maximumBytes, MatchPersistenceFailureReason.DocumentTooLarge);
        return document;
    }

    private static T Deserialize<T>(
        string document,
        MatchPersistenceDocumentKind expectedKind,
        long maximumBytes = MaximumDocumentBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            document);
        CheckSize(document, maximumBytes, MatchPersistenceFailureReason.CorruptDocument);

        try
        {
            MatchPersistenceEnvelope? envelope =
                JsonSerializer.Deserialize<
                    MatchPersistenceEnvelope>(
                        document,
                        s_jsonOptions);

            if (envelope is null ||
                !string.Equals(
                    envelope.Magic,
                    Magic,
                    StringComparison.Ordinal))
            {
                throw Failure(
                    MatchPersistenceFailureReason.CorruptDocument,
                    "The match document header is invalid.");
            }

            if (envelope.FormatVersion !=
                CurrentFormatVersion)
            {
                throw Failure(
                    MatchPersistenceFailureReason.IncompatibleVersion,
                    $"Match document format {envelope.FormatVersion} is incompatible with format {CurrentFormatVersion}.");
            }

            if (envelope.Kind !=
                expectedKind)
            {
                throw Failure(
                    MatchPersistenceFailureReason.CorruptDocument,
                    $"Expected a {expectedKind} document but found {envelope.Kind}.");
            }

            if (string.IsNullOrWhiteSpace(envelope.Payload) ||
                !MatchPersistenceValidation.Hash(envelope.PayloadSha256))
                throw Failure(MatchPersistenceFailureReason.CorruptDocument,
                    "The match document payload or checksum is missing or invalid.");

            if (!MatchesSha256(
                    envelope.Payload,
                    envelope.PayloadSha256))
            {
                throw Failure(
                    MatchPersistenceFailureReason.CorruptDocument,
                    "The match document payload checksum is invalid.");
            }

            T? payload;
            // Constructors validate persisted identities. Translate only errors from
            // materializing untrusted content, not arbitrary restoration failures.
            try
            {
                payload = JsonSerializer.Deserialize<T>(MigratePayload(envelope.Payload), s_jsonOptions);
            }
            catch (ArgumentException exception)
            {
                throw Failure(MatchPersistenceFailureReason.CorruptDocument,
                    "The match payload contains an invalid identity or value.", exception);
            }
            if (payload is MatchSaveData save) MatchPersistenceService.ValidateSave(save);
            else if (payload is MatchReplayData replay) MatchPersistenceService.ValidateReplay(replay);
            return payload ?? throw Failure(MatchPersistenceFailureReason.CorruptDocument,
                "The match document payload is missing.");
        }
        catch (MatchPersistenceException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                "The match document is not valid JSON.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                "The match document contains unsupported serialized data.",
                exception);
        }
    }

    // The envelope checksum is validated before migration. Schema 1 has no type-name
    // discriminators: only its preset opponent fields and absent composition key differ.
    private static string MigratePayload(string payload)
    {
        if (JsonNode.Parse(payload) is not JsonObject root ||
            root["SchemaVersion"] is not JsonValue schema || !schema.TryGetValue<int>(out int version))
            throw new JsonException("The match payload must be an object with an integer schema.");
        if (version is < 1 or > CurrentSchemaVersion)
            throw Failure(MatchPersistenceFailureReason.IncompatibleVersion,
                $"Match schema {version} is incompatible with schema {CurrentSchemaVersion}.");
        if (root["Configuration"] is not JsonObject configuration ||
            configuration["Scenario"] is not JsonObject scenario)
            throw new JsonException("Missing match configuration or scenario object.");
        if (version != 1)
        {
            if (configuration["CompositionKey"] is not JsonValue key ||
                !key.TryGetValue<string>(out string? identity) || string.IsNullOrWhiteSpace(identity))
            {
                throw new JsonException("Missing match composition identity.");
            }
            return payload;
        }
        if (scenario["OpponentConfigurations"] is null)
        {
            scenario["OpponentConfigurations"] = new JsonObject
            {
                ["1"] = scenario["WestOpponent"]?.DeepClone() ?? throw new JsonException("Missing west policy."),
                ["2"] = scenario["EastOpponent"]?.DeepClone() ?? throw new JsonException("Missing east policy.")
            };
            scenario.AsObject().Remove("WestOpponent");
            scenario.AsObject().Remove("EastOpponent");
        }
        configuration["CompositionKey"] ??= CentralDivideScenario.CompositionKey;
        return root.ToJsonString();
    }

    private static void WriteDocument(
        string path,
        string document,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        string fullPath =
            Path.GetFullPath(
                path);
        string? directory =
            Path.GetDirectoryName(
                fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        string temporaryPath =
            fullPath +
            ".tmp." +
            Guid.NewGuid().ToString("N");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.WriteAllText(
                temporaryPath,
                document,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                temporaryPath,
                fullPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }

    private static string ReadDocument(
        string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        string fullPath =
            Path.GetFullPath(
                path);
        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (stream.Length <= 0 ||
            stream.Length >
                MaximumDocumentBytes)
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                $"The match document size {stream.Length} is invalid; the limit is {MaximumDocumentBytes} UTF-8 bytes.");
        }

        try
        {
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException error)
        {
            throw Failure(MatchPersistenceFailureReason.CorruptDocument, "The match document is not valid UTF-8.", error);
        }
    }

    private static void CheckSize(string document, long maximumBytes, MatchPersistenceFailureReason reason)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, MaximumDocumentBytes);
        long bytes = Encoding.UTF8.GetByteCount(document);
        if (bytes > maximumBytes)
            throw Failure(reason, $"The match document requires {bytes} UTF-8 bytes; the limit is {maximumBytes}. The previous file was preserved.");
    }

    private static bool MatchesSha256(
        string payload,
        string expected)
    {
        if (string.IsNullOrWhiteSpace(
                expected))
        {
            return false;
        }

        byte[] actualBytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    payload));
        byte[] expectedBytes;

        try
        {
            expectedBytes =
                Convert.FromHexString(
                    expected);
        }
        catch (FormatException)
        {
            return false;
        }

        return actualBytes.Length ==
               expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(
                actualBytes,
                expectedBytes);
    }

    private static string ComputeSha256(
        string payload) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    payload)));

    private static MatchPersistenceException Failure(
        MatchPersistenceFailureReason reason,
        string message,
        Exception? innerException = null) =>
        new(
            reason,
            message,
            innerException);
}
