using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class CombatDamageResolutionSystem : ISimulationSystem
{
    private readonly CombatRuntime _runtime;

    public CombatDamageResolutionSystem(
        CombatRuntime runtime)
    {
        _runtime = runtime ??
            throw new ArgumentNullException(nameof(runtime));
    }

    public SimulationPhase Phase =>
        SimulationPhase.DamageResolution;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _runtime.BeginTick(context.Tick);

        ReadOnlySpan<PendingCombatDamage> pending =
            _runtime.PendingDamage;

        for (int index = 0;
             index < pending.Length;
             index++)
        {
            PendingCombatDamage request =
                pending[index];

            if (!context.Entities.IsAlive(
                    request.Target) ||
                !context.Entities.TryGetComponent(
                    request.Target,
                    out HealthState health) ||
                health.IsDepleted)
            {
                continue;
            }

            HealthState updated =
                health.ApplyDamage(
                    request.Damage,
                    out double appliedDamage);

            if (appliedDamage <= 0.0)
            {
                continue;
            }

            context.Entities.SetComponent(
                request.Target,
                updated);
            _runtime.RecordDamage(
                request,
                appliedDamage);

            if (updated.IsDepleted)
            {
                _runtime.QueueHealthDestruction(
                    request);
            }
        }

        _runtime.ClearPendingDamage();
    }
}
