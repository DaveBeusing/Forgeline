using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class ConveyorNetworkOverlay
{
    public const int MaximumFocusedPieces = 32;
    public const int MaximumLabels = 8;

    public static void Draw(DebugDraw draw, PresentationSnapshot? snapshot,
        IReadOnlyCollection<EntityId> selection, EntityId hovered)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ArgumentNullException.ThrowIfNull(selection);
        if (!draw.Enabled || snapshot?.Conveyors is not { } network || network.Tick != snapshot.Tick ||
            !snapshot.SessionId.IsSpecified || snapshot.PlayerExperience is not { IsMatchComplete: false } experience ||
            experience.Tick != snapshot.Tick || !experience.Player.IsSpecified) return;
        Span<EntityId> focus = stackalloc EntityId[MaximumFocusedPieces];
        int count = 0, labels = 0;
        if (hovered.IsValid && snapshot.Hover is { } hover && hover.Entity == hovered &&
            hover.SessionId == snapshot.SessionId && hover.Tick == snapshot.Tick && hover.OwnedDetails.Count == 1)
            focus[count++] = hovered;
        if (snapshot.SelectedOrders is { } orders && orders.Tick == snapshot.Tick &&
            orders.MatchesSelection(snapshot.SessionId, selection))
            foreach (var entity in selection)
            {
                if (count == MaximumFocusedPieces) break;
                if (!focus[..count].Contains(entity)) focus[count++] = entity;
            }
        foreach (var entity in focus[..count])
        {
            var row = network.Pieces.FirstOrDefault(p => p.Entity == entity);
            if (row.Piece.Owner != experience.Player || row.Flow is not { } flow ||
                !OperationsWorldOverlay.CanFocus(snapshot, entity) ||
                !Finite(row.Piece.Placement.Bounds.Minimum) || !Finite(row.Piece.Placement.Bounds.Maximum)) continue;
            Vector4 color = row.Status == ConveyorLinkStatus.Flowing ? new(.2f, 1, .5f, .8f) : new(1, .7f, .1f, .8f);
            draw.Box(row.Piece.Placement.Bounds,color);
            Vector3 center=row.Piece.Placement.Bounds.Center+Vector3.UnitY*.6f;
            if (labels++ < MaximumLabels)
                draw.Label(center,FormattableString.Invariant($"{row.Status} | {flow.OutputPerSecond:0.#}/s | {row.Piece.BufferedQuantity:0.#}/{flow.Capacity}"),color);
            for (int output = 0; output < 2; output++)
            {
                EntityId target = output == 0 ? flow.OutputA : flow.OutputB;
                if (!target.IsValid || !OperationsWorldOverlay.CanFocus(snapshot, target)) continue;
                var next=network.Pieces.FirstOrDefault(p=>p.Entity==target);
                Vector3? position = next.Entity.IsValid && next.Piece.Owner == experience.Player ? next.Piece.Placement.Bounds.Center :
                    network.Ports.Where(p => p.Building == target && p.Owner == experience.Player && p.Input)
                        .Select(p => (Vector3?)p.Position).FirstOrDefault();
                if (position is { } end && Finite(end)) draw.Line(center, end + Vector3.UnitY * .6f, color);
            }
        }
    }

    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
