using ForgeLine.Core;
using ForgeLine.Combat;
using ForgeLine.Game;
using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

/// <summary>Selection aggregates from copied, owned, live members; missing components never contribute zero.</summary>
public sealed class SelectedCombatGroup
{
    private readonly int[] _composition = new int[8];
    public SimulationTick Tick { get; private init; }
    public int TotalCount { get; private init; }
    public int LiveCount { get; private set; }
    public int CombatCount { get; private set; }
    public int HealthCount { get; private set; }
    public int ReadinessCount { get; private set; }
    public int SupplyCount { get; private set; }
    public int PolicyCount { get; private set; }
    public bool MixedPolicy { get; private set; }
    public FirePolicy FirePolicy { get; private set; }
    public int OrderCount { get; private set; }
    public bool MixedOrder { get; private set; }
    public CombatOrderKind Order { get; private set; }
    public string PolicyLabel => PolicyCount == 0 ? "ENGAGEMENT N/A" : MixedPolicy ? "ENGAGEMENT MIXED" : FirePolicy switch
    { FirePolicy.HoldFire => "ENGAGEMENT HOLD FIRE", FirePolicy.ReturnFire => "ENGAGEMENT RETURN FIRE", _ => "ENGAGEMENT FIRE AT WILL" };
    public string ObjectiveLabel => OrderCount == 0 ? "OBJECTIVE NONE" : MixedOrder || OrderCount < CombatCount ? "OBJECTIVE MIXED" : Order switch
    { CombatOrderKind.Attack => "OBJECTIVE ATTACK", CombatOrderKind.AttackMove => "OBJECTIVE ATTACK MOVE",
      CombatOrderKind.HoldPosition => "OBJECTIVE HOLD", CombatOrderKind.Stop => "OBJECTIVE STOP", _ => "OBJECTIVE RETREAT" };
    public double Health { get; private set; }
    public double Readiness { get; private set; }
    public double Fuel { get; private set; }
    public double Ammunition { get; private set; }
    public int DamagedCount { get; private set; }
    public int UnsuppliedCount { get; private set; }
    public EntityId DamagedMember { get; private set; }
    public EntityId UnsuppliedMember { get; private set; }

    public int Composition(int index) => index is >= 0 and < 8 ? _composition[index] : 0;
    public static int TypeIndex(UnitId unit) => unit.Value is >= 1 and <= 7 ? (int)unit.Value - 1 : 7;
    public static string TypeLabel(int index) => index switch
    {
        0 => "INF",
        1 => "ENG",
        2 => "SCOUT",
        3 => "TANK",
        4 => "ART",
        5 => "CARGO",
        6 => "SUPPLY",
        _ => "OTHER"
    };

    public static CombatGroupOperationalSnapshot? Resolve(PresentationSnapshot? snapshot) =>
        snapshot is { SessionId.IsSpecified: true, CombatGroups: { } members, PlayerExperience: { IsMatchComplete: false } experience } &&
        members.SessionId == snapshot.SessionId && members.Tick == snapshot.Tick && experience.Tick == snapshot.Tick
            ? members : null;

    public static SelectedCombatGroup Create(CombatGroupOperationalSnapshot operational, SelectionSet selection)
    {
        ArgumentNullException.ThrowIfNull(operational);
        ArgumentNullException.ThrowIfNull(selection);
        var result = new SelectedCombatGroup { Tick = operational.Tick, TotalCount = selection.Count };
        foreach (EntityId entity in selection.Entities)
        {
            if (!operational.TryGet(entity, out var member)) continue;
            result.LiveCount++;
            result._composition[TypeIndex(member.Unit)]++;
            if (member.CombatEligible) result.CombatCount++;
            if (member.HasFirePolicy)
            {
                if (result.PolicyCount++ == 0) result.FirePolicy = member.FirePolicy;
                else if (result.FirePolicy != member.FirePolicy) result.MixedPolicy = true;
            }
            if (member.CombatEligible && member.HasOrder)
            {
                if (result.OrderCount++ == 0) result.Order = member.Order;
                else if (result.Order != member.Order) result.MixedOrder = true;
            }
            if (member.HasHealth)
            {
                result.HealthCount++;
                result.Health += member.HealthFraction;
                if (member.HealthFraction <= 0.25)
                {
                    result.DamagedCount++;
                    if (!result.DamagedMember.IsValid) result.DamagedMember = entity;
                }
            }
            if (member.HasReadiness) { result.ReadinessCount++; result.Readiness += member.Readiness; }
            if (member.HasSupply)
            {
                result.SupplyCount++;
                result.Fuel += member.FuelFraction;
                result.Ammunition += member.AmmunitionFraction;
                if (member.SupplyStatus is BattlefieldSupplyStatus.Critical or BattlefieldSupplyStatus.Unsupplied)
                {
                    result.UnsuppliedCount++;
                    if (!result.UnsuppliedMember.IsValid) result.UnsuppliedMember = entity;
                }
            }
        }
        if (result.HealthCount > 0) result.Health /= result.HealthCount;
        if (result.ReadinessCount > 0) result.Readiness /= result.ReadinessCount;
        if (result.SupplyCount > 0) { result.Fuel /= result.SupplyCount; result.Ammunition /= result.SupplyCount; }
        return result;
    }
}
