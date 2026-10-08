using ForgeLine.Benchmarks;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Jobs;

namespace ForgeLine.Simulation.Benchmarks;

internal static class SimulationScalabilityMeasurements
{
    public static void Run(string output, int samples, bool phaseTiming = true)
    {
        var results = new List<object>();
        foreach (int count in new[] { 0, 1000, 10000 })
        {
            var simulation = new SimulationCoordinator(seed: 2026,
                initialEntityCapacity: Math.Max(256, count),
                diagnosticsOptions: new SimulationDiagnosticsOptions { Enabled = true, TrackPhaseTiming = phaseTiming });
            for (int i = 0; i < count; i++)
                simulation.Entities.AddComponent(simulation.Entities.CreateEntity(), new ScalePosition(i));
            if (count > 0) for (int i = 0; i < 4; i++) simulation.RegisterSystem(new IteratePositions());
            results.Add(MeasureTicks($"lightweight-{count}-four-systems", "Synthetic ECS iteration; not active gameplay", simulation, samples));
            if (simulation.Entities.EntityCount != count) throw new InvalidOperationException("Lightweight entity count changed.");
        }
        var movement = new GroundMovementBenchmarks();
        movement.Setup();
        results.Add(ScalabilityMeasurement.Measure("active-movement-1000-tick", "One 20-Hz movement tick; 1000 active entities; reset outside each sample",
            movement.MoveOneThousandEntitiesForOneTick, samples: Math.Min(samples, 1024),
            budgetMilliseconds: 50, prepare: movement.ResetScenario, counters: () => new { ActualEntities = movement.ActualEntities, TickRate = 20 }));
        results.Add(ScalabilityMeasurement.Measure("active-movement-1000-ten-ticks", "Ten 20-Hz movement ticks; reset outside each sample; no per-tick percentile claim",
            movement.MoveOneThousandEntitiesForTenTicks, samples: Math.Min(samples, 128),
            budgetMilliseconds: 500, prepare: movement.ResetScenario));
        var combat = new CombatWeaponBenchmarks { ArmedEntityCount = 1000 };
        combat.Setup();
        results.Add(ScalabilityMeasurement.Measure("combat-1000-weapons", "One 20-Hz tick; 1000 shooters plus 1000 high-health targets, ample finite ammunition",
            combat.ExecuteDirectFireTick, samples, budgetMilliseconds: 50));
        var cargo = new CargoTransportBenchmarks { TransportCount = 500 };
        results.Add(ScalabilityMeasurement.Measure("logistics-500-deliveries", "Complete physical delivery cycle, at most 300 ticks; fresh scenario outside each sample; not one tick",
            cargo.RunCargoTransportBatch, samples: Math.Min(samples, 32), prepare: cargo.Setup));
        using (var jobs = new JobSchedulerBenchmarks { ItemCount = 65536, BatchSize = 1024, WorkerCount = Math.Min(4, Environment.ProcessorCount) })
        {
            jobs.Setup();
            results.Add(ScalabilityMeasurement.Measure("scheduler-65536-parallel", "Synthetic parallel range including wait; not a gameplay tick",
                jobs.ParallelRange, samples, counters: () => jobs.Metrics));
        }
        foreach (var profile in new[] { MatchScenarioProfile.Gameplay, MatchScenarioProfile.Validation })
        {
            using var match = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(profile, 2026, enableDiagnostics: true)
                with
            { EnablePhaseTiming = phaseTiming });
            results.Add(MeasureTicks($"central-divide-{profile.ToString().ToLowerInvariant()}",
                profile == MatchScenarioProfile.Gameplay ? "Production gameplay profile; two opponents; no graphics resources" : "Accelerated validation profile; not production gameplay performance",
                match.Simulation, samples, () => match.Simulation.Entities.GetComponent<MatchState>(match.BattlefieldRuntime.MatchStateEntity).Lifecycle.ToString()));
        }
        ScalabilityMeasurement.Write(output, "Headless CPU; simulation/tick phases and scheduler. No graphics or presentation references.",
            results, [typeof(SimulationCoordinator).Assembly.Location, typeof(MatchRuntime).Assembly.Location, typeof(SimulationScalabilityMeasurements).Assembly.Location]);
    }

    private static object MeasureTicks(string name, string scope, SimulationCoordinator simulation, int samples, Func<string>? matchLifecycle = null)
    {
        SimulationPhase[] phases = SimulationPhaseOrder.All.ToArray();
        var phaseSamples = phases.Select(_ => new double[samples]).ToArray();
        var observers = new double[samples];
        object result = ScalabilityMeasurement.Measure(name, scope, () => { simulation.AdvanceOneTick(); return simulation.CurrentTick.Value; },
            samples, budgetMilliseconds: 50, observe: index =>
            {
                for (int p = 0; p < phases.Length; p++) phaseSamples[p][index] = simulation.Diagnostics.GetLastPhaseDuration(phases[p]).TotalMilliseconds;
                observers[index] = simulation.Diagnostics.LastTickObserversDuration.TotalMilliseconds;
            }, counters: () => new
            {
                Seed = 2026,
                TickRate = 20,
                ActualEntities = simulation.Entities.EntityCount,
                simulation.RegisteredSystemCount,
                PhaseTimingEnabled = simulation.Diagnostics.PhaseTimingEnabled,
                MatchLifecycle = matchLifecycle?.Invoke(),
                Diagnostics = simulation.Diagnostics.Capture(simulation)
            }, windowCounters: () => new { Tick = simulation.CurrentTick.Value, ActualEntities = simulation.Entities.EntityCount, simulation.PendingCommandCount, MatchLifecycle = matchLifecycle?.Invoke() });
        return new
        {
            Measurement = result,
            Phases = phases.Select((phase, i) => new
            {
                Phase = phase.ToString(),
                Timing = ScalabilityMeasurement.Summarize(phaseSamples[i]),
                RawMilliseconds = phaseSamples[i]
            }),
            TickObservers = new { Timing = ScalabilityMeasurement.Summarize(observers), RawMilliseconds = observers }
        };
    }

    private readonly record struct ScalePosition(int X);

    private sealed class IteratePositions : ISimulationSystem
    {
        public SimulationPhase Phase => SimulationPhase.Movement;
        public void Execute(SimulationContext context)
        {
            foreach (EntityId entity in context.Entities.Query<ScalePosition>())
            {
                ScalePosition position = context.Entities.GetComponent<ScalePosition>(entity);
                context.Entities.SetComponent(entity, new ScalePosition(unchecked(position.X + 1)));
            }
        }
    }
}
