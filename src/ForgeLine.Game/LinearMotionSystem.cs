using ForgeLine.Ecs;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public sealed class LinearMotionSystem : ISimulationSystem
{
    public SimulationPhase Phase => SimulationPhase.Movement;

    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        float deltaSeconds = (float)context.TickDuration.TotalSeconds;
        if (deltaSeconds <= 0.0f)
        {
            return;
        }

        foreach (var entity in context.Entities.Query<WorldTransform, LinearVelocity>())
        {
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);
            LinearVelocity velocity =
                context.Entities.GetComponent<LinearVelocity>(entity);

            context.Entities.SetComponent(
                entity,
                transform with
                {
                    Position =
                        transform.Position +
                        velocity.UnitsPerSecond * deltaSeconds
                });
        }
    }
}
