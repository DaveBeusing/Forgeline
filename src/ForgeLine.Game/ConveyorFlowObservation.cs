using ForgeLine.Core;
using ForgeLine.Economy;

namespace ForgeLine.Game;

/// <summary>Transient transfer evidence; never serialized as authoritative state.</summary>
public enum ConveyorBlockReason : byte
{
    None, DestroyedRoute, DestroyedSupport, NoPower, DisconnectedPort,
    IncompatibleResource, ReservedStock, DestinationFull, RetainedStock, NoInput, DamagedRoute, Unknown
}

public readonly record struct ConveyorBlockObservation(ConveyorBlockReason Reason,
    EntityId Cause, ResourceId Resource = default);

/// <summary>At most three distinct immediate facts in stable priority order.</summary>
public readonly record struct ConveyorFlowObservation(ConveyorBlockObservation First,
    ConveyorBlockObservation Second = default, ConveyorBlockObservation Third = default);
