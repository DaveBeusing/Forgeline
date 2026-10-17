using System.Diagnostics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using System.Numerics;

namespace ForgeLine.Presentation;

public enum WorldAudioBus : byte { World, Alerts, Ambience }
public enum WorldAudioCue : byte
{
    None,
    CombatRifleFire01,
    CombatRifleFire02,
    CombatRifleFire03,
    CombatMachineGunFire01,
    CombatMachineGunFire02,
    CombatMachineGunFire03,
    CombatTankFire01,
    CombatTankFire02,
    CombatTankFire03,
    CombatArtilleryFire01,
    CombatArtilleryFire02,
    CombatArtilleryFire03,
    CombatExplosionSmall,
    CombatExplosionVehicle,
    CombatExplosionBuilding,
    CombatImpactDirt,
    CombatImpactMetal,
    CombatImpactConcrete,
    VehiclesScoutIdleLoop,
    VehiclesScoutMoveLoop,
    VehiclesTruckIdleLoop,
    VehiclesTruckMoveLoop,
    VehiclesTankIdleLoop,
    VehiclesTankMoveLoop,
    VehiclesEngineStart,
    VehiclesEngineStop,
    IndustryExtractorLoop,
    IndustrySmelterLoop,
    IndustryRefineryLoop,
    IndustryFactoryLoop,
    IndustryPowerPlantLoop,
    IndustryDepotLoop,
    LogisticsCargoLoad,
    LogisticsCargoUnload,
    LogisticsRefuel,
    LogisticsRearm,
    LogisticsRepair,
    LogisticsSupplyComplete,
    ConstructionPlace,
    ConstructionStart,
    ConstructionComplete,
    AlertsSupplyLow,
    AlertsSupplyCritical,
    AlertsPowerLow,
    AlertsPowerRestored,
    AlertsBaseUnderAttack,
    AlertsResearchComplete,
    AlertsUnitReady,
    AlertsInvalidOrder,
    AmbienceWindLoop,
    AmbienceIndustrialLoop,
    AmbienceBattlefieldLoop,
    CombatRocketLaunch,
    CombatIncomingArtillery,
    EventsVictory,
    EventsDefeat,
}

public readonly record struct WorldAudioDefinition(WorldAudioCue Cue, string AssetId,
    WorldAudioBus Bus, bool Loop, bool Bound, string UnavailableReason = "");

/// <summary>Production IDs only; this catalog neither loads files nor opens an audio device.</summary>
public static class WorldAudioCatalog
{
    private static readonly WorldAudioDefinition[] Definitions =
    [
        new(WorldAudioCue.CombatRifleFire01, "audio.combat.rifle_fire_01", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatRifleFire02, "audio.combat.rifle_fire_02", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatRifleFire03, "audio.combat.rifle_fire_03", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatMachineGunFire01, "audio.combat.machine_gun_fire_01", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatMachineGunFire02, "audio.combat.machine_gun_fire_02", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatMachineGunFire03, "audio.combat.machine_gun_fire_03", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatTankFire01, "audio.combat.tank_fire_01", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatTankFire02, "audio.combat.tank_fire_02", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatTankFire03, "audio.combat.tank_fire_03", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatArtilleryFire01, "audio.combat.artillery_fire_01", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatArtilleryFire02, "audio.combat.artillery_fire_02", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatArtilleryFire03, "audio.combat.artillery_fire_03", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatExplosionSmall, "audio.combat.explosion_small", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatExplosionVehicle, "audio.combat.explosion_vehicle", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatExplosionBuilding, "audio.combat.explosion_building", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatImpactDirt, "audio.combat.impact_dirt", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatImpactMetal, "audio.combat.impact_metal", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.CombatImpactConcrete, "audio.combat.impact_concrete", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.VehiclesScoutIdleLoop, "audio.vehicles.scout_idle_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.VehiclesScoutMoveLoop, "audio.vehicles.scout_move_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.VehiclesTruckIdleLoop, "audio.vehicles.truck_idle_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.VehiclesTruckMoveLoop, "audio.vehicles.truck_move_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.VehiclesTankIdleLoop, "audio.vehicles.tank_idle_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.VehiclesTankMoveLoop, "audio.vehicles.tank_move_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.VehiclesEngineStart, "audio.vehicles.engine_start", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.VehiclesEngineStop, "audio.vehicles.engine_stop", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.IndustryExtractorLoop, "audio.industry.extractor_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.IndustrySmelterLoop, "audio.industry.smelter_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.IndustryRefineryLoop, "audio.industry.refinery_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.IndustryFactoryLoop, "audio.industry.factory_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.IndustryPowerPlantLoop, "audio.industry.power_plant_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.IndustryDepotLoop, "audio.industry.depot_loop", WorldAudioBus.World, true, true, ""),
        new(WorldAudioCue.LogisticsCargoLoad, "audio.logistics.cargo_load", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.LogisticsCargoUnload, "audio.logistics.cargo_unload", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.LogisticsRefuel, "audio.logistics.refuel", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.LogisticsRearm, "audio.logistics.rearm", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.LogisticsRepair, "audio.logistics.repair", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.LogisticsSupplyComplete, "audio.logistics.supply_complete", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.ConstructionPlace, "audio.construction.place", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.ConstructionStart, "audio.construction.start", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.ConstructionComplete, "audio.construction.complete", WorldAudioBus.World, false, true, ""),
        new(WorldAudioCue.AlertsSupplyLow, "audio.alerts.supply_low", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.AlertsSupplyCritical, "audio.alerts.supply_critical", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.AlertsPowerLow, "audio.alerts.power_low", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.AlertsPowerRestored, "audio.alerts.power_restored", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.AlertsBaseUnderAttack, "audio.alerts.base_under_attack", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.AlertsResearchComplete, "audio.alerts.research_complete", WorldAudioBus.Alerts, false, false, "No completed-research counter or event is published."),
        new(WorldAudioCue.AlertsUnitReady, "audio.alerts.unit_ready", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.AlertsInvalidOrder, "audio.alerts.invalid_order", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.AmbienceWindLoop, "audio.ambience.wind_loop", WorldAudioBus.Ambience, true, true, ""),
        new(WorldAudioCue.AmbienceIndustrialLoop, "audio.ambience.industrial_loop", WorldAudioBus.Ambience, true, true, ""),
        new(WorldAudioCue.AmbienceBattlefieldLoop, "audio.ambience.battlefield_loop", WorldAudioBus.Ambience, true, true, ""),
        new(WorldAudioCue.CombatRocketLaunch, "audio.combat.rocket_launch", WorldAudioBus.World, false, false, "No rocket weapon event exists in the current content."),
        new(WorldAudioCue.CombatIncomingArtillery, "audio.combat.incoming_artillery", WorldAudioBus.World, false, false, "No authorized incoming-shell warning read model exists."),
        new(WorldAudioCue.EventsVictory, "audio.events.victory", WorldAudioBus.Alerts, false, true, ""),
        new(WorldAudioCue.EventsDefeat, "audio.events.defeat", WorldAudioBus.Alerts, false, true, ""),
    ];
    public static ReadOnlySpan<WorldAudioDefinition> All => Definitions;
    public static WorldAudioDefinition Get(WorldAudioCue cue) => cue > WorldAudioCue.None && (int)cue <= Definitions.Length
        ? Definitions[(int)cue - 1] : throw new ArgumentOutOfRangeException(nameof(cue));
}

public readonly record struct WorldAudioEvent(ulong Sequence, SimulationTick CreatedAt,
    SimulationTick ExpiresAt, WorldAudioCue Cue, EntityId Entity, Vector3 Position, bool Spatial, long CreatedAtTimestamp);
public readonly record struct WorldAudioLoop(WorldAudioCue Cue, EntityId Entity, Vector3 Position, bool Spatial = true);
public readonly record struct WorldAudioMetrics(long DroppedEvents, long DroppedLoops, long ReplacedHistory,
    int RetainedEvents, int LoopCount, long RetractedEvents = 0);

/// <summary>Immutable, authorized completed-tick audio facts. Loops are a complete replacement set.</summary>
public sealed class WorldAudioSnapshot
{
    private readonly WorldAudioEvent[] _events;
    private readonly WorldAudioLoop[] _loops;
    internal WorldAudioSnapshot(SimulationSessionId session, PlayerId player, SimulationTick tick,
        ReadOnlySpan<WorldAudioEvent> events, ReadOnlySpan<WorldAudioLoop> loops, WorldAudioMetrics metrics, long capturedAtTimestamp)
    { CapturedAtTimestamp = capturedAtTimestamp; Session = session; Player = player; Tick = tick; _events = events.ToArray(); _loops = loops.ToArray(); Metrics = metrics; }
    public long CapturedAtTimestamp { get; }
    public SimulationSessionId Session { get; }
    public PlayerId Player { get; }
    public SimulationTick Tick { get; }
    public ReadOnlySpan<WorldAudioEvent> Events => _events;
    public ReadOnlySpan<WorldAudioLoop> Loops => _loops;
    public WorldAudioMetrics Metrics { get; }
    public static WorldAudioSnapshot? Resolve(PresentationSnapshot? snapshot, SimulationSessionId session,
        PlayerId player, SimulationTick minimumTick) => snapshot is { WorldAudio: { } audio, PlayerExperience: { } experience } &&
        session.IsSpecified && player.IsSpecified && snapshot.SessionId == session && audio.Session == session &&
        audio.Player == player && experience.Player == player && audio.Tick == snapshot.Tick && experience.Tick == snapshot.Tick &&
        snapshot.Tick >= minimumTick ? audio : null;
}

/// <summary>Explicit session/player binding prevents old-session packets from reopening playback.</summary>
public sealed class WorldAudioEventCursor(SimulationSessionId session, PlayerId player)
{
    private SimulationTick _tick;
    private ulong _sequence;
    public int CopyFreshEvents(PresentationSnapshot snapshot, Span<WorldAudioEvent> destination, long? nowTimestamp = null)
    {
        var audio = WorldAudioSnapshot.Resolve(snapshot, session, player, _tick);
        if (audio is null) return 0;
        long now = nowTimestamp ?? Stopwatch.GetTimestamp();
        if (now < audio.CapturedAtTimestamp || Stopwatch.GetElapsedTime(audio.CapturedAtTimestamp, now) > TimeSpan.FromMilliseconds(250)) return 0;
        _tick = snapshot.Tick;
        int count = 0;
        foreach (var item in audio.Events)
        {
            if (count == destination.Length) break;
            if (item.Sequence <= _sequence || item.CreatedAt > audio.Tick || item.ExpiresAt <= audio.Tick || now < item.CreatedAtTimestamp ||
                Stopwatch.GetElapsedTime(item.CreatedAtTimestamp, now) > TimeSpan.FromMilliseconds(250)) continue;
            destination[count++] = item;
            _sequence = item.Sequence;
        }
        return count;
    }
    public void SuppressThrough(PresentationSnapshot snapshot)
    {
        var audio = WorldAudioSnapshot.Resolve(snapshot, session, player, _tick);
        if (audio is null) return;
        _tick = snapshot.Tick;
        foreach (var item in audio.Events)
            if (item.CreatedAt <= audio.Tick) _sequence = Math.Max(_sequence, item.Sequence);
    }

}
