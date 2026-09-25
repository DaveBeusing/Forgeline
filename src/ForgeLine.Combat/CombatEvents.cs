using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;

namespace ForgeLine.Combat;

public enum CombatEventType : byte
{
    ShotFired = 0,
    ProjectileSpawned = 1,
    Impact = 2,
    DamageApplied = 3,
    EntityDestroyed = 4
}

public readonly record struct CombatEvent(
    SimulationTick Tick,
    CombatEventType Type,
    EntityId Source,
    EntityId Target,
    EntityId Projectile,
    WeaponId Weapon,
    Vector3 Position,
    double Amount);
