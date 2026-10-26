using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal readonly record struct ConveyorEndpoint(Vector3 Position, EntityId Building, int Tier,
    Vector3 Facing = default, bool Input = false);

/// <summary>Selects one copied endpoint; the construction preview still validates the connection.</summary>
internal sealed class ConveyorEndpointPicker
{
    private const float CarrierHeight = 5.8f;
    private const float SnapRadiusPixels = 28;
    private readonly Dictionary<EntityId, RenderInstance> _visible = new();

    internal bool TryPick(PresentationSnapshot? snapshot, PlayerId player, RtsCamera camera,
        Vector2 pointer, int width, int height, float scale, bool input, out ConveyorEndpoint endpoint,
        bool includeBuildings = true)
    {
        endpoint = default;
        _visible.Clear();
        if (width <= 0 || height <= 0 || !float.IsFinite(pointer.X) || !float.IsFinite(pointer.Y) ||
            snapshot is not { SessionId.IsSpecified: true, PlayerExperience: { IsMatchComplete: false } experience,
                Conveyors: { } network } || network.Tick != snapshot.Tick || experience.Tick != snapshot.Tick ||
            !player.IsSpecified || experience.Player != player) return false;

        foreach (ref readonly var instance in snapshot.Instances)
            if (OperationsWorldOverlay.IsOwnedVisible(instance, player) &&
                ((instance.Selectable.Category & ControllableEntityCategory.Building) != 0 || instance.ConveyorFeature.IsSpecified) &&
                Finite(instance.Transform.Position) && Finite(instance.Transform.Scale))
                _visible[instance.Entity] = instance;

        CameraRay ray = camera.ScreenPointToWorldRay(pointer, width, height);
        float nearest = float.PositiveInfinity;
        EntityId chosen = default;
        ConveyorEndpoint bodyEndpoint = default;
        // Clicking any visible part of the building selects its appropriate input/output socket.
        foreach (var port in network.Ports)
        {
            if (!includeBuildings || !Usable(port, player, input) || !_visible.TryGetValue(port.Building, out var instance) ||
                (instance.Selectable.Category & ControllableEntityCategory.Building) == 0) continue;
            var bounds = PresentationBounds.ResolveAxisAlignedBounds(instance);
            if (!Finite(bounds.Minimum) || !Finite(bounds.Maximum) ||
                !SelectionPicking.TryIntersectBounds(ray, bounds.Minimum, bounds.Maximum, out float distance) ||
                !Closer(distance, port.Building, nearest, chosen)) continue;
            nearest = distance;
            chosen = port.Building;
            bodyEndpoint = new(port.Position - Vector3.UnitY * CarrierHeight, port.Building, 0, port.Facing, input);
        }
        bool bodyFound = chosen.IsValid;
        chosen = default;

        Matrix4x4 projection = camera.GetMatrices(width, height).ViewProjection;
        float radius = SnapRadiusPixels * (float.IsFinite(scale) ? Math.Clamp(scale, .5f, 4) : 1);
        nearest = radius * radius;
        foreach (var port in network.Ports)
        {
            if (!includeBuildings || !Usable(port, player, input) || !_visible.ContainsKey(port.Building) ||
                !TryScreenDistance(port.Position, projection, pointer, width, height, out float distance) ||
                !Closer(distance, port.Building, nearest, chosen)) continue;
            nearest = distance;
            chosen = port.Building;
            endpoint = new(port.Position - Vector3.UnitY * CarrierHeight, port.Building, 0, port.Facing, input);
        }
        foreach (var row in network.Pieces)
        {
            if (row.Piece.Owner != player || row.Status == ConveyorLinkStatus.Destroyed ||
                !Finite(row.Piece.Placement.Pivot) || !_visible.ContainsKey(row.Entity)) continue;
            foreach (var socket in row.Piece.Placement.Sockets)
            {
                if (socket.Input != input || !Finite(socket.Position) || !Finite(socket.Facing) ||
                    !TryScreenDistance(socket.Position, projection, pointer, width, height, out float distance) ||
                    !Closer(distance, row.Entity, nearest, chosen)) continue;
                nearest = distance;
                chosen = row.Entity;
                int rise = row.Piece.Placement.RiseMeters!=0?0:
                    (int)MathF.Round((socket.Position.Y - row.Piece.Placement.Pivot.Y - CarrierHeight) / 2);
                endpoint = new(socket.Position - Vector3.UnitY * CarrierHeight, EntityId.Invalid,
                    Math.Clamp(row.Piece.Placement.Tier + rise, 0, 1), socket.Facing, input);
            }
        }
        if (chosen.IsValid) return true;
        endpoint = bodyEndpoint;
        return bodyFound;
    }

    private static bool Usable(in ConveyorBuildingPort port, PlayerId player, bool input) =>
        port.Building.IsValid && port.Owner == player && port.Input == input &&
        Finite(port.Position) && Finite(port.Facing) && port.Facing.LengthSquared() > .5f;

    private static bool Closer(float distance, EntityId entity, float nearest, EntityId chosen) =>
        float.IsFinite(distance) && (distance < nearest || distance == nearest && chosen.IsValid && entity.CompareTo(chosen) < 0);

    private static bool TryScreenDistance(Vector3 point, Matrix4x4 projection, Vector2 pointer,
        int width, int height, out float distance)
    {
        distance = default;
        Vector4 clip = Vector4.Transform(new Vector4(point, 1), projection);
        if (!float.IsFinite(clip.W) || clip.W <= 0) return false;
        Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (!Finite(ndc) || ndc.X is < -1 or > 1 || ndc.Y is < -1 or > 1 || ndc.Z is < 0 or > 1) return false;
        Vector2 screen = new((ndc.X + 1) * .5f * width, (1 - ndc.Y) * .5f * height);
        distance = Vector2.DistanceSquared(screen, pointer);
        return float.IsFinite(distance);
    }

    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
