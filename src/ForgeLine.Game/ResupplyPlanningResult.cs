using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

[Flags]
public enum ResupplyProviderRejection : uint
{
    None = 0,
    NoFriendlyProvider = 1 << 0,
    SelfSupply = 1 << 1,
    Disabled = 1 << 2,
    MissingInventory = 1 << 3,
    FuelStockUnavailable = 1 << 4,
    AmmunitionStockUnavailable = 1 << 5,
    MissingPosition = 1 << 6,
    DepotNotOperational = 1 << 7,
    RecipientTravelUnavailable = 1 << 8,
    ProviderNotMobile = 1 << 9,
    ProviderRefueling = 1 << 10,
    ProviderBusy = 1 << 11,
    ProviderFuelUnavailable = 1 << 12,
    RouteRetryDeferred = 1 << 13
}

/// <summary>
/// Last normal planner result for a recipient. Rejections describe owned
/// providers only; foreign provider counts and positions are not disclosed.
/// This result does not drive resource transfers or navigation outcomes.
/// </summary>
public readonly record struct ResupplyPlanningResult(
    SimulationTick EvaluatedAtTick,
    int FriendlyProviders,
    int RejectedProviders,
    ResupplyProviderRejection Rejections,
    EntityId SelectedProvider);
