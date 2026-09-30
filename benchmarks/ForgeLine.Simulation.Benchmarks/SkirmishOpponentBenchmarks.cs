using BenchmarkDotNet.Attributes;
using ForgeLine.Game;

namespace ForgeLine.Simulation.Benchmarks;

[MemoryDiagnoser]
public class SkirmishOpponentBenchmarks
{
    private VerticalSliceScenario? _scenario;

    [Params(false, true)]
    public bool TwoControllers { get; set; }

    [Params(false, true)]
    public bool DiagnosticsEnabled { get; set; }

    [Params(22, 2_002)]
    public int ScenarioAgeTicks { get; set; }

    [IterationSetup]
    public void Setup()
    {
        VerticalSliceScenarioSettings settings =
            VerticalSliceScenarioSettings.Create(
                VerticalSliceScenarioProfile.Validation);
        VerticalSliceRuntimeSettings runtime =
            VerticalSliceRuntimeSettings.CreateHeadless(
                settings.Profile,
                seed: 7331,
                enableDiagnostics: DiagnosticsEnabled,
                enableDebugCapture: DiagnosticsEnabled) with
            {
                Scenario = settings,
                Participants =
                    VerticalSliceRuntimeSettings.CreateDefaultParticipants(
                        westComputerControlled: true,
                        eastComputerControlled: TwoControllers)
            };

        _scenario =
            VerticalSliceScenario.Create(runtime);
        _scenario.Simulation.RunTicks(
            checked((ulong)ScenarioAgeTicks));
    }

    [Benchmark]
    public SkirmishOpponentWorkMetrics EightNonDecisionTicks()
    {
        VerticalSliceScenario scenario =
            _scenario ??
            throw new InvalidOperationException(
                "Benchmark scenario is not initialized.");

        scenario.Simulation.RunTicks(8);
        return scenario.Opponents.WorkMetrics;
    }

    [IterationCleanup]
    public void Cleanup()
    {
        _scenario?.Dispose();
        _scenario = null;
    }
}
