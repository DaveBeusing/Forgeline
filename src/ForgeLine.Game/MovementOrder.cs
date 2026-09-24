using System.Numerics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public enum MovementOrderKind : byte
{
    Strategic = 0,
    FormationLocal = 1
}

public readonly record struct MovementOrder(
    PlayerId Issuer,
    Vector3 WorldTarget,
    SimulationTick SubmittedAtTick,
    SimulationTick AcceptedAtTick,
    MovementOrderKind Kind = MovementOrderKind.Strategic);
