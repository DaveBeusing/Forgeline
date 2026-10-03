using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Game;

namespace ForgeLine.Headless;

internal sealed record GameplayTelemetryBatchReport(
    int SchemaVersion,
    string Runtime,
    string OperatingSystem,
    string ProcessArchitecture,
    int ProcessorCount,
    string Profile,
    ulong RequestedTicksPerMatch,
    int RequestedMatches,
    ulong StartingSeed,
    IReadOnlyList<GameplayTelemetryMatch> Matches,
    GameplayTelemetryAnalysisResult Analysis,
    IReadOnlyList<GameplayMetricComparison> Comparison)
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new()
        {
            WriteIndented = true
        };

    public static GameplayTelemetryBatchReport Create(
        HeadlessOptions options,
        IReadOnlyList<GameplayTelemetryMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        GameplayTelemetryAnalysisResult analysis =
            GameplayTelemetryAnalysis.Aggregate(
                matches);

        IReadOnlyList<GameplayMetricComparison> comparison =
            options.TelemetryBaseline is null
                ? []
                : GameplayTelemetryAnalysis.Compare(
                    Read(
                        options.TelemetryBaseline).Analysis,
                    analysis);

        return new GameplayTelemetryBatchReport(
            GameplayTelemetryCollector.CurrentSchemaVersion,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            options.Profile.ToString(),
            options.TickCount,
            options.MatchCount,
            options.Seed,
            matches,
            analysis,
            comparison);
    }

    public static GameplayTelemetryBatchReport Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath =
            Path.GetFullPath(path);
        string json =
            File.ReadAllText(
                fullPath);
        GameplayTelemetryBatchReport? report =
            JsonSerializer.Deserialize<GameplayTelemetryBatchReport>(
                json,
                s_jsonOptions);

        if (report is null)
        {
            throw new InvalidDataException(
                $"Gameplay telemetry baseline '{fullPath}' could not be read.");
        }

        if (report.SchemaVersion !=
            GameplayTelemetryCollector.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Gameplay telemetry baseline schema {report.SchemaVersion} is incompatible with schema {GameplayTelemetryCollector.CurrentSchemaVersion}.");
        }

        return report;
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath =
            Path.GetFullPath(path);
        string? directory =
            Path.GetDirectoryName(
                fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        string json =
            JsonSerializer.Serialize(
                this,
                s_jsonOptions);
        File.WriteAllText(
            fullPath,
            json);
    }
}
