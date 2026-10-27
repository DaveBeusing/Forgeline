using System.Numerics;
using ForgeLine.Combat;
using ForgeLine.Core;
using ForgeLine.Ecs;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class BattlefieldStatusOverlayTests
{
    [Fact]
    public void OwnedStatusCaptureLeavesCompletedAuthoritativeStateUnchanged()
    {
        using var runtime = MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation, 23), TestContext.Current.CancellationToken);
        runtime.Simulation.AdvanceOneTick();
        string before = MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        foreach (var entity in runtime.Simulation.Entities.Query<ControllableEntity>())
            _ = PlayerBattlefieldStatusCapture.Capture(runtime.Simulation.Entities, entity, new(1));
        Assert.Equal(before, MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256());
    }

    [Fact]
    public void ForeignDestroyedAndRecycledEntitiesPublishNoPrivateOperationalState()
    {
        var entities = new EntityRegistry();
        var entity = entities.CreateEntity();
        entities.AddComponent(entity, new ControllableEntity(new(2), ControllableEntityCategory.Unit));
        entities.AddComponent(entity, new UnitIdentity(UnitIds.MobileArtillery, new(1)));
        entities.AddComponent(entity, new SuppressionState(1, SuppressionLevel.Pinned, default, default));
        Assert.Equal(default, PlayerBattlefieldStatusCapture.Capture(entities, entity, new(1)));
        Assert.Equal(SuppressionLevel.Pinned, PlayerBattlefieldStatusCapture.Capture(entities, entity, new(2)).Suppression);
        entities.DestroyEntity(entity);
        var recycled = entities.CreateEntity();
        Assert.NotEqual(entity, recycled);
        Assert.Equal(default, PlayerBattlefieldStatusCapture.Capture(entities, entity, new(2)));
    }

    [Theory]
    [InlineData(FireMissionStatus.NoAmmo, "INSUFFICIENT AMMO")]
    [InlineData(FireMissionStatus.WaitingReload, "RELOADING")]
    [InlineData(FireMissionStatus.Acquiring, "ACQUIRING")]
    [InlineData(FireMissionStatus.Cancelled, "CANCELLED")]
    public void ArtilleryExplainsCopiedMissionStateWithoutInventingAHiddenTarget(FireMissionStatus mission, string reason)
    {
        Assert.Contains(reason, BattlefieldStatusText.Fire(new(default, true, mission, false), true, .5));
        Assert.Empty(BattlefieldStatusText.Fire(default, true, 0));
    }

    [Theory]
    [InlineData(120)]
    [InlineData(1000)]
    public void SelectedEmptyArtillerySurvivesDenseFormationAndToggleAndStaleDataClear(float distance)
    {
        var camera = new RtsCamera(new() { InitialTarget = Vector3.Zero, InitialDistance = distance });
        var snapshot = Snapshot(100, enemy: false);
        var view = RtsInformationLayerView.Empty with { SelectedEntities = [new(100, 1)], OverlayMode = StrategicOverlayMode.Supply };
        var overlay = new BattlefieldStatusOverlay();
        Update(snapshot, view);
        Assert.Equal(1, overlay.Count); // Identical projected positions collapse behind the selected priority.
        Assert.Contains("CANNOT FIRE", overlay.Markers[0].Label);
        Assert.True(overlay.Markers[0].Critical);
        Update(snapshot, view with { SelectedEntities = [], OverlayMode = StrategicOverlayMode.None });
        Assert.Equal(0, overlay.Count);
        Update(snapshot, view with { OverlayMode = StrategicOverlayMode.None });
        Assert.Equal(1, overlay.Count);
        Update(Snapshot(100, enemy: true), view);
        Assert.Equal(0, overlay.Count);
        Update(Snapshot(100, stale: true), view);
        Assert.Equal(0, overlay.Count);
        Update(Snapshot(100, wrongSession: true), view);
        Assert.Equal(0, overlay.Count);

        void Update(PresentationSnapshot source, RtsInformationLayerView layer) =>
            overlay.Update(source, camera, layer, new(0, 0, 1600, 900), 1, 1600, 900);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(4096)]
    public void WarmDenseOverlayReusesScratchWithoutAllocating(int count)
    {
        var source = Snapshot(count);
        var camera = new RtsCamera(new() { InitialTarget = Vector3.Zero });
        var view = RtsInformationLayerView.Empty with { OverlayMode = StrategicOverlayMode.Supply };
        var overlay = new BattlefieldStatusOverlay();
        for (int i = 0; i < 128; i++) Update();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 256; i++) Update();
        double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        if (Environment.GetEnvironmentVariable("FORGELINE_BATTLEFIELD_STATUS_CAPTURE") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"overlay-cpu-{count}.json"), System.Text.Json.JsonSerializer.Serialize(new
            { Instances = count, Warmup = 128, Samples = 256, TotalMilliseconds = elapsed, AllocatedBytes = allocated,
                MarkerCount = overlay.Count, Scope = "CPU projection only; observational timing on shared host" }));
        }
        Assert.InRange(overlay.Count, 1, BattlefieldStatusOverlay.MaximumMarkers);
        void Update() => overlay.Update(source, camera, view, new(0, 0, 1600, 900), 1, 1600, 900);
    }

    internal static PresentationSnapshot Snapshot(int count, bool enemy = false, bool stale = false, bool wrongSession = false)
    {
        var members = new CombatGroupMemberReadModel[count];
        var instances = new RenderInstance[count];
        for (int i = 0; i < count; i++)
        {
            var entity = new EntityId((uint)(i + 1), 1);
            members[i] = new(entity, ControllableEntityCategory.Unit, true, 1, true,
                BattlefieldSupplyStatus.Unsupplied, 0, 0, true, 1, 0, false, default, false, default, false, default)
                { BattlefieldStatus = new(default, true, FireMissionStatus.NoAmmo, false) };
            instances[i] = new(entity, RenderTransform.Identity, default, default, RenderVisibilityMask.World,
                Selectable: new(new(enemy ? 2UL : 1UL), ControllableEntityCategory.Unit));
        }
        var experience = default(PlayerExperienceSnapshot) with { Player = new(1), Tick = new(1), MatchStatus = PlayerMatchStatus.Active };
        return new(new SimulationTick(1), TimeSpan.FromMilliseconds(50), count, instances,
            sessionId: new(1), playerExperience: experience,
            combatGroups: new(new(stale ? 0UL : 1UL), members, new(wrongSession ? 2UL : 1UL)));
    }
}
