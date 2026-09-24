using System.Numerics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct MovementOrder(
    PlayerId Issuer,
    Vector3 WorldTarget,
    SimulationTick SubmittedAtTick,
    SimulationTick AcceptedAtTick);
