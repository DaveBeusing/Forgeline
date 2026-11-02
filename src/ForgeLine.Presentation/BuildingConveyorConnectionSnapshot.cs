using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

public enum BuildingConveyorPortRole : byte { Available, Input, Output }
public enum BuildingConveyorAttachmentState : byte { Free, Attached, Blocked, Invalid }

public readonly record struct BuildingConveyorPortView(
    string Key, string Facing, BuildingConveyorPortRole Role,
    BuildingConveyorAttachmentState Attachment, ConveyorLinkStatus? Transport,
    string Materials)
{
    public bool IsConnected => Attachment == BuildingConveyorAttachmentState.Attached;
}

/// <summary>Copied, owner-scoped port facts for one selected full-generation building.</summary>
public sealed class BuildingConveyorConnectionSnapshot
{
    public BuildingConveyorConnectionSnapshot(EntityId building, bool unavailable,
        IReadOnlyList<BuildingConveyorPortView> ports)
    {
        Building = building;
        Unavailable = unavailable;
        Ports = Array.AsReadOnly(ports.ToArray());
        foreach (BuildingConveyorPortView port in Ports)
        {
            switch (port.Role)
            {
                case BuildingConveyorPortRole.Input:
                    TotalInputs++;
                    if (port.IsConnected) ConnectedInputs++;
                    break;
                case BuildingConveyorPortRole.Output:
                    TotalOutputs++;
                    if (port.IsConnected) ConnectedOutputs++;
                    break;
                case BuildingConveyorPortRole.Available:
                    if (port.Attachment == BuildingConveyorAttachmentState.Free) AvailablePorts++;
                    break;
            }
        }
    }

    public EntityId Building { get; }
    public bool Unavailable { get; }
    public IReadOnlyList<BuildingConveyorPortView> Ports { get; }
    public int ConnectedInputs { get; private set; }
    public int TotalInputs { get; private set; }
    public int ConnectedOutputs { get; private set; }
    public int TotalOutputs { get; private set; }
    public int AvailablePorts { get; private set; }
}

internal static class BuildingConveyorConnectionCapture
{
    public static BuildingConveyorConnectionSnapshot Capture(EntityId building, PlayerId owner,
        IReadOnlyList<ConveyorBuildingPort> ports, IReadOnlyList<ConveyorOwnedPiece> pieces,
        Func<ConveyorBuildingPort, string> materials, bool unavailable = false)
    {
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(materials);
        if (unavailable) return new(building, true, []);

        var adapters = new List<ConveyorOwnedPiece>();
        foreach (ConveyorOwnedPiece piece in pieces)
            if (piece.Piece.Owner == owner && piece.Piece.Attachment == building &&
                piece.Piece.Placement.Module is ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter)
                adapters.Add(piece);

        var views = new List<BuildingConveyorPortView>();
        var seenAdaptable = new HashSet<(string Key, Vector3 Position)>();
        foreach (ConveyorBuildingPort port in ports)
        {
            if (port.Building != building || port.Owner != owner) continue;
            if (port.Adaptable && !seenAdaptable.Add((port.Key, port.Position))) continue;

            BuildingConveyorPortRole role = port.Adaptable &&
                (port.Status is ConveyorPortState.Free or ConveyorPortState.Blocked)
                ? BuildingConveyorPortRole.Available
                : port.Input ? BuildingConveyorPortRole.Input : BuildingConveyorPortRole.Output;
            int matches = 0;
            bool complementary = false;
            ConveyorLinkStatus? transport = null;
            foreach (ConveyorOwnedPiece piece in adapters)
            {
                bool intake = piece.Piece.Placement.Module == ConveyorModule.OutputAdapter;
                ConveyorSocket terminal = intake
                    ? piece.Piece.Placement.Sockets[^1] : piece.Piece.Placement.Sockets[0];
                if (Vector3.DistanceSquared(terminal.Position, port.Position) >= .0004f) continue;
                matches++;
                complementary = intake == port.Input &&
                    Vector3.Dot(terminal.Facing, port.Facing) < -.999f;
                transport = piece.Status;
            }

            BuildingConveyorAttachmentState attachment =
                matches > 1 || port.Status == ConveyorPortState.Blocked
                    ? BuildingConveyorAttachmentState.Blocked
                    : matches == 1 && complementary &&
                      (!port.Adaptable || port.Status ==
                          (port.Input ? ConveyorPortState.ConnectedInput : ConveyorPortState.ConnectedOutput))
                        ? BuildingConveyorAttachmentState.Attached
                        : matches != 0 || (port.Adaptable &&
                            (port.Status is ConveyorPortState.ConnectedInput or ConveyorPortState.ConnectedOutput))
                            ? BuildingConveyorAttachmentState.Invalid
                            : BuildingConveyorAttachmentState.Free;
            views.Add(new(port.Key, Facing(port.Facing), role, attachment,
                attachment == BuildingConveyorAttachmentState.Attached ? transport : null,
                role == BuildingConveyorPortRole.Available ? "BY DIRECTION" : materials(port)));
        }
        return new(building, false, views);
    }

    private static string Facing(Vector3 facing) =>
        MathF.Abs(facing.X) > MathF.Abs(facing.Z)
            ? facing.X >= 0 ? "E" : "W"
            : facing.Z >= 0 ? "S" : "N";
}
