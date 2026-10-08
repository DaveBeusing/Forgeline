using BenchmarkDotNet.Attributes;
using ForgeLine.Game;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class SkirmishOpponentBenchmarks
{
    private MatchRuntime? _scenario;
    private uint _reactionCadenceTicks;

    [Params(false, true)]
    public bool TwoControllers { get; set; }

    [Params(false, true)]
    public bool DiagnosticsEnabled { get; set; }

    [Params(22, 2_002)]
    public int ScenarioAgeTicks { get; set; }

    [IterationSetup]
    public void Setup()
    {
        MatchScenarioSettings settings =
            CentralDivideScenario.CreateSettings(
                MatchScenarioProfile.Validation);
        _reactionCadenceTicks =
            settings.OpponentConfigurations[1].ReactionCadenceTicks;
        MatchRuntimeSettings runtime =
            CentralDivideScenario.CreateHeadless(
                settings.Profile,
                seed: 7331,
                enableDiagnostics: DiagnosticsEnabled,
                enableDebugCapture: DiagnosticsEnabled) with
            {
                Scenario = settings,
                Participants =
                    CentralDivideScenario.CreateDefaultParticipants(
                        westComputerControlled: true,
                        eastComputerControlled: TwoControllers)
            };

        _scenario =
            CentralDivideScenario.Create(runtime);
        _scenario.Simulation.RunTicks(
            checked((ulong)ScenarioAgeTicks));
    }

    [Benchmark]
    public SkirmishOpponentWorkMetrics EightNonDecisionTicks()
    {
        MatchRuntime scenario =
            _scenario ??
            throw new InvalidOperationException(
                "Benchmark scenario is not initialized.");

        scenario.Simulation.RunTicks(8);
        return scenario.Opponents.WorkMetrics;
    }

    [Benchmark]
    public SkirmishOpponentWorkMetrics DecisionCadenceWindow()
    {
        MatchRuntime scenario =
            _scenario ??
            throw new InvalidOperationException(
                "Benchmark scenario is not initialized.");

        scenario.Simulation.RunTicks(
            _reactionCadenceTicks);
        return scenario.Opponents.WorkMetrics;
    }

    [IterationCleanup]
    public void Cleanup()
    {
        _scenario?.Dispose();
        _scenario = null;
    }
}
