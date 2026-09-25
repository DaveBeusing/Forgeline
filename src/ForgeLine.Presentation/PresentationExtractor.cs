using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public sealed class PresentationExtractor : ISimulationTickObserver
{
    private readonly PresentationSnapshotBuffer _buffer;
    private readonly FactionIntelligenceStore? _intelligence;
    private readonly FactionId _viewingFaction;
    private readonly AxisAlignedBounds? _intelligenceWorldBounds;

    public PresentationExtractor(
        PresentationSnapshotBuffer buffer,
        FactionIntelligenceStore? intelligence = null,
        FactionId viewingFaction = default,
        AxisAlignedBounds? intelligenceWorldBounds = null)
    {
        _buffer =
            buffer ??
            throw new ArgumentNullException(nameof(buffer));
        _intelligence = intelligence;
        _viewingFaction = viewingFaction;
        _intelligenceWorldBounds = intelligenceWorldBounds;

        if (_intelligence is not null &&
            !_viewingFaction.IsSpecified)
        {
            throw new ArgumentException(
                "Faction-specific intelligence extraction requires a viewing faction.",
                nameof(viewingFaction));
        }
    }

    public void OnTickCompleted(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        FactionIntelligenceSnapshot? intelligenceSnapshot =
            CaptureIntelligence();

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
                    ReadOnlySpan<RenderInstance>.Empty,
                    intelligenceSnapshot));
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
            if (!IsVisibleToViewer(
                    context.Entities,
                    entity))
            {
                continue;
            }

            WorldTransform transform =
                context.Entities.GetComponent<WorldTransform>(entity);
            VisualIdentity visual =
                context.Entities.GetComponent<VisualIdentity>(entity);

            RenderVisibilityMask visibility =
                (RenderVisibilityMask)(uint)visual.Visibility;

            SelectablePresentationMetadata selectable =
                context.Entities.TryGetComponent(
                    entity,
                    out ControllableEntity controllable) &&
                controllable.IsControllable
                    ? new SelectablePresentationMetadata(
                        controllable.Owner,
                        controllable.Category)
                    : SelectablePresentationMetadata.None;

            instances[index++] = new RenderInstance(
                entity,
                new RenderTransform(
                    transform.Position,
                    transform.Rotation,
                    transform.Scale),
                new RenderMeshHandle(visual.VisualId),
                RenderMaterialHandle.Default,
                visibility,
                entity.Index,
                selectable);
        }

        _buffer.Publish(
            new PresentationSnapshot(
                context.Tick,
                context.TickDuration,
                context.Entities.EntityCount,
                instances.AsSpan(0, index),
                intelligenceSnapshot));
    }

    private bool IsVisibleToViewer(
        EntityRegistry entities,
        EntityId entity)
    {
        if (_intelligence is null ||
            !_viewingFaction.IsSpecified ||
            !entities.TryGetComponent(
                entity,
                out IntelligenceSignature signature) ||
            signature.Faction == _viewingFaction)
        {
            return true;
        }

        return _intelligence.IsEntityCurrentlyIdentified(
            _viewingFaction,
            entity);
    }

    private FactionIntelligenceSnapshot? CaptureIntelligence()
    {
        if (_intelligence is null ||
            !_viewingFaction.IsSpecified)
        {
            return null;
        }

        return _intelligenceWorldBounds is AxisAlignedBounds bounds
            ? _intelligence.Capture(
                _viewingFaction,
                bounds)
            : _intelligence.Capture(
                _viewingFaction);
    }
}
