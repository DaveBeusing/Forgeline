using ForgeLine.Economy;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal static class MechanicalDoorPolicy
{
    public static bool CanMove(BuildingPresentationState state,
        UnitProductionStatus? units = null, ProductionStatus? processing = null) =>
        state is BuildingPresentationState.Operational or BuildingPresentationState.Idle &&
        units is not (UnitProductionStatus.Paused or UnitProductionStatus.NoPower) &&
        processing is not (ProductionStatus.Paused or ProductionStatus.NoPower);
}
