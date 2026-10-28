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
    IReadOnlyList<GameplayMetricComparison> Comparison,
    string SourceIdentity)
{
    public PacingBenchmarkReport? PacingBenchmarks { get; init; }

    private static readonly JsonSerializerOptions s_jsonOptions =
        new()
        {
            WriteIndented = true
        };

    public static GameplayTelemetryBatchReport Create(
        HeadlessOptions options,
        IReadOnlyList<GameplayTelemetryMatch> matches, PacingThresholds? thresholds = null, bool cancelled = false)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var baseline = options.TelemetryBaseline is null ? null : Read(options.TelemetryBaseline);
        GameplayTelemetryAnalysisResult analysis =
            GameplayTelemetryAnalysis.Aggregate(
                matches);

        var pacing = PacingBenchmarkReport.Create(options, matches, thresholds ?? new(), cancelled, baseline?.PacingBenchmarks);
        IReadOnlyList<GameplayMetricComparison> comparison =
            options.TelemetryBaseline is null || (baseline?.PacingBenchmarks is not null && pacing.BaselineCompatibility != "Comparable")
                ? []
                : GameplayTelemetryAnalysis.Compare(
                    baseline!.Analysis,
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
            comparison,
            typeof(GameplayTelemetryBatchReport).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion)
        { PacingBenchmarks = pacing };
    }

    public static GameplayTelemetryBatchReport Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath =
            Path.GetFullPath(path);
        if (new FileInfo(fullPath).Length > 33_554_432) throw new InvalidDataException("Telemetry baseline exceeds 32 MiB.");
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
        PacingBenchmarks?.WriteSummary(fullPath + ".pacing.md");
    }
}
