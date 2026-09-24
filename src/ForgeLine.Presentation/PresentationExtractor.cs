using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public sealed class PresentationExtractor : ISimulationTickObserver
{
    private readonly PresentationSnapshotBuffer _buffer;

    public PresentationExtractor(PresentationSnapshotBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
    }

    public void OnTickCompleted(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int count =
            context.Entities.GetComponentCount<WorldTransform>();

        if (count == 0 ||
            context.Entities.GetComponentCount<VisualIdentity>() == 0)
        {
            _buffer.Publish(
                new PresentationSnapshot(
                    context.Tick,
                    context.TickDuration,
                    context.Entities.EntityCount,
                    ReadOnlySpan<RenderInstance>.Empty));
            return;
        }

        var instances = new RenderInstance[
            Math.Min(
                count,
                context.Entities.GetComponentCount<VisualIdentity>())];
        int index = 0;

        foreach (EntityId entity in context.Entities.Query<
                     WorldTransform,
                     VisualIdentity>(
                         QueryIterationOrder.StableByEntityIndex))
        {
            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);
            VisualIdentity visual =
                context.Entities.GetComponent<VisualIdentity>(entity);

            RenderVisibilityFlags visibility =
                (RenderVisibilityFlags)(uint)visual.Visibility;

            instances[index++] = new RenderInstance(
                entity,
                new RenderTransform(
                    transform.Position,
                    transform.Rotation,
                    transform.Scale),
                new RenderMeshHandle(visual.VisualId),
                RenderMaterialHandle.Default,
                visibility,
                entity.Index);
        }

        _buffer.Publish(
            new PresentationSnapshot(
                context.Tick,
                context.TickDuration,
                context.Entities.EntityCount,
                instances.AsSpan(0, index)));
    }
}
