using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public static class ConveyorNetworkOverlay
{
    public const int MaximumFocusedPieces = 32;
    public const int MaximumLabels = 8;
    public const int MaximumBuildingPorts = 64;
    public const int MaximumBeltPorts = 64;

    public static void Draw(DebugDraw draw, PresentationSnapshot? snapshot,
        IReadOnlyCollection<EntityId> selection, EntityId hovered, bool showAllPorts = false,
        Matrix4x4? viewProjection = null)
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
        DrawBuildingPorts(draw, snapshot, network, experience.Player, focus[..count], showAllPorts, viewProjection);
        int beltPorts = 0;
        foreach (var row in network.Pieces)
        {
            if (row.Piece.Owner != experience.Player || row.Status == ConveyorLinkStatus.Destroyed ||
                (!showAllPorts && !focus[..count].Contains(row.Entity)) ||
                !OperationsWorldOverlay.CanFocus(snapshot, row.Entity)) continue;
            foreach (var socket in row.Piece.Placement.Sockets)
            {
                if (beltPorts == MaximumBeltPorts) break;
                if (!Finite(socket.Position) || !Finite(socket.Facing) ||
                    socket.Facing.LengthSquared() < .5f ||
                    viewProjection.HasValue && !InView(socket.Position, viewProjection.Value)) continue;
                DrawSocket(draw, socket.Position, socket.Facing, socket.Input);
                beltPorts++;
            }
            if (beltPorts == MaximumBeltPorts) break;
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

    private static void DrawBuildingPorts(DebugDraw draw, PresentationSnapshot snapshot,
        ConveyorConstructionReadModel network, PlayerId player, ReadOnlySpan<EntityId> focus, bool showAll,
        Matrix4x4? viewProjection)
    {
        int drawn = 0;
        // Focused buildings retain their markers when a large construction network reaches the budget.
        foreach (var entity in focus)
            foreach (var port in network.Ports)
                if (port.Building == entity && CanDraw(port))
                {
                    DrawPort(draw, port);
                    if (++drawn == MaximumBuildingPorts) return;
                }
        if (!showAll) return;
        foreach (var port in network.Ports)
        {
            if (focus.Contains(port.Building) || !CanDraw(port)) continue;
            DrawPort(draw, port);
            if (++drawn == MaximumBuildingPorts) return;
        }

        bool CanDraw(ConveyorBuildingPort port) => port.Building.IsValid && port.Owner == player &&
            Finite(port.Position) && Finite(port.Facing) &&
            new Vector2(port.Facing.X, port.Facing.Z).LengthSquared() > .5f &&
            (!viewProjection.HasValue || InView(port.Position, viewProjection.Value)) &&
            OperationsWorldOverlay.CanFocus(snapshot, port.Building);
    }

    private static bool InView(Vector3 position, Matrix4x4 projection)
    {
        Vector4 clip = Vector4.Transform(new Vector4(position, 1), projection);
        return float.IsFinite(clip.X) && float.IsFinite(clip.Y) && float.IsFinite(clip.Z) &&
            float.IsFinite(clip.W) && clip.W > 0 && clip.X >= -clip.W && clip.X <= clip.W &&
            clip.Y >= -clip.W && clip.Y <= clip.W && clip.Z >= 0 && clip.Z <= clip.W;
    }

    private static void DrawPort(DebugDraw draw, in ConveyorBuildingPort port) =>
        DrawSocket(draw, port.Position, port.Facing, port.Input);

    internal static void DrawSocket(DebugDraw draw, Vector3 position, Vector3 facing, bool input, string? label = null)
    {
        Vector3 outward = Vector3.Normalize(new Vector3(facing.X, 0, facing.Z));
        Vector3 side = Vector3.Cross(Vector3.UnitY, outward);
        Vector4 color = input ? new(.2f, .8f, 1, 1) : new(1, .72f, .18f, 1);
        Vector3 center = position;
        Vector3 up = Vector3.UnitY * .75f;
        Vector3 across = side * .75f;
        draw.Line(center - across - up, center + across - up, color);
        draw.Line(center + across - up, center + across + up, color);
        draw.Line(center + across + up, center - across + up, color);
        draw.Line(center - across + up, center - across - up, color);
        Vector3 inner = center + outward * .2f;
        Vector3 outer = center + outward * 3;
        Vector3 start = input ? outer : inner;
        Vector3 tip = input ? inner : outer;
        Vector3 direction = input ? -outward : outward;
        draw.Line(start, tip, color);
        draw.Line(tip, tip - direction * .9f + side * .65f, color);
        draw.Line(tip, tip - direction * .9f - side * .65f, color);
        draw.Label(center + Vector3.UnitY * 1.15f + outward * .3f, label ?? (input ? "IN" : "OUT"), color);
    }

    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
