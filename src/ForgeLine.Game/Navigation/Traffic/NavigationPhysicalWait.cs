using ForgeLine.Navigation;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

// Same-tick immutable evidence of a physical veto, never persisted as authority.
internal readonly record struct NavigationPhysicalWait(SimulationTick Tick, NavigationSnapshotRevision Revision,
    NavigationVehicleObservation Owner, NavigationVehicleObservation Blocker);
