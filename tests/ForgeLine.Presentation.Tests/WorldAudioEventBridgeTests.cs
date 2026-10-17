using System.Numerics;
using System.Text.Json;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;
using Cue = ForgeLine.Presentation.WorldAudioCue;

namespace ForgeLine.Presentation.Tests;

public sealed class WorldAudioEventBridgeTests
{
    private static PlayerExperienceSnapshot Experience(ulong tick) => default(PlayerExperienceSnapshot) with
    { Player = new(1), Tick = new(tick), MatchStatus = PlayerMatchStatus.Active };
    private static void Begin(WorldAudioEventBridge b, ulong tick, ulong session = 1) =>
        Assert.True(b.Begin(new(session), new(1), new(tick), TimeSpan.FromMilliseconds(50)));
    private static WorldAudioSnapshot End(WorldAudioEventBridge b, ulong tick) => Assert.IsType<WorldAudioSnapshot>(b.Complete(Experience(tick)));
    private static WorldAudioEventBridge Warm()
    { var b = new WorldAudioEventBridge(() => 0); Begin(b, 1); End(b, 1); return b; }
    private static WorldAudioEntityFacts Truck(uint id = 1, uint generation = 1) =>
        new(new(id, generation), Vector3.Zero, new(UnitIds.CargoTruck, UnitPresentationDamageState.Intact), default, true);
    private static PresentationSnapshot Packet(WorldAudioSnapshot audio, PlayerExperienceSnapshot? experience = null,
        ulong? session = null, ulong? tick = null) =>
        new(new(tick ?? audio.Tick.Value), TimeSpan.FromMilliseconds(50), 0, [], sessionId: new(session ?? audio.Session.Value),
            playerExperience: experience ?? Experience(audio.Tick.Value)) { WorldAudio = audio };

    [Fact]
    public void SourceCatalogAccountsForEveryWorldClipWithoutInventingMissingEvents()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "assets/source/audio/library.json"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "assets/source/audio/library.json")));
        var expected = document.RootElement.GetProperty("assets").EnumerateArray()
            .Where(x => x.GetProperty("bus").GetString() != "ui").ToDictionary(x => x.GetProperty("id").GetString()!, StringComparer.Ordinal);
        Assert.Equal(56, WorldAudioCatalog.All.Length);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int missing = 0, loops = 0;
        foreach (var definition in WorldAudioCatalog.All)
        {
            Assert.True(ids.Add(definition.AssetId));
            var source = expected[definition.AssetId];
            Assert.Equal(source.GetProperty("loop").GetBoolean(), definition.Loop);
            Assert.Equal(definition, WorldAudioCatalog.Get(definition.Cue));
            if (definition.Loop) loops++;
            if (!definition.Bound) { Assert.NotEmpty(definition.UnavailableReason); missing++; }
        }
        using var ledger = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "docs/WorldAudioBindings.json")));
        var bindings = ledger.RootElement.GetProperty("bindings").EnumerateArray().ToDictionary(x => x.GetProperty("clipAssetId").GetString()!, StringComparer.Ordinal);
        Assert.Equal(56, bindings.Count);
        foreach (var definition in WorldAudioCatalog.All)
        {
            var row = bindings[definition.AssetId];
            Assert.Equal(definition.Bound, row.GetProperty("bound").GetBoolean());
            Assert.Equal(definition.Loop, row.GetProperty("loop").GetBoolean());
            Assert.Equal(definition.UnavailableReason, row.GetProperty("unavailableReason").GetString());
        }
        Assert.Equal(3, missing); Assert.Equal(15, loops);
        Assert.False(WorldAudioCatalog.Get(Cue.CombatRocketLaunch).Bound);
        Assert.False(WorldAudioCatalog.Get(Cue.CombatIncomingArtillery).Bound);
        Assert.False(WorldAudioCatalog.Get(Cue.AlertsResearchComplete).Bound);
    }

    [Theory]
    [InlineData(VfxEffectKind.MuzzleInfantry, Cue.CombatRifleFire01)]
    [InlineData(VfxEffectKind.MuzzleMachineGun, Cue.CombatMachineGunFire01)]
    [InlineData(VfxEffectKind.MuzzleTankCannon, Cue.CombatTankFire01)]
    [InlineData(VfxEffectKind.MuzzleArtillery, Cue.CombatArtilleryFire01)]
    public void VariantRotationHasNoImmediateRepeatAndUsesNoSimulationRandomness(VfxEffectKind kind, Cue first)
    {
        var b = Warm(); Begin(b, 2);
        for (int i = 0; i < 7; i++) b.ObserveCombat(kind, new(1, 1), Vector3.One, new(2));
        var events = End(b, 2).Events.ToArray();
        for (int i = 0; i < 7; i++) Assert.Equal((Cue)((int)first + i % 3), events[i].Cue);
        Assert.Equal(7, events.Select(x => x.Sequence).Distinct().Count());
    }

    [Theory]
    [InlineData(VfxEffectKind.ImpactDirt, Cue.CombatImpactDirt)]
    [InlineData(VfxEffectKind.ImpactArmor, Cue.CombatImpactMetal)]
    [InlineData(VfxEffectKind.ImpactConcrete, Cue.CombatImpactConcrete)]
    [InlineData(VfxEffectKind.ExplosionSmall, Cue.CombatExplosionSmall)]
    [InlineData(VfxEffectKind.ExplosionVehicle, Cue.CombatExplosionVehicle)]
    [InlineData(VfxEffectKind.ExplosionBuilding, Cue.CombatExplosionBuilding)]
    public void AuthorizedImpactAndDestructionKindsHaveExplicitBindings(VfxEffectKind kind, Cue cue)
    {
        var b = Warm(); Begin(b, 2); b.ObserveCombat(kind, default, Vector3.One, new(2));
        Assert.Equal(cue, Assert.Single(End(b, 2).Events.ToArray()).Cue);
    }

    [Fact]
    public void RetainedSnapshotsAndSkippedFramesStayImmutableAndDeduplicatedUntilExpiry()
    {
        var b = Warm(); Begin(b, 2); b.ObserveCombat(VfxEffectKind.MuzzleInfantry, new(1, 1), Vector3.One, new(2));
        var first = End(b, 2); var cursor = new WorldAudioEventCursor(new(1), new(1));
        var scratch = new WorldAudioEvent[256];
        Assert.Equal(1, cursor.CopyFreshEvents(Packet(first), scratch, 0));
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(first), scratch, 0));
        Begin(b, 3); var later = End(b, 3);
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(later), scratch, 0));
        Assert.Equal(1, new WorldAudioEventCursor(new(1), new(1)).CopyFreshEvents(Packet(later), scratch, 0));
        Assert.Single(first.Events.ToArray()); Assert.Equal(Vector3.One, first.Events[0].Position);
        Begin(b, 7); var expired = End(b, 7);
        Assert.Empty(expired.Events.ToArray()); Assert.Single(first.Events.ToArray());
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(first), scratch, 0));
    }

    [Fact]
    public void SessionPlayerTickAndCoherenceRejectOldOrForeignPackets()
    {
        var b = Warm(); Begin(b, 2); b.ObserveCombat(VfxEffectKind.ExplosionSmall, default, Vector3.One, new(2));
        var audio = End(b, 2); var scratch = new WorldAudioEvent[4];
        var cursor = new WorldAudioEventCursor(new(1), new(1));
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(audio, session: 2), scratch, 0));
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(audio, tick: 3), scratch, 0));
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(audio, Experience(2) with { Player = new(2) }), scratch, 0));
        Assert.False(b.Begin(new(1), new(2), new(3), TimeSpan.FromMilliseconds(50)));
        Assert.Null(b.Complete(Experience(3)));
        Assert.False(b.Begin(new(1), new(1), new(1), TimeSpan.FromMilliseconds(50)));
        Assert.Null(b.Complete(Experience(1)));
        Begin(b, 1, 2); Assert.Empty(End(b, 1).Events.ToArray());
        Assert.False(b.Begin(new(1), new(1), new(20), TimeSpan.FromMilliseconds(50)));
        Assert.Null(b.Complete(Experience(20)));
        Assert.Equal(1, cursor.CopyFreshEvents(Packet(audio), scratch, 0));
    }

    [Fact]
    public void RepeatedExtractionOfTheSameTickReturnsTheOriginalPayload()
    {
        var b = Warm(); Begin(b, 2); b.ObserveCombat(VfxEffectKind.ExplosionSmall, default, Vector3.One, new(2));
        var first = End(b, 2);
        Assert.False(b.Begin(new(1), new(1), new(2), TimeSpan.FromMilliseconds(50)));
        b.ObserveCombat(VfxEffectKind.ExplosionBuilding, default, Vector3.Zero, new(2));
        Assert.Same(first, b.Complete(Experience(2)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void VisibilityLossRetractsSpatialHistoryAndRemovesBattlefieldBed(bool validEntity)
    {
        var b = Warm(); Begin(b, 2);
        b.ObserveCombat(VfxEffectKind.ExplosionSmall, validEntity ? new(1, 1) : default, Vector3.One, new(2));
        var retained = b.Complete(Experience(2), new Visibility(true)); Assert.NotNull(retained);
        Assert.Single(retained.Events.ToArray());
        Begin(b, 3); var hidden = b.Complete(Experience(3), new Visibility(false)); Assert.NotNull(hidden);
        Assert.Empty(hidden.Events.ToArray());
        Assert.DoesNotContain(hidden.Loops.ToArray(), x => x.Cue == Cue.AmbienceBattlefieldLoop);
        Assert.Equal(1, hidden.Metrics.RetractedEvents);
        Assert.Single(retained.Events.ToArray());
    }

    [Fact]
    public void ForeignMutableFactsNeverBecomeLoopsOrLogisticsCues()
    {
        var b = Warm();
        for (ulong tick = 2; tick <= 4; tick++)
        {
            Begin(b, tick); b.ObserveEntity(Truck() with { Owned = false, Position = new(tick, 0, 0),
                Cargo = CargoTransportLifecycleState.Loading, Supply = new(.5, .5, BattlefieldSupplyStatus.LowSupply, new(tick)) });
            var audio = End(b, tick);
            Assert.Empty(audio.Events.ToArray());
            Assert.Equal(Cue.AmbienceWindLoop, Assert.Single(audio.Loops.ToArray()).Cue);
        }
    }

    [Fact]
    public void MovementTransitionsUseFullEntityIdentityAndRequireConsecutiveVisibility()
    {
        var b = new WorldAudioEventBridge(() => 0); Begin(b, 1); b.ObserveEntity(Truck()); End(b, 1);
        Begin(b, 2); b.ObserveEntity(Truck() with { Position = Vector3.One });
        var moving = End(b, 2); Assert.Contains(moving.Events.ToArray(), x => x.Cue == Cue.VehiclesEngineStart);
        Assert.Contains(moving.Loops.ToArray(), x => x.Cue == Cue.VehiclesTruckMoveLoop);
        Begin(b, 3); End(b, 3); // The source disappears: complete loop set removes it.
        Begin(b, 4); b.ObserveEntity(Truck() with { Position = new(10, 0, 0) });
        var reacquired = End(b, 4);
        Assert.DoesNotContain(reacquired.Events.ToArray(), x => x.CreatedAt.Value == 4);
        Begin(b, 5); b.ObserveEntity(Truck(generation: 2) with { Position = new(20, 0, 0) });
        Assert.DoesNotContain(End(b, 5).Events.ToArray(), x => x.CreatedAt.Value == 5);
    }

    [Theory]
    [InlineData(3, Cue.VehiclesScoutIdleLoop)] [InlineData(4, Cue.VehiclesTankIdleLoop)]
    [InlineData(5, Cue.VehiclesTankIdleLoop)] [InlineData(6, Cue.VehiclesTruckIdleLoop)] [InlineData(7, Cue.VehiclesTruckIdleLoop)]
    public void VehicleFamiliesUseTheExpectedLoop(uint unit, Cue cue)
    {
        var b = Warm(); Begin(b, 2); b.ObserveEntity(Truck() with { Unit = new(new(unit), UnitPresentationDamageState.Intact) });
        Assert.Contains(End(b, 2).Loops.ToArray(), x => x.Cue == cue);
    }

    [Theory]
    [InlineData(2, Cue.IndustryPowerPlantLoop)] [InlineData(3, Cue.IndustryExtractorLoop)]
    [InlineData(4, Cue.IndustryDepotLoop)] [InlineData(5, Cue.IndustrySmelterLoop)]
    [InlineData(6, Cue.IndustryRefineryLoop)] [InlineData(12, Cue.IndustryFactoryLoop)]
    public void OperationalOwnedIndustryUsesExplicitFamiliesAndUnpoweredStopsIt(uint id, Cue cue)
    {
        var b = Warm(); var facts = new WorldAudioEntityFacts(new(1, 1), Vector3.One, default,
            new(new(id), BuildingPresentationState.Operational), true, true);
        Begin(b, 2); b.ObserveEntity(facts); Assert.Contains(End(b, 2).Loops.ToArray(), x => x.Cue == cue);
        Begin(b, 3); b.ObserveEntity(facts with { Building = facts.Building with { State = BuildingPresentationState.Unpowered } });
        Assert.DoesNotContain(End(b, 3).Loops.ToArray(), x => x.Cue == cue || x.Cue == Cue.AmbienceIndustrialLoop);
    }

    [Fact]
    public void ConstructionLoadingRepairAndSupplyChangesAreTransitionsNotEveryTickSounds()
    {
        var b = Warm();
        Begin(b, 2); b.ObserveEntity(new(new(4, 1), Vector3.One, default, new(BuildingIds.PowerPlant, BuildingPresentationState.ConstructionFrame), true));
        Assert.Contains(End(b, 2).Events.ToArray(), x => x.Cue == Cue.ConstructionStart);
        Begin(b, 3); b.ObserveEntity(new(new(4, 1), Vector3.One, default, new(BuildingIds.PowerPlant, BuildingPresentationState.Operational), true));
        Assert.Contains(End(b, 3).Events.ToArray(), x => x.Cue == Cue.ConstructionComplete);
        var facts = Truck() with { Supply = new(.1, .1, BattlefieldSupplyStatus.Critical, new(4)), HealthFraction = .3 };
        Begin(b, 4); b.ObserveEntity(facts); End(b, 4);
        Begin(b, 5); facts = facts with { Supply = new(.5, .6, BattlefieldSupplyStatus.Supplied, new(5)), HealthFraction = .4, Cargo = CargoTransportLifecycleState.Loading };
        b.ObserveEntity(facts); var changed = End(b, 5);
        foreach (var cue in new[] { Cue.LogisticsRefuel, Cue.LogisticsRearm, Cue.LogisticsRepair, Cue.LogisticsSupplyComplete, Cue.LogisticsCargoLoad })
            Assert.Contains(changed.Events.ToArray(), x => x.Cue == cue && x.CreatedAt.Value == 5);
        Begin(b, 6); b.ObserveEntity(facts with { Supply = new(.6, .7, BattlefieldSupplyStatus.Supplied, new(6)), HealthFraction = .5 });
        Assert.DoesNotContain(End(b, 6).Events.ToArray(), x => x.CreatedAt.Value == 6);
    }

    [Fact]
    public void WarningEdgesAndFinalOutcomeHaveOneIdentityAndTerminalRemovesWorldHistory()
    {
        var b = Warm(); Begin(b, 2);
        var xp = Experience(2) with { Alerts = PlayerAlertState.LowPower | PlayerAlertState.SupplyCritical | PlayerAlertState.CommandCoreDamaged,
            Statistics = new(2, .1, 1, 0, 0), Feedback = PlayerCommandFeedback.None with { Kind = PlayerCommandFeedbackKind.Movement, State = PlayerCommandFeedbackState.Rejected, ResolvedAtTick = new(2) } };
        var first = b.Complete(xp); Assert.NotNull(first);
        foreach (var cue in new[] { Cue.AlertsPowerLow, Cue.AlertsSupplyCritical, Cue.AlertsBaseUnderAttack, Cue.AlertsUnitReady, Cue.AlertsInvalidOrder })
            Assert.Contains(first.Events.ToArray(), x => x.Cue == cue);
        Begin(b, 3); var still = b.Complete(xp with { Tick = new(3) }); Assert.NotNull(still);
        Assert.DoesNotContain(still.Events.ToArray(), x => x.CreatedAt.Value == 3);
        Begin(b, 4); var restored = b.Complete(xp with { Tick = new(4), Alerts = PlayerAlertState.None }); Assert.NotNull(restored);
        Assert.Contains(restored.Events.ToArray(), x => x.Cue == Cue.AlertsPowerRestored);
        Begin(b, 5); var terminal = b.Complete(xp with { Tick = new(5), MatchStatus = PlayerMatchStatus.Victory }); Assert.NotNull(terminal);
        Assert.Empty(terminal.Loops.ToArray()); Assert.Equal(Cue.EventsVictory, Assert.Single(terminal.Events.ToArray()).Cue);
        Begin(b, 6); Assert.Null(b.Complete(Experience(6))); // A completed session cannot resume from a stray active packet.
    }

    [Fact]
    public void OverflowReservesWarningSpaceAndBoundsLoopsAndHistory()
    {
        var b = Warm(); Begin(b, 2);
        for (int i = 0; i < 4000; i++)
        { b.ObserveEntity(Truck((uint)i + 1)); b.ObserveCombat(VfxEffectKind.MuzzleInfantry, default, Vector3.One, new(2)); }
        var audio = b.Complete(Experience(2) with { Alerts = PlayerAlertState.LowPower }); Assert.NotNull(audio);
        Assert.InRange(audio.Events.Length, 1, WorldAudioEventBridge.MaximumEvents);
        Assert.InRange(audio.Loops.Length, 1, WorldAudioEventBridge.MaximumLoops);
        Assert.Contains(audio.Events.ToArray(), x => x.Cue == Cue.AlertsPowerLow);
        Assert.True(audio.Metrics.DroppedEvents > 0); Assert.True(audio.Metrics.DroppedLoops > 0); Assert.True(audio.Metrics.ReplacedHistory > 0);
        Assert.Equal(audio.Events.Length, audio.Events.ToArray().Select(x => x.Sequence).Distinct().Count());
    }

    [Fact]
    public void InvalidCoordinatesFutureCombatAndIncoherentExperienceFailClosed()
    {
        var b = Warm(); Begin(b, 2);
        b.ObserveCombat(VfxEffectKind.MuzzleInfantry, default, new(float.NaN), new(2));
        b.ObserveCombat(VfxEffectKind.ExplosionSmall, default, Vector3.One, new(3));
        b.ObserveEntity(Truck() with { Position = new(float.PositiveInfinity) });
        Assert.Empty(End(b, 2).Events.ToArray());
        Begin(b, 3); b.ObserveCombat(VfxEffectKind.ExplosionSmall, default, Vector3.One, new(3));
        Assert.Null(b.Complete(Experience(4)));
        Begin(b, 4); Assert.Empty(End(b, 4).Events.ToArray());
    }

    [Fact]
    public void FrozenAndResumedSimulationCannotReplayEventsOlderThanTheWallClockLimit()
    {
        long timestamp = 10;
        var b = new WorldAudioEventBridge(() => timestamp); Begin(b, 1); End(b, 1);
        Begin(b, 2); b.ObserveCombat(VfxEffectKind.ExplosionSmall, default, Vector3.One, new(2));
        var audio = End(b, 2); var scratch = new WorldAudioEvent[4];
        var cursor = new WorldAudioEventCursor(new(1), new(1));
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(audio), scratch, timestamp - 1));
        timestamp += System.Diagnostics.Stopwatch.Frequency;
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(audio), scratch, timestamp));
        Begin(b, 3); Assert.Empty(End(b, 3).Events.ToArray());
    }

    [Fact]
    public void MutedOrRecreatedOutputCanSuppressBacklogWithoutConsumingTheNextFreshEvent()
    {
        var b = Warm(); Begin(b, 2); b.ObserveCombat(VfxEffectKind.ExplosionSmall, default, Vector3.One, new(2));
        var old = End(b, 2); var cursor = new WorldAudioEventCursor(new(1), new(1)); var scratch = new WorldAudioEvent[4];
        cursor.SuppressThrough(Packet(old));
        Assert.Equal(0, cursor.CopyFreshEvents(Packet(old), scratch, 0));
        Begin(b, 3); b.ObserveCombat(VfxEffectKind.ImpactDirt, default, Vector3.One, new(3)); var fresh = End(b, 3);
        Assert.Equal(1, cursor.CopyFreshEvents(Packet(fresh), scratch, 0));
        Assert.Equal(Cue.CombatImpactDirt, scratch[0].Cue);
    }

    [Theory]
    [InlineData(249, 1)] [InlineData(250, 1)] [InlineData(251, 0)]
    public void WallClockExpiryHasAnExplicitBoundary(int ageMilliseconds, int expected)
    {
        var b = Warm(); Begin(b, 2); b.ObserveCombat(VfxEffectKind.ExplosionSmall, default, Vector3.One, new(2));
        var audio = End(b, 2); var cursor = new WorldAudioEventCursor(new(1), new(1));
        Assert.Equal(expected, cursor.CopyFreshEvents(Packet(audio), new WorldAudioEvent[4],
            (long)ageMilliseconds * System.Diagnostics.Stopwatch.Frequency / 1000));
    }

    [Fact]
    public void SupplyWarningAndAcceptedPlacementUseOwnedCoherentFactsAndDefeatIsNotVictory()
    {
        var b = Warm(); Begin(b, 2);
        b.ObserveEntity(Truck() with { Supply = new(.2, .2, BattlefieldSupplyStatus.LowSupply, new(3)) });
        var placed = b.Complete(Experience(2) with { Feedback = PlayerCommandFeedback.None with
        { Kind = PlayerCommandFeedbackKind.Construction, State = PlayerCommandFeedbackState.Accepted, ResolvedAtTick = new(2) } });
        Assert.NotNull(placed); var placement = Assert.Single(placed.Events.ToArray());
        Assert.Equal(Cue.ConstructionPlace, placement.Cue); Assert.False(placement.Spatial);
        Begin(b, 3); b.ObserveEntity(Truck() with { Supply = new(.2, .2, BattlefieldSupplyStatus.LowSupply, new(3)) });
        Assert.Contains(End(b, 3).Events.ToArray(), x => x.Cue == Cue.AlertsSupplyLow);
        Begin(b, 4); var defeat = b.Complete(Experience(4) with { MatchStatus = PlayerMatchStatus.Defeat });
        Assert.NotNull(defeat); Assert.Equal(Cue.EventsDefeat, Assert.Single(defeat.Events.ToArray()).Cue);
        Assert.Empty(defeat.Loops.ToArray());
    }

    [Theory]
    [InlineData(3, Cue.VehiclesScoutMoveLoop)] [InlineData(4, Cue.VehiclesTankMoveLoop)]
    [InlineData(6, Cue.VehiclesTruckMoveLoop)]
    public void CurrentMovementFactsSurviveHistoryPressure(uint unit, Cue expected)
    {
        var b = Warm(); Begin(b, 2);
        b.ObserveEntity(Truck() with { Unit = new(new(unit), UnitPresentationDamageState.Intact), Moving = true });
        Assert.Contains(End(b, 2).Loops.ToArray(), x => x.Cue == expected);
    }

    [Fact]
    public void LoadingAndUnloadingAreDistinctEntryCues()
    {
        var b = new WorldAudioEventBridge(() => 0); Begin(b, 1); b.ObserveEntity(Truck()); End(b, 1);
        Begin(b, 2); b.ObserveEntity(Truck() with { Cargo = CargoTransportLifecycleState.Loading });
        Assert.Contains(End(b, 2).Events.ToArray(), x => x.Cue == Cue.LogisticsCargoLoad);
        Begin(b, 3); b.ObserveEntity(Truck() with { Cargo = CargoTransportLifecycleState.Unloading });
        Assert.Contains(End(b, 3).Events.ToArray(), x => x.Cue == Cue.LogisticsCargoUnload);
    }

    [Fact]
    public void CatchingUpAfterRejectedRollbackCannotReviveTheAbandonedTimeline()
    {
        var b = Warm(); Begin(b, 2); b.ObserveEntity(Truck()); End(b, 2);
        Begin(b, 3); b.ObserveEntity(Truck() with { Position = Vector3.One });
        Assert.Contains(End(b, 3).Events.ToArray(), x => x.Cue == Cue.VehiclesEngineStart);
        Assert.False(b.Begin(new(1), new(1), new(2), TimeSpan.FromMilliseconds(50))); Assert.Null(b.Complete(Experience(2)));
        Assert.False(b.Begin(new(1), new(1), new(3), TimeSpan.FromMilliseconds(50))); Assert.Null(b.Complete(Experience(3)));
        Begin(b, 4); b.ObserveEntity(Truck() with { Position = new(100, 0, 0), Moving = true });
        var recovered = End(b, 4);
        Assert.Empty(recovered.Events.ToArray());
        Assert.Contains(recovered.Loops.ToArray(), x => x.Cue == Cue.VehiclesTruckMoveLoop);
    }

    [Fact]
    public void RepeatedTickWithConflictingOutcomeCannotBorrowAnActiveOrWinningPayload()
    {
        var b = Warm(); Begin(b, 2); var active = End(b, 2);
        Assert.False(b.Begin(new(1), new(1), new(2), TimeSpan.FromMilliseconds(50)));
        Assert.Null(b.Complete(Experience(2) with { MatchStatus = PlayerMatchStatus.Victory }));
        Assert.Same(active, b.Complete(Experience(2)));
    }

    private sealed class Visibility(bool visible) : IWorldAudioVisibility
    { public bool Allows(EntityId entity, Vector3 position) => visible; }
}
