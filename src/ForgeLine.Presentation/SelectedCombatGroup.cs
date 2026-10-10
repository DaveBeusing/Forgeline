using ForgeLine.Core;
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

public static class CombatGroupCardLayout
{
    public static float Scale(in GameplayHudLayout layout) =>
        MathF.Min(layout.Scale, MathF.Min(layout.SelectionInspector.Width / 360, layout.SelectionInspector.Height / 112));

    public static HudRect Control(in GameplayHudLayout layout, int index)
    {
        if (index is < 0 or > 11 || layout.SelectionInspector.IsEmpty) return default;
        float scale = Scale(layout);
        HudRect area = layout.SelectionInspector;
        int columns = index < 8 ? 4 : 2;
        int column = index < 8 ? index % 4 : (index - 8) % 2;
        float y = index < 8 ? 42 + index / 4 * 12 : index < 10 ? 68 : 82;
        float width = (area.Width - 12 * scale) / columns;
        return new(area.X + 6 * scale + column * width, area.Y + y * scale, width - 2 * scale, 11 * scale);
    }
}
