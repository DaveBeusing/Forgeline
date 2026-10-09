using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

internal readonly record struct ClientSimulationTelemetry(
    SimulationSessionId SessionId,
    ulong CompletedTicks,
    RuntimeSimulationState State);
