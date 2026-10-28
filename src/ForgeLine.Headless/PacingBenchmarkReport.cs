using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.Game;

namespace ForgeLine.Headless;

internal sealed record PacingBenchmarkRun(ulong Seed, string StopReason, string ConfigurationFingerprint,
    ulong EndTick, IReadOnlyList<PacingMeasurement> Measurements, IReadOnlyList<PacingWarning> Warnings);
internal sealed record PacingBenchmarkDifference(string Name, string Owner, string Unit, double? BaselineMean,
    double? CurrentMean, double? Delta, int BaselineReached, int CurrentReached, int BaselineUnavailable, int CurrentUnavailable);
internal sealed record PacingBenchmarkReport(int SchemaVersion, string ExecutionStatus, string Context, string? GameAssemblySha256,
    PacingThresholds Thresholds, IReadOnlyList<PacingBenchmarkRun> Runs, IReadOnlyList<PacingDistribution> Distributions,
    string BaselineCompatibility, IReadOnlyList<PacingBenchmarkDifference> Differences)
{
    private static readonly JsonSerializerOptions s_failureJson = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions s_thresholdJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static PacingThresholds ReadThresholds(string? path)
    {
        if (path is null) return new();
        if (new FileInfo(path).Length > 65_536) throw new InvalidDataException("Pacing thresholds exceed 64 KiB.");
        var thresholds = JsonSerializer.Deserialize<PacingThresholds>(File.ReadAllText(path), s_thresholdJson)
            ?? throw new InvalidDataException("Pacing thresholds cannot be null.");
        thresholds.Validate(); return thresholds;
    }

    internal static PacingBenchmarkReport Create(HeadlessOptions options, IReadOnlyList<GameplayTelemetryMatch> matches,
        PacingThresholds thresholds, bool cancelled, PacingBenchmarkReport? baseline)
    {
        thresholds.Validate();
        var runs = matches.Select((match, index) =>
        {
            var snapshot = match.Telemetry;
            var rows = MatchPacingAnalysis.Measure(snapshot);
            bool terminal = snapshot.MatchResult.Lifecycle == "Completed";
            return new PacingBenchmarkRun(match.Seed, terminal ? "Terminal" : cancelled && index == matches.Count - 1 ? "Cancelled" : "TickBudget",
                snapshot.Decisions?.ConfigurationFingerprint ?? "Unavailable", snapshot.Decisions?.EndTick ?? 0, rows,
                MatchPacingAnalysis.Warnings(rows, (double)(snapshot.Decisions?.ObservedTicks ?? 0) / snapshot.TickRate,
                    thresholds, options.Profile == MatchScenarioProfile.Gameplay));
        }).ToArray();
        var distributions = MatchPacingAnalysis.Aggregate(runs.Select(r => r.Measurements).ToArray());
        string context = JsonSerializer.Serialize(new
        {
            options.ScenarioKey, Profile = options.Profile.ToString(), options.TickCount,
            Rates = matches.Select(m => m.Telemetry.TickRate).Distinct().Order().ToArray(),
            Seeds = runs.Select(r => r.Seed).ToArray(), Configurations = runs.Select(r => r.ConfigurationFingerprint).Distinct().Order(StringComparer.Ordinal).ToArray(),
            CommandSource = options.DeveloperPresetInput is null ? "Existing deterministic opponents; no external commands" : "Developer preset; cross-run comparison unavailable"
        });
        string compatibility = baseline is null ? "NoComparableBaseline" :
            baseline.SchemaVersion != 1 ? "IncompatibleSchema" : baseline.Context != context || options.DeveloperPresetInput is not null ? "IncompatibleContext" :
            cancelled || matches.Count != options.MatchCount || baseline.ExecutionStatus != "Complete" ? "PartialRun" : "Comparable";
        var differences = new List<PacingBenchmarkDifference>();
        if (compatibility == "Comparable")
        {
            var previous = baseline!.Distributions.ToDictionary(d => (d.Name, d.Owner, d.Unit));
            var current = distributions.ToDictionary(d => (d.Name, d.Owner, d.Unit));
            foreach (var key in previous.Keys.Union(current.Keys).OrderBy(k => k.Name, StringComparer.Ordinal).ThenBy(k => k.Owner, StringComparer.Ordinal))
            {
                previous.TryGetValue(key, out var a); current.TryGetValue(key, out var b);
                differences.Add(new(key.Name, key.Owner, key.Unit, a?.Mean, b?.Mean,
                    a?.Mean is { } av && b?.Mean is { } bv ? bv - av : null,
                    a?.Reached ?? 0, b?.Reached ?? 0, a?.Unavailable ?? baseline.Runs.Count, b?.Unavailable ?? runs.Length));
            }
        }
        string location = typeof(GameplayTelemetryCollector).Assembly.Location;
        string? hash = File.Exists(location) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(location))) : null;
        return new(1, cancelled ? "Cancelled" : matches.Count == options.MatchCount ? "Complete" : "Partial", context, hash, thresholds, runs, distributions, compatibility, differences);
    }

    internal static void WriteFailure(string path, HeadlessOptions options, ulong seed,
        MatchPacingObservation? completed, Exception error)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string message = error.Message.Length > 512 ? error.Message[..512] : error.Message;
        File.WriteAllText(fullPath, JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, Status = "SimulationFault", options.ScenarioKey,
            Profile = options.Profile.ToString(), Seed = seed, RequestedTicks = options.TickCount,
            LastCompletedTick = completed?.EndTick, CompletedObservation = completed,
            ErrorType = error.GetType().FullName, Message = message,
            Scope = "Last completed observation only. Failed-tick state is not captured; not a successful baseline."
        }, s_failureJson));
    }

    internal void WriteSummary(string path)
    {
        var text = new StringBuilder("# Match pacing benchmark\n\nLogical simulation time; duration windows are aspirations, not guarantees.\n\n");
        text.AppendLine("| Metric | Owner | Reached / unreached / unavailable | Mean | Median | P90 | Unit |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var row in Distributions)
            text.AppendLine(CultureInfo.InvariantCulture, $"| {row.Name} | {row.Owner} | {row.Reached} / {row.Unreached} / {row.Unavailable} | {Number(row.Mean)} | {Number(row.Median)} | {Number(row.P90)} | {row.Unit} |");
        text.AppendLine(); text.AppendLine(CultureInfo.InvariantCulture, $"Baseline: {BaselineCompatibility}.");
        foreach (var run in Runs)
        {
            text.AppendLine(); text.AppendLine(CultureInfo.InvariantCulture, $"Seed {run.Seed}: {run.StopReason}, tick {run.EndTick}.");
            foreach (var warning in run.Warnings) text.AppendLine(CultureInfo.InvariantCulture, $"- {warning.Code} / {warning.Owner}: {warning.Message}");
        }
        text.AppendLine(); text.AppendLine("Unreached events are censored at the observed tick budget. Unavailable samples do not enter numeric distributions. Quiet windows are activity proxies; pauses and missing ticks do not establish inactivity. Front centroids do not establish territorial control. Factory idle time and unit shortages require causal investigation.");
        File.WriteAllText(path, text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
        static string Number(double? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "unavailable";
    }
}
