using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeLine.Game;

public static class MatchPersistenceSerializer
{
    public const int CurrentFormatVersion = 1;
    public const int CurrentSchemaVersion = 1;
    public const string Magic = "FORGELINE_MATCH";

    private const long MaximumDocumentBytes =
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

    public static void WriteSave(
        string path,
        MatchSaveData save) =>
        WriteDocument(
            path,
            SerializeSave(
                save));

    public static void WriteReplay(
        string path,
        MatchReplayData replay) =>
        WriteDocument(
            path,
            SerializeReplay(
                replay));

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
        T payload)
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

        return JsonSerializer.Serialize(
            envelope,
            s_jsonOptions);
    }

    private static T Deserialize<T>(
        string document,
        MatchPersistenceDocumentKind expectedKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            document);

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

            if (!MatchesSha256(
                    envelope.Payload,
                    envelope.PayloadSha256))
            {
                throw Failure(
                    MatchPersistenceFailureReason.CorruptDocument,
                    "The match document payload checksum is invalid.");
            }

            T? payload =
                JsonSerializer.Deserialize<T>(
                    envelope.Payload,
                    s_jsonOptions);

            return payload ??
                throw Failure(
                    MatchPersistenceFailureReason.CorruptDocument,
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

    private static void WriteDocument(
        string path,
        string document)
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
            File.WriteAllText(
                temporaryPath,
                document,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));
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
        var info =
            new FileInfo(
                fullPath);

        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "The match document does not exist.",
                fullPath);
        }

        if (info.Length <= 0 ||
            info.Length >
                MaximumDocumentBytes)
        {
            throw Failure(
                MatchPersistenceFailureReason.CorruptDocument,
                $"The match document size {info.Length} is invalid.");
        }

        return File.ReadAllText(
            fullPath,
            Encoding.UTF8);
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
