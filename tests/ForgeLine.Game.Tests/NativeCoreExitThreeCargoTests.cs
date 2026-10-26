using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class NativeCoreExitThreeCargoTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CapturedThreeCargoCoreExitClearsThroughOrdinaryMovement(bool currentGeneration, bool hasFuel)
    {
        using var scene = CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay, 2026) with
        { Participants = CentralDivideScenario.CreateDefaultParticipants(false, false) }, TestContext.Current.CancellationToken);
        scene.Simulation.RunTicks(40, TestContext.Current.CancellationToken);
        var entities = scene.Simulation.Entities; var owner = new PlayerId(2);
        var catalog = DirectorateContent.CreateUnitCatalog();
        var positions = new[] { new Vector3(2622.5999f,9.324579f,1553.7333f),new Vector3(2617.5093f,9.397788f,1558.7842f),new Vector3(2624.428f,9.33169f,1558.8304f) };
        var headings = new[] { -.020107135f,-1.5468751f,-1.5500659f };
        var goals = new[] { new Vector3(2622.4f,9.374009f,1563.6777f),new Vector3(2653.5002f,8.56628f,1484.3f),new Vector3(2552,8.648485f,1536) };
        var trucks = new EntityId[3];
        for (int i=0;i<3;i++)
        {
            trucks[i] = scene.UnitFactory.Create(catalog[UnitIds.CargoTruck],positions[i],owner);
            var p=entities.GetComponent<WorldTransform>(trucks[i]);
            entities.SetComponent(trucks[i],p with { Rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,headings[i]) });
            entities.SetComponent(trucks[i],GroundMovementState.Stationary(headings[i]));
            if (!hasFuel && i > 0)
            { var fuel=entities.GetComponent<UnitFuelState>(trucks[i]); Assert.True(scene.Inventories.Remove(fuel.InventoryId,ResourceIds.Fuel,scene.Inventories.GetQuantity(fuel.InventoryId,ResourceIds.Fuel)).Succeeded); }
            entities.AddComponent(trucks[i], new MovementOrder(owner,goals[i],SimulationTick.Zero,new(1),i==0 ? MovementOrderKind.LocalApproach : MovementOrderKind.Strategic,i==0 ? 4.371139e-8f : null));
        }
        var bay=scene.GetBase(owner).CommandCore;
        var pose=new VehicleDockPose(new(2586.8f,9.6144f,1550.5f),new(2602,9.493963f,1550.5f),new(2617.2f,9.359385f,1550.5f),MathF.PI/2,3.2f,1.6f,new(2571.4001f,9.72162f,1550.5f));
        entities.AddComponent(bay,new VehicleDockSlot(trucks[0],62,pose,owner));
        entities.AddComponent(trucks[0],new VehicleDockVisit(bay,currentGeneration ? 62UL : 61UL,VehicleDockStatus.Exiting,VehicleDockBlockReason.None,pose,ExitTurning:true,LaneEntered:true,DepartureDistance:5.2f));
        // Escape rests on the current physical mutual wait, not the visit generation.
        // The actual bay generation and complete physical clearance are checked every tick.
        double FuelTotal() => scene.Inventories.CaptureSnapshot().Inventories.SelectMany(i=>i.Resources).Where(r=>r.ResourceId==ResourceIds.Fuel).Sum(r=>r.Quantity);
        double fuelBefore=FuelTotal();
        for(int tick=0;tick<800;tick++)
        {
            scene.Simulation.AdvanceOneTick();
            if(entities.TryGetComponent(bay,out VehicleDockSlot slot))
            { Assert.Equal(trucks[0],slot.Truck); Assert.Equal(62UL,slot.Generation); Assert.Equal(pose,slot.Pose); }
            else Assert.False(pose.Contains(entities.GetComponent<WorldTransform>(trucks[0]).Position,pose.HalfLength));
        }
        Assert.InRange(FuelTotal(),0,fuelBefore);
        if(!hasFuel)
        {
            Assert.True(entities.HasComponent<VehicleDockSlot>(bay));
            for(int i=1;i<3;i++) Assert.Equal(new Vector2(positions[i].X,positions[i].Z),new Vector2(entities.GetComponent<WorldTransform>(trucks[i]).Position.X,entities.GetComponent<WorldTransform>(trucks[i]).Position.Z));
            return;
        }
        Assert.False(entities.HasComponent<VehicleDockSlot>(bay),string.Join(';',scene.Services.GroundMovement.CaptureNavigationState().Traffic.Vehicles));
        Assert.False(pose.Contains(entities.GetComponent<WorldTransform>(trucks[0]).Position,pose.HalfLength));
        for(int i=1;i<3;i++) Assert.Equal(goals[i], Assert.Single(scene.Services.GroundMovement.CaptureNavigationState().Actors,a=>a.Entity==trucks[i]).OriginalOrder.WorldTarget);
        if (currentGeneration)
        for(int i=1;i<3;i++) Assert.True(Vector3.Distance(positions[i],entities.GetComponent<WorldTransform>(trucks[i]).Position)>5,$"truck={i} position={entities.GetComponent<WorldTransform>(trucks[i])}");
    }
}
