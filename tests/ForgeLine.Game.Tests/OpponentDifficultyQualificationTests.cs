using System.Diagnostics;
using System.Text.Json;
using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class OpponentDifficultyQualificationTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("FORGELINE_OPPONENT_COHORT") == "1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Theory]
    [InlineData(17UL, false)]
    [InlineData(17UL, true)]
    [InlineData(2026UL, false)]
    [InlineData(2026UL, true)]
    [InlineData(831UL, false)]
    [InlineData(831UL, true)]
    public void SeededNormalStockSkirmishIsBoundedAndReportsCensoredOutcomes(ulong seed, bool swap)
    {
        if (!Enabled) Assert.Skip("Set FORGELINE_OPPONENT_COHORT=1 for the bounded full-match cohort.");
        string? output = Environment.GetEnvironmentVariable("FORGELINE_OPPONENT_COHORT_OUTPUT");
        Assert.False(string.IsNullOrWhiteSpace(output));
        var west = swap ? OpponentDifficultyId.Hard : OpponentDifficultyId.Easy;
        var east = swap ? OpponentDifficultyId.Easy : OpponentDifficultyId.Hard;
        var settings = CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, seed, enableDebugCapture: true);
        settings = settings with
        {
            Scenario = OpponentDifficultyProfiles.Apply(settings.Scenario,
                new Dictionary<ulong, OpponentDifficultyId> { [1] = west, [2] = east })
        };
        using var runtime = MatchRuntime.Create(settings, TestContext.Current.CancellationToken);
        var elapsed = Stopwatch.StartNew();
        bool terminal = runtime.RunUntil(r => r.GetMatchState().IsTerminal, 80_000, TestContext.Current.CancellationToken);
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        elapsed.Stop();
        var state = runtime.GetMatchState();
        var diagnostics = runtime.Opponents.CaptureQualificationDecisions();
        Assert.InRange(runtime.Simulation.CurrentTick.Value, 1UL, 80_000UL);
        Assert.True(terminal || runtime.Simulation.CurrentTick.Value == 80_000, "An incomplete run cannot be reported as a censored result.");
        Assert.InRange(runtime.Opponents.WorkMetrics.ScratchStatesCreated, 0, 2);
        Assert.InRange(diagnostics.Count, 0, 2);
        var report = new
        {
            SchemaVersion = 1, Seed = seed, WestProfile = west.ToString(), EastProfile = east.ToString(),
            Profile = "Gameplay", MaximumTicks = 80_000, Tick = runtime.Simulation.CurrentTick.Value,
            Terminal = terminal, Outcome = state.Outcome.ToString(), Winner = state.Winner.Value,
            Availability = terminal ? "CompletedMatch" : "CensoredAtTickLimit",
            ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            FinalStateSha256 = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256(),
            Work = runtime.Opponents.WorkMetrics, Decisions = diagnostics,
            CompletedCargoOrders = runtime.CargoTransport.Metrics.CompletedOrderCount,
            FailedRoutes = runtime.CargoTransport.Metrics.RouteFailureCount
        };
        Directory.CreateDirectory(output!);
        File.WriteAllText(Path.Combine(output!, $"seed-{seed}-swap-{swap}.json"), JsonSerializer.Serialize(report, JsonOptions));
        // Censoring is explicit; reaching a bound is never counted as a win.
        Assert.Equal(terminal, state.IsTerminal);
    }
}
