using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Logistics;
using ForgeLine.Simulation;

namespace ForgeLine.Game;

public readonly record struct BridgeCombat(FactionId Faction, uint Revision = 0)
{
    public const uint IdentityKey = 0xB1000001;
    public const double MaximumHealth = 1200, SteelCost = 120, ElectronicsCost = 20;
    public const float EngineerRange = 32, MaterialRange = 64;
}

public enum BridgeRepairFailure : byte
{
    None, ServiceUnavailable, MatchUnavailable, InvalidBridge, NotIdentified, NotRestorable,
    InvalidEngineer, ForeignEngineer, EngineerOutOfRange, EngineerBusy, OtherPlayerRepair,
    InvalidSource, ForeignSource, SourceOutOfRange, MissingMaterials, NotDamaged,
    StaleRevision, RevisionExhausted, EngineerUnavailable, DamagedAgain
}

public readonly record struct BridgeRepairWork(PlayerId Owner, EntityId Engineer, EntityId MaterialSource,
    double StartHealth, double ExpectedHealth, BridgeRepairFailure Block = BridgeRepairFailure.None);

public static class BridgeCombatInitialization
{
    public static void Initialize(EntityRegistry entities, LogisticsNetwork logistics,
        IEnumerable<EntityId> crossings, IEnumerable<FactionId> participantFactions)
    {
        var occupied = participantFactions.Select(faction => faction.Value).ToHashSet();
        uint value = uint.MaxValue;
        while (value > 0 && occupied.Contains(value)) value--;
        if (value == 0) throw new InvalidOperationException("No neutral infrastructure faction available.");
        var faction = new FactionId(value);
        foreach (EntityId entity in crossings.Take(32))
        {
            if (!entities.TryGetComponent(entity, out InfrastructurePresentationIdentity identity) ||
                identity.Kind != InfrastructurePresentationKind.RoadBridge ||
                !entities.TryGetComponent(entity, out StrategicInfrastructure infrastructure)) continue;
            Vector3 half = new(6, 3, 21);
            if (logistics.TryGetEdge(infrastructure.LogisticsEdge, out var edge) &&
                logistics.TryGetNode(edge.Source, out var source) && logistics.TryGetNode(edge.Destination, out var destination))
            {
                Vector3 span = Vector3.Abs(destination.WorldPosition - source.WorldPosition) * .5f;
                half = new(MathF.Max(6, span.X), 3, MathF.Max(6, span.Z));
            }
            entities.AddComponent(entity, new BridgeCombat(faction));
            entities.AddComponent(entity, new Combatant(faction));
            entities.AddComponent(entity, new Targetable(TargetClass.Structure));
            entities.AddComponent(entity, new TargetPriority(-100));
            entities.AddComponent(entity, new CombatHitbox(half));
            bool operational = entities.GetComponent<StrategicInfrastructureState>(entity).IsOperational;
            entities.AddComponent(entity, new HealthState(operational ? BridgeCombat.MaximumHealth : 0, BridgeCombat.MaximumHealth));
            entities.AddComponent(entity, new IntelligenceSignature(faction, BridgeCombat.IdentityKey));
        }
    }
}

public sealed class RepairBridgeCommand(StrategicInfrastructureSystem infrastructure, PlayerId issuer,
    EntityId engineer, EntityId bridge, EntityId materialSource, uint expectedRevision,
    SimulationTick submittedAtTick) : ISimulationCommand
{
    public PlayerId Issuer { get; } = issuer;
    public EntityId Engineer { get; } = engineer;
    public EntityId Bridge { get; } = bridge;
    public EntityId MaterialSource { get; } = materialSource;
    public uint ExpectedRevision { get; } = expectedRevision;
    public SimulationTick SubmittedAtTick { get; } = submittedAtTick;
    public SimulationTick ExecutedAtTick { get; private set; }
    public BridgeRepairFailure Result { get; private set; }
    public bool Accepted => ExecutedAtTick != SimulationTick.Zero && Result == BridgeRepairFailure.None;
    public void Execute(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ExecutedAtTick = context.Tick;
        Result = infrastructure.StartBridgeRepair(context, Issuer, Engineer, Bridge, MaterialSource, ExpectedRevision);
    }
}

public static class BridgeRepairText
{
    public static string Describe(BridgeRepairFailure failure) => failure switch
    {
        BridgeRepairFailure.None => "REPAIR AVAILABLE",
        BridgeRepairFailure.NotDamaged => "BRIDGE HEALTHY",
        BridgeRepairFailure.NotIdentified => "IDENTIFY BRIDGE FIRST",
        BridgeRepairFailure.InvalidEngineer => "SELECT OWN COMBAT ENGINEER",
        BridgeRepairFailure.ForeignEngineer => "ENGINEER OWNER CHANGED",
        BridgeRepairFailure.EngineerUnavailable => "ENGINEER UNAVAILABLE / RESUME WITH OWN ENGINEER",
        BridgeRepairFailure.EngineerOutOfRange => "ENGINEER MUST BE WITHIN 32m OF CROSSING",
        BridgeRepairFailure.EngineerBusy => "ENGINEER ALREADY REPAIRING ANOTHER BRIDGE",
        BridgeRepairFailure.OtherPlayerRepair => "OTHER PLAYER'S REPAIR",
        BridgeRepairFailure.MissingMaterials => "NEARBY STOCK NEEDS 120 STEEL / 20 ELECTRONICS",
        BridgeRepairFailure.InvalidSource => "OWN COMPLETED STORAGE REQUIRED",
        BridgeRepairFailure.ForeignSource => "MATERIAL SOURCE OWNER CHANGED",
        BridgeRepairFailure.SourceOutOfRange => "MATERIAL SOURCE MUST BE WITHIN 64m OF ENGINEER",
        BridgeRepairFailure.NotRestorable => "BRIDGE NOT RESTORABLE",
        BridgeRepairFailure.StaleRevision => "BRIDGE CHANGED / REVIEW AGAIN",
        BridgeRepairFailure.RevisionExhausted => "BRIDGE REVISION EXHAUSTED",
        BridgeRepairFailure.DamagedAgain => "REPAIR INTERRUPTED BY DAMAGE / KIT SPENT",
        BridgeRepairFailure.MatchUnavailable => "MATCH UNAVAILABLE",
        BridgeRepairFailure.ServiceUnavailable => "REPAIR SERVICE UNAVAILABLE",
        _ => "BRIDGE UNAVAILABLE"
    };
}
