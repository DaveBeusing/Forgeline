using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Combat;
using ForgeLine.Economy;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class PracticeScenarioTests
{
    private static readonly PlayerId Player=new(1);
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions=new(){IncludeFields=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
    internal static MatchRuntime Create(string key)=>MatchRuntime.Create(CentralDivideScenario.CreateHeadless(
        MatchScenarioProfile.Gameplay,17,composition:ScenarioRegistry.Default.Resolve(key)),TestContext.Current.CancellationToken);
    internal static PracticeSnapshot Snapshot(MatchRuntime r)=>r.Practice!.Capture(Player,r.Simulation.CurrentTick)!;
    private static void Step(MatchRuntime r,int n=1)=>r.Simulation.RunTicks((ulong)n,TestContext.Current.CancellationToken);
    private static void Until(MatchRuntime r,Func<bool> condition,int ticks=6000)
    {
        for(int i=0;i<ticks&&!condition();i++)Step(r);
        if(!condition())Assert.Fail(System.Text.Json.JsonSerializer.Serialize(Snapshot(r))+Diagnostics(r));
    }
    private static void CheckCompletedRecovery(MatchRuntime r)
    {
        var before=Snapshot(r);
        Assert.Equal(PracticeStatus.Complete,before.Status);
        using var restored=MatchPersistenceService.Restore(MatchPersistenceService.CaptureSave(r));
        using var replay=MatchPersistenceService.PlayReplay(MatchPersistenceService.CaptureReplay(r));
        Assert.Equal(before.CompletedAtTicks,Snapshot(restored).CompletedAtTicks);
        Assert.Equal(before.CompletedAtTicks,Snapshot(replay).CompletedAtTicks);
        Assert.Equal(MatchAuthoritativeSnapshot.Capture(r).ComputeSha256(),MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256());
        TestContext.Current.TestOutputHelper?.WriteLine(System.Text.Json.JsonSerializer.Serialize(before));
    }
    private static string Diagnostics(MatchRuntime r)
    {
        var rows=new List<object>();
        foreach(var e in r.Simulation.Entities.Query<CompletedBuilding>())
            if(r.Simulation.Entities.GetComponent<CompletedBuilding>(e).Owner==Player)
            {
                var has=r.Simulation.Entities.TryGetComponent(e,out ProductionFacility facility);
                rows.Add(new {Entity=e,Building=r.Simulation.Entities.GetComponent<CompletedBuilding>(e),
                    Position=r.Simulation.Entities.GetComponent<WorldTransform>(e).Position,
                    Power=r.Simulation.Entities.TryGetComponent(e,out PowerConsumer power)?power:default,
                    Production=facility,Input=has?new{Ore=r.Inventories.GetQuantity(facility.InputInventory,ResourceIds.FerrousOre),Fuel=r.Inventories.GetQuantity(facility.InputInventory,ResourceIds.Fuel)}:null});
            }
        foreach(var e in r.GetBase(Player).StartingUnits)
            rows.Add(new{Unit=e,Identity=r.Simulation.Entities.GetComponent<UnitIdentity>(e),
                Position=r.Simulation.Entities.GetComponent<WorldTransform>(e).Position,
                Ground=r.Simulation.Entities.TryGetComponent(e,out GroundMovementState ground)?ground:default,
                Movement=r.Simulation.Entities.TryGetComponent(e,out MovementOrder movement)?movement:default,
                Failure=r.Simulation.Entities.TryGetComponent(e,out NavigationFailureState failure)?failure:default,
                Loading=r.Simulation.Entities.TryGetComponent(e,out SupplyLoadingOrder loading)?loading:default});
        return System.Text.Json.JsonSerializer.Serialize(rows,JsonOptions);
    }
    internal static EntityId Building(MatchRuntime r,BuildingId id)
    {
        foreach(var e in r.Simulation.Entities.Query<CompletedBuilding>())
        {var b=r.Simulation.Entities.GetComponent<CompletedBuilding>(e);if(b.Owner==Player&&b.BuildingId==id)return e;}
        return default;
    }
    private static EntityId Build(MatchRuntime r,BuildingId id,Vector3 origin)
    {
        Vector3? point=r.Services.BuildingPlacement.Evaluate(r.Simulation.Entities,Player,id,origin,BuildingOrientation.North).IsValid?origin:null;
        for(int z=-5;z<=5&&point is null;z++)for(int x=-5;x<=5&&point is null;x++)
        {
            var candidate=origin+new Vector3(x*16,0,z*16);
            if(r.Services.BuildingPlacement.Evaluate(r.Simulation.Entities,Player,id,candidate,BuildingOrientation.North).IsValid)point=candidate;
        }
        Assert.NotNull(point);
        var command=new BuildCommand(Player,id,point.Value,BuildingOrientation.North,r.GetBase(Player).CommandCore,r.Simulation.CurrentTick);
        r.Simulation.SubmitCommand(command,r.Simulation.CurrentTick.Next());Step(r,2);
        Assert.True(r.Services.BuildingCommands.TryGetLastResult(Player,out var result));
        Assert.True(result.Accepted,result.RejectionReason+" / "+result.PlacementFailure);
        Until(r,()=>r.Simulation.Entities.HasComponent<CompletedBuilding>(result.CreatedSite));
        return result.CreatedSite;
    }
    private static void Connect(MatchRuntime r,EntityId source,EntityId target)
    {
        var ports=ConveyorConstructionService.CapturePorts(r.Simulation.Entities,Player);
        var output=Assert.Single(ports,p=>p.Building==source&&!p.Input&&
            (!p.Adaptable||p.Key=="east.primary"));
        var input=Assert.Single(ports,p=>p.Building==target&&p.Input&&
            (!p.Adaptable||p.Key=="west.primary"));
        var request=new ConveyorBuildRequest(ConveyorEdit.Build,ConveyorModule.Route,
            output.Position-Vector3.UnitY*5.8f,input.Position-Vector3.UnitY*5.8f,BuildingOrientation.North,0,true,
            SourcePortBuilding:source,DestinationPortBuilding:target,AutomaticRouting:true,SnapToSocket:true,AutomaticHeight:true);
        var command=new ConveyorConstructionCommand(r.Services.Conveyors!,Player,request,r.GetBase(Player).CommandCore,r.Simulation.CurrentTick);
        r.Simulation.SubmitCommand(command,r.Simulation.CurrentTick.Next());Step(r);
        Assert.True(command.Accepted,command.Result.ToString());
    }

    [Theory]
    [InlineData(PracticeLessons.ReconKey)]
    [InlineData(PracticeLessons.IndustryKey)]
    [InlineData(PracticeLessons.SupplyKey)]
    public void RegisteredLessonsStartWithoutFakeSuccessAndRoundTrip(string key)
    {
        using var r=Create(key);Step(r,8);
        Assert.All(r.MatchConfiguration.Participants,p=>Assert.False(p.IsComputerControlled));
        Assert.Equal(PracticeStatus.Active,Snapshot(r).Status);
        Assert.All(Snapshot(r).CompletedAtTicks,t=>Assert.Null(t));
        var save=MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(r)));
        using var copy=MatchPersistenceService.Restore(save);
        using var replay=MatchPersistenceService.PlayReplay(MatchPersistenceService.CaptureReplay(r));
        Assert.Equal(save.StateSha256,MatchAuthoritativeSnapshot.Capture(copy).ComputeSha256());
        Assert.Equal(save.StateSha256,MatchAuthoritativeSnapshot.Capture(replay).ComputeSha256());
        Assert.Equal(Snapshot(r).CompletedAtTicks,Snapshot(copy).CompletedAtTicks);
        Assert.Equal(20,MatchAuthoritativeSnapshot.CurrentSchemaVersion);
    }

    [Fact]
    public void CommandAndReconCompletesThroughNormalAttackMove()
    {
        using var r=Create(PracticeLessons.ReconKey);
        var scout=r.GetBase(Player).StartingUnits.First(e=>r.Simulation.Entities.GetComponent<UnitIdentity>(e).UnitId==UnitIds.ScoutVehicle);
        var command=PlayerTacticalActionCommand.AttackMove(Player,[scout],new(990,0,1536),r.Simulation.CurrentTick,
            FormationTemplate.Compact,r.Intelligence,r.Services.Weapons,r.Services.ArtilleryWeapons);
        r.Simulation.SubmitCommand(command,r.Simulation.CurrentTick.Next());
        Until(r,()=>Snapshot(r).Status==PracticeStatus.Complete);
        Assert.All(Snapshot(r).CompletedAtTicks,t=>Assert.NotNull(t));
        CheckCompletedRecovery(r);
    }

    [Fact]
    public void BuildPowerAndProductionCompletesWithPaidConstructionAndRealCargoInput()
    {
        using var r=Create(PracticeLessons.IndustryKey);Step(r,800);
        var extractor=Building(r,BuildingIds.Extractor);Assert.True(extractor.IsValid,System.Text.Json.JsonSerializer.Serialize(r.Services.BuildingCommands.Metrics));
        Build(r,BuildingIds.PowerPlant,new(470,0,1450));
        var origin=r.Simulation.Entities.GetComponent<WorldTransform>(extractor).Position;
        var smelter=Build(r,BuildingIds.Smelter,origin+new Vector3(64,0,64));
        r.Simulation.SubmitCommand(PlayerLogisticsActionCommand.SetStockPolicy(Player,smelter,ResourceIds.FerrousOre,50,100,150,LogisticsStockPriority.High,true,r.Simulation.CurrentTick),r.Simulation.CurrentTick.Next());
        Until(r,()=>Snapshot(r).Status==PracticeStatus.Complete,12000);
        Assert.True(r.Simulation.Entities.GetComponent<ProductionFacility>(smelter).CompletedCycles>0);
        CheckCompletedRecovery(r);
    }

    [Fact]
    public void SupplyRecoveryRequiresConveyorProcessingAndPhysicalMobileAmmunition()
    {
        using var r=Create(PracticeLessons.SupplyKey);Step(r,800);
        var extractor=Building(r,BuildingIds.Extractor);var smelter=Building(r,BuildingIds.Smelter);
        Assert.True(extractor.IsValid&&smelter.IsValid,System.Text.Json.JsonSerializer.Serialize(r.Services.BuildingCommands.Metrics));
        Connect(r,extractor,smelter);
        Until(r,()=>Snapshot(r).CompletedAtTicks[1].HasValue);
        var truck=r.GetBase(Player).StartingUnits.First(e=>r.Simulation.Entities.HasComponent<SupplyTruck>(e));
        var squad=r.GetBase(Player).StartingUnits.First(e=>r.Simulation.Entities.GetComponent<UnitIdentity>(e).UnitId==UnitIds.RifleSquad);
        var load=PlayerLogisticsActionCommand.RequestResupply(Player,truck,r.Simulation.CurrentTick);
        r.Simulation.SubmitCommand(load,r.Simulation.CurrentTick.Next());
        Until(r,()=>Snapshot(r).CompletedAtTicks[2].HasValue);
        Until(r,()=>!r.Simulation.Entities.HasComponent<SupplyLoadingOrder>(truck)&&!r.Simulation.Entities.HasComponent<VehicleDockVisit>(truck));
        var target=r.Simulation.Entities.GetComponent<WorldTransform>(squad).Position+new Vector3(15,0,0);
        r.Simulation.SubmitCommand(new MoveEntitiesCommand(Player,[truck],target,r.Simulation.CurrentTick),r.Simulation.CurrentTick.Next());
        Until(r,()=>Vector3.DistanceSquared(r.Simulation.Entities.GetComponent<WorldTransform>(truck).Position,target)<100);
        r.Simulation.SubmitCommand(PlayerLogisticsActionCommand.RequestResupply(Player,squad,r.Simulation.CurrentTick),r.Simulation.CurrentTick.Next());
        Until(r,()=>Snapshot(r).Status==PracticeStatus.Complete);
        CheckCompletedRecovery(r);
    }

    [Fact]
    public void PracticeRegistrationCannotBypassPinnedRules()
    {
        var registrations=ScenarioRegistry.Default.Descriptors.Select(d=>d.Key==PracticeLessons.ReconKey
            ? d with {Identity=d.Identity with {StartRule="incompatible"}}:d).ToArray();
        var registry=new ScenarioRegistry(Path.Combine(AppContext.BaseDirectory,"maps"),registrations);
        Assert.Throws<InvalidOperationException>(()=>registry.Resolve(PracticeLessons.ReconKey));
    }

    [Fact]
    public void SnapshotsAreDetachedAndOwnedAndCriticalLossFailsExplicitly()
    {
        using var r=Create(PracticeLessons.ReconKey);Step(r,2);
        var before=Snapshot(r);
        Assert.Null(r.Practice!.Capture(new(2),r.Simulation.CurrentTick));
        Assert.Null(r.Practice.Capture(Player,new(1)));
        var scout=r.GetBase(Player).StartingUnits.First(e=>r.Simulation.Entities.GetComponent<UnitIdentity>(e).UnitId==UnitIds.ScoutVehicle);
        r.Simulation.Entities.DestroyEntity(scout);Step(r);
        Assert.Equal(PracticeStatus.Failed,Snapshot(r).Status);
        Assert.Contains("destroyed",Snapshot(r).Failure);
        Assert.Equal(PracticeStatus.Active,before.Status);
        Assert.All(before.CompletedAtTicks,t=>Assert.Null(t));
    }

    [Fact]
    public void InvalidProgressAndCancelledRestoreFailWithoutChangingSkirmish()
    {
        using var r=Create(PracticeLessons.ReconKey);Step(r,2);
        var save=MatchPersistenceService.CaptureSave(r);
        Assert.Throws<MatchPersistenceException>(()=>MatchPersistenceService.Restore(save with {PracticeProgress=null}));
        Assert.Throws<MatchPersistenceException>(()=>MatchPersistenceService.Restore(save with {PracticeProgress=save.PracticeProgress! with {CompletedAtTicks=[1,1,1],Status=PracticeStatus.Complete}}));
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>MatchPersistenceService.RestoreCancellable(save,cancelled.Token));
        using var normal=MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay,17),TestContext.Current.CancellationToken);
        Assert.Null(normal.Practice);
        Assert.Equal(CentralDivideScenario.CompositionKey,ScenarioRegistry.Default.Descriptors[0].Key);
    }
}
