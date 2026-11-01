using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal enum ConveyorSnapFailure : byte { None, Ambiguous, Blocked, Incompatible }

internal readonly record struct ConveyorEndpoint(Vector3 Position, EntityId Building, int Tier,
    Vector3 Facing = default, bool Input = false, string Materials = "",
    EntityId Source = default, string SocketKey = "", int SocketOrdinal = 0);

/// <summary>Selects one copied endpoint; the construction preview still validates the connection.</summary>
internal sealed class ConveyorEndpointPicker
{
    private const float CarrierHeight = 5.8f;
    private readonly Dictionary<EntityId, RenderInstance> _visible = new();
    private readonly List<ConveyorSnapSelector.Candidate> _candidates = new();
    internal ConveyorSnapFailure LastFailure { get; private set; }
    internal void ResetFailure() => LastFailure = ConveyorSnapFailure.None;

    internal bool TryPick(PresentationSnapshot? snapshot, PlayerId player, RtsCamera camera,
        Vector2 pointer, int width, int height, float scale, bool input, out ConveyorEndpoint endpoint,
        bool includeBuildings = true, ConveyorEndpoint? previous = null)
    {
        endpoint = default;
        _visible.Clear();
        _candidates.Clear();
        LastFailure = ConveyorSnapFailure.None;
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
        int bestRank = int.MaxValue;
        ConveyorEndpoint bodyEndpoint = default;
        // Whole-building clicks prefer legacy west-in/east-out, then available
        // secondary positions. Explicit socket picking below permits any free port.
        foreach (var port in network.Ports)
        {
            if (!includeBuildings || !Usable(port, player, input) ||
                !_visible.TryGetValue(port.Building, out var instance) ||
                (instance.Selectable.Category & ControllableEntityCategory.Building) == 0) continue;
            var bounds = PresentationBounds.ResolveAxisAlignedBounds(instance);
            if (!Finite(bounds.Minimum) || !Finite(bounds.Maximum) ||
                !SelectionPicking.TryIntersectBounds(ray, bounds.Minimum, bounds.Maximum, out float distance))
                continue;
            int rank = BodyRank(port, input);
            if (!Closer(distance, port.Building, nearest, chosen) &&
                !(distance == nearest && port.Building == chosen && rank < bestRank)) continue;
            nearest = distance;
            chosen = port.Building;
            bestRank = rank;
            bodyEndpoint = new(port.Position - Vector3.UnitY * CarrierHeight, port.Building, 0, port.Facing, input,
                Source: port.Building, SocketKey: port.Key);
        }
        bool bodyFound = chosen.IsValid;
        chosen = default;

        Matrix4x4 projection = camera.GetMatrices(width, height).ViewProjection;
        float radius = ConveyorSnapSelector.AcquirePixels *
            (float.IsFinite(scale) ? Math.Clamp(scale, .5f, 4f) : 1f);
        bool blocked = false, incompatible = false;
        foreach (var port in network.Ports)
        {
            if (!includeBuildings || !port.Building.IsValid || port.Owner != player ||
                !_visible.ContainsKey(port.Building) ||
                !TryScreenDistance(port.Position, projection, pointer, width, height, out float distance)) continue;
            if (port.Adaptable && port.Status != ConveyorPortState.Free)
            {
                blocked |= distance <= radius * radius;
                continue;
            }
            if (port.Input != input)
            {
                incompatible |= distance <= radius * radius;
                continue;
            }
            if (!Usable(port, player, input)) continue;
            var candidate = new ConveyorEndpoint(port.Position - Vector3.UnitY * CarrierHeight,
                port.Building, 0, port.Facing, input, Source: port.Building, SocketKey: port.Key);
            _candidates.Add(new(candidate, port.Building, port.Key, 0, distance, false));
        }
        foreach (var row in network.Pieces)
        {
            if (row.Piece.Owner != player || row.Status == ConveyorLinkStatus.Destroyed ||
                !Finite(row.Piece.Placement.Pivot) || !_visible.ContainsKey(row.Entity)) continue;
            int socketOrdinal = 0;
            foreach (var socket in row.Piece.Placement.Sockets)
            {
                int ordinal = socketOrdinal++;
                if (!Finite(socket.Position) || !Finite(socket.Facing) ||
                    !TryScreenDistance(socket.Position, projection, pointer, width, height, out float distance)) continue;
                if (socket.Input != input)
                {
                    incompatible |= distance <= radius * radius;
                    continue;
                }
                int rise = row.Piece.Placement.RiseMeters != 0 ? 0 :
                    (int)MathF.Round((socket.Position.Y - row.Piece.Placement.Pivot.Y - CarrierHeight) / 2);
                var candidate = new ConveyorEndpoint(socket.Position - Vector3.UnitY * CarrierHeight,
                    EntityId.Invalid, Math.Clamp(row.Piece.Placement.Tier + rise, 0, 1),
                    socket.Facing, input, Source: row.Entity, SocketOrdinal: ordinal);
                _candidates.Add(new(candidate, row.Entity, "", ordinal, distance, true));
            }
        }
        if (!ConveyorSnapSelector.TrySelect(_candidates, previous, scale, out endpoint,
                out bool ambiguous))
        {
            if (ambiguous || blocked || incompatible)
            {
                LastFailure = ambiguous ? ConveyorSnapFailure.Ambiguous :
                    blocked ? ConveyorSnapFailure.Blocked : ConveyorSnapFailure.Incompatible;
                return false;
            }
            if (!bodyFound) return false;
            endpoint = bodyEndpoint;
        }
        string materials = network.BeltMaterials;
        if (endpoint.Building.IsValid)
        {
            materials = "DETAILS UNAVAILABLE";
            foreach (var information in network.PortInformation)
                if (information.Building == endpoint.Building && information.Input == endpoint.Input)
                { materials = information.Materials; break; }
        }
        endpoint = endpoint with { Materials = materials.Length == 0 ? "DETAILS UNAVAILABLE" : materials };
        return true;
    }

    private static int BodyRank(in ConveyorBuildingPort port, bool input)
    {
        if (!port.Adaptable) return 0;
        if (port.Key == (input ? "west.primary" : "east.primary")) return 0;
        if (port.Key == (input ? "west.secondary" : "east.secondary")) return 1;
        if (port.Key == (input ? "east.primary" : "west.primary")) return 2;
        return 3;
    }

    private static bool Usable(in ConveyorBuildingPort port, PlayerId player, bool input) =>
        port.Building.IsValid && port.Owner == player && port.Input == input &&
        (!port.Adaptable || port.Status == ConveyorPortState.Free) &&
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
