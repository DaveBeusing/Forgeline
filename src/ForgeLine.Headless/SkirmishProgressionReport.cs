using System.Text.Json;
using ForgeLine.Game;

namespace ForgeLine.Headless;

internal sealed record SkirmishProgressionReport(
    string Profile,
    int MatchIndex,
    ulong Seed,
    ulong ExecutedTicks,
    string MatchStatus,
    ulong ObservedDecisions,
    ulong DroppedHistoryEntries,
    IReadOnlyList<SkirmishDecisionDiagnostic> History,
    IReadOnlyList<SkirmishEligibilityLossDiagnostic> FirstEligibilityLosses,
    IReadOnlyList<SkirmishDecisionDiagnostic> Latest)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    public static void Write(
        string mainReportPath,
        VerticalSliceScenarioProfile profile,
        int matchIndex,
        ulong seed,
        ulong executedTicks,
        MatchState match,
        SkirmishProgressionDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainReportPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var report = new SkirmishProgressionReport(
            profile.ToString(), matchIndex, seed, executedTicks, match.Status.ToString(),
            diagnostics.ObservedDecisions, diagnostics.DroppedHistoryEntries,
            diagnostics.History.ToArray(), diagnostics.FirstEligibilityLosses.ToArray(), diagnostics.Latest);
        string path = Path.GetFullPath(Path.ChangeExtension(mainReportPath, $"progression-{matchIndex}.json"));
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(report, s_jsonOptions));
        foreach (SkirmishDecisionDiagnostic entry in report.History)
        {
            Console.WriteLine(
                $"Progression: tick={entry.Tick}; player={entry.Player}; branch={entry.Goal}; " +
                $"eligible={entry.EligibleAttackers}/{entry.MinimumAttackers}; " +
                $"excluded=scout:{entry.ScoutExclusions},readiness:{entry.ReadinessExclusions}," +
                $"fuel:{entry.FuelExclusions},ammo:{entry.AmmunitionExclusions}; resupply={entry.ActiveResupplyOrders}.");
        }

        foreach (SkirmishEligibilityLossDiagnostic loss in report.FirstEligibilityLosses)
        {
            Console.WriteLine("First eligibility loss before: " + JsonSerializer.Serialize(loss.Before));
            Console.WriteLine("First eligibility loss after: " + JsonSerializer.Serialize(loss.After));
        }

        foreach (SkirmishDecisionDiagnostic entry in report.History.TakeLast(12))
        {
            Console.WriteLine(
                $"Transport history: tick={entry.Tick}; player={entry.Player}; " +
                JsonSerializer.Serialize(entry.Economy.Transports));
        }

        foreach (SkirmishDecisionDiagnostic entry in report.Latest)
        {
            Console.WriteLine("Latest decision detail: " + JsonSerializer.Serialize(entry));
        }

        Console.WriteLine($"Progression report: {path}; omitted history entries={report.DroppedHistoryEntries}.");
    }
}
