using ForgeLine.Simulation;

namespace ForgeLine.Presentation;

public readonly record struct EarlyGameGuidanceView(bool Visible, string Objective, string Context, string Binding, string Next,
    SimulationSessionId SessionId = default);

internal readonly record struct GuidanceStep(PlayerGuidanceMilestone Milestone, string Objective, string Context, string Binding);

internal static class EarlyGameGuidanceCatalog
{
    internal static readonly GuidanceStep[] Steps =
    [
        new(PlayerGuidanceMilestone.CommandCore, "ESTABLISH COMMAND CORE", "Your core holds starting materials and supply.", "B BUILD / COMMAND CORE"),
        new(PlayerGuidanceMilestone.Power, "ESTABLISH POWER", "Industry needs power; build a Power Plant.", "B BUILD / POWER PLANT"),
        new(PlayerGuidanceMilestone.FerrousExtraction, "EXTRACT FERROUS ORE", "Place a Mine / Extractor on Ferrous Ore.", "B BUILD / MINE / EXTRACTOR"),
        new(PlayerGuidanceMilestone.SteelProcessing, "PROCESS STEEL", "Build a Smelter; deliver Ore to its input.", "L LOGISTICS / SELECT SMELTER / P PROCESS"),
        new(PlayerGuidanceMilestone.VehicleFactory, "BUILD VEHICLE FACTORY", "Starting stock can fund industry in any order.", "B BUILD / VEHICLE FACTORY"),
        new(PlayerGuidanceMilestone.Scout, "FIELD A SCOUT VEHICLE", "Deliver factory materials; queue a Scout.", "L LOGISTICS / SELECT FACTORY / U UNITS"),
        new(PlayerGuidanceMilestone.Supply, "MAKE SUPPLY AVAILABLE", "Stock a core or Supply Depot with Fuel and Ammo.", "L LOGISTICS / Y SUPPLY"),
        new(PlayerGuidanceMilestone.OpponentContact, "SCOUT THE OPPONENT", "Move scouts to gain permitted contact information.", "RIGHT CLICK MOVE / F11 MINIMAP"),
        new(PlayerGuidanceMilestone.None, "DEFEAT THE ENEMY COMMAND CORE", "Keep forces supplied; the match decides victory.", "K COMBAT / F1 HELP")
    ];
}

/// <summary>Advisory observations only; no commands, saved achievements or match authority.</summary>
public sealed class EarlyGameGuidanceController
{
    private SimulationSessionId _session;
    private SimulationTick _tick;
    private PlayerGuidanceMilestone _observed;

    public PlayerGuidanceMilestone Observed => _observed;

    public EarlyGameGuidanceView Update(PresentationSnapshot? snapshot, bool enabled, bool blocked = false)
    {
        if (snapshot is null || !snapshot.SessionId.IsSpecified) return default;
        if (_session != snapshot.SessionId)
        { _session = snapshot.SessionId; _tick = default; _observed = default; }
        if (snapshot.PlayerExperience is not { IsMatchComplete: false } experience ||
            snapshot.Guidance is not { } summary || summary.SessionId != snapshot.SessionId ||
            summary.Tick != snapshot.Tick || summary.Player != experience.Player || snapshot.Tick.Value < _tick.Value) return default;
        _tick = snapshot.Tick;
        _observed |= summary.Observed;
        if (!enabled || blocked) return default;
        int current = 0;
        while (current < EarlyGameGuidanceCatalog.Steps.Length - 1 &&
            (_observed & EarlyGameGuidanceCatalog.Steps[current].Milestone) != 0) current++;
        int next = current + 1;
        while (next < EarlyGameGuidanceCatalog.Steps.Length - 1 &&
            (_observed & EarlyGameGuidanceCatalog.Steps[next].Milestone) != 0) next++;
        var step = EarlyGameGuidanceCatalog.Steps[current];
        return new(true, step.Objective, step.Context, step.Binding,
            next < EarlyGameGuidanceCatalog.Steps.Length ? EarlyGameGuidanceCatalog.Steps[next].Objective : string.Empty, _session);
    }
}
