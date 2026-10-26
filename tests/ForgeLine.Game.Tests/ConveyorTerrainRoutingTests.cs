using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Game.Tests;

public sealed class ConveyorTerrainRoutingTests
{
    [Theory]
    [InlineData(.04f)]
    [InlineData(-.04f)]
    public void MineConnectsToSmelterAtADifferentFoundationHeightAndDeliversOre(float slope)
    {
        var fixture = new ConveyorTestFixture(terrainHeight: (x, _) => 8 + x * slope);
        EntityId mine = fixture.AddBuilding(BuildingIds.Extractor, Foundation(fixture, BuildingIds.Extractor, 64, 96));
        EntityId deposit = fixture.Entities.CreateEntity();
        var faction = new FactionId(1);
        fixture.Entities.AddComponent(deposit, new ResourceDeposit(ResourceIds.FerrousOre,
            new AxisAlignedBounds(new(55, -10, 87), new(73, 30, 105)),
            totalQuantity:30,baseExtractionRatePerSecond:7,owner:faction));
        fixture.Entities.AddComponent(mine, new ResourceExtractor(deposit, ResourceIds.FerrousOre, 7, faction, outputInventory: mine));
        var extraction = new ResourceExtractionSystem(inventories: fixture.Inventories);
        fixture.Simulation.RegisterSystem(extraction);
        EntityId smelter = fixture.AddProduction(BuildingIds.Smelter, RecipeIds.Steel,
            Foundation(fixture, BuildingIds.Smelter, 160, 96));
        var output = fixture.Port(mine, false);
        var input = fixture.Port(smelter, true);
        Assert.True(MathF.Abs(output.Position.Y-input.Position.Y)>1);
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position-Vector3.UnitY*5.8f, input.Position-Vector3.UnitY*5.8f,
            BuildingOrientation.North, 0, true, SourcePortBuilding: mine,
            DestinationPortBuilding: smelter, AutomaticRouting: true,AutomaticHeight:true);
        Assert.Equal(ConveyorFailure.PortMismatch,fixture.Construction.Preview(fixture.Entities,fixture.Player,
            request with {AutomaticHeight=false},fixture.Payer).Failure);
        var plan = fixture.Construction.Preview(fixture.Entities, fixture.Player, request, fixture.Payer);
        Assert.True(plan.IsValid, $"Mine-to-smelter slope {slope}: {plan.Failure}");
        fixture.Build(request);
        fixture.RunUntil(() => fixture.Stock(smelter, ResourceIds.FerrousOre) >= 30-1e-7);
        Assert.InRange(fixture.Stock(mine, ResourceIds.FerrousOre), 0, 1e-7);
        Assert.Equal(0, fixture.Entities.GetComponent<ResourceDeposit>(deposit).RemainingQuantity, 7);
        Assert.Equal(30, fixture.Total(ResourceIds.FerrousOre), 7);
        ValidateGeometry(fixture, plan);
        fixture.Simulation.SubmitCommand(PlayerProductionActionCommand.Queue(fixture.Player,smelter,RecipeIds.Steel,
            fixture.Simulation.CurrentTick),new SimulationTick(fixture.Simulation.CurrentTick.Value+1));
        fixture.RunUntil(()=>fixture.Entities.GetComponent<ProductionFacility>(smelter).CompletedCycles==1);
        Assert.Equal(10,fixture.Inventories.GetQuantity(fixture.Output(smelter),ResourceIds.Steel),7);
        Assert.Equal(20,fixture.Total(ResourceIds.FerrousOre),7);
    }

    [Fact]
    public void IntermediateHillRaisesTheRouteAndExtendsSupportsToLocalGround()
    {
        var fixture = new ConveyorTestFixture(terrainHeight: (x, _) => MathF.Max(0,3-MathF.Abs(x-112)*.125f));
        EntityId source=fixture.AddStorage(Foundation(fixture,BuildingIds.StorageDepot,64,96));
        EntityId destination=fixture.AddStorage(Foundation(fixture,BuildingIds.StorageDepot,176,96));
        var output=fixture.Port(source,false);var input=fixture.Port(destination,true);
        var request=new ConveyorBuildRequest(ConveyorEdit.Build,ConveyorModule.Route,
            output.Position-Vector3.UnitY*5.8f,input.Position-Vector3.UnitY*5.8f,
            BuildingOrientation.North,0,true,SourcePortBuilding:source,DestinationPortBuilding:destination,AutomaticRouting:true,AutomaticHeight:true);
        var plan=fixture.Construction.Preview(fixture.Entities,fixture.Player,request,fixture.Payer);
        Assert.True(plan.IsValid,plan.Failure.ToString());
        Assert.Contains(plan.Pieces,p=>p.RiseMeters!=0);
        Assert.Contains(plan.Pieces,p=>p.SupportHeightMeters>0);
        double steel=fixture.Stock(fixture.Payer,ResourceIds.Steel);
        fixture.Build(request);
        Assert.Equal(steel-plan.Steel,fixture.Stock(fixture.Payer,ResourceIds.Steel),7);
        ValidateGeometry(fixture,plan);
        fixture.Give(source,ResourceIds.FerrousOre,11.5);
        fixture.RunUntil(()=>fixture.Stock(destination,ResourceIds.FerrousOre)>=11.5-1e-7);
        Assert.Equal(11.5,fixture.Total(ResourceIds.FerrousOre),7);
        var graded=fixture.Carriers.First(e=>fixture.Entities.GetComponent<ConveyorPiece>(e).Placement.RiseMeters!=0);
        var before=fixture.Entities.GetComponent<ConveyorPiece>(graded).Placement;
        var replacement=fixture.Edit(graded,ConveyorEdit.Replace);
        Assert.True(replacement.Accepted,replacement.Result.ToString());
        Assert.Contains(fixture.Carriers,e=>fixture.Entities.GetComponent<ConveyorPiece>(e).Placement==before);
    }

    [Fact]
    public void UnreachableHeightRejectsWithoutPaymentOrPartialConstruction()
    {
        var fixture=new ConveyorTestFixture();
        EntityId source=fixture.AddStorage(new(64,0,96));
        EntityId destination=fixture.AddStorage(new(176,80,96));
        var output=fixture.Port(source,false);var input=fixture.Port(destination,true);
        var request=new ConveyorBuildRequest(ConveyorEdit.Build,ConveyorModule.Route,
            output.Position-Vector3.UnitY*5.8f,input.Position-Vector3.UnitY*5.8f,
            BuildingOrientation.North,0,true,SourcePortBuilding:source,DestinationPortBuilding:destination,AutomaticRouting:true,AutomaticHeight:true);
        double steel=fixture.Stock(fixture.Payer,ResourceIds.Steel);
        var command=fixture.Execute(request);
        Assert.False(command.Accepted);
        Assert.Equal(ConveyorFailure.Terrain,command.Result);
        Assert.Empty(fixture.Pieces);
        Assert.Equal(steel,fixture.Stock(fixture.Payer,ResourceIds.Steel));
    }

    private static void ValidateGeometry(ConveyorTestFixture fixture,ConveyorPlan plan)
    {
        foreach(var part in plan.Pieces)
        {
            if(part.IsSupport)
            {
                Assert.True(fixture.Terrain.TrySampleHeight(part.Pivot.X,part.Pivot.Z,out float ground));
                Assert.Equal(ground,part.Bounds.Minimum.Y,4);
                Assert.Contains(plan.Pieces,carrier=>!carrier.IsSupport && carrier.Sockets.Any(socket=>
                    Vector2.Distance(new(socket.Position.X,socket.Position.Z),new(part.Pivot.X,part.Pivot.Z))<.02f &&
                    MathF.Abs(part.Bounds.Maximum.Y-(socket.Position.Y-.58f))<.02f));
                continue;
            }
            var bounds=part.Bounds;
            for(float x=bounds.Minimum.X;x<=bounds.Maximum.X;x+=.25f)
            for(float z=bounds.Minimum.Z;z<=bounds.Maximum.Z;z+=.25f)
            {
                Assert.True(fixture.Terrain.TrySampleHeight(x,z,out float height));
                Assert.True(part.UndersideAt(x,z)>=height+5-.001f);
            }
        }
    }

    private static Vector3 Foundation(ConveyorTestFixture fixture, BuildingId id, float x, float z)
    {
        Vector3 half = ConveyorGeometry.BuildingPortDimensions(id)/2;
        float maximum = float.NegativeInfinity;
        foreach(float dx in new[]{-half.X,half.X})
        foreach(float dz in new[]{-half.Z,half.Z})
        {
            Assert.True(fixture.Terrain.TrySampleHeight(x+dx,z+dz,out float height));
            maximum=MathF.Max(maximum,height);
        }
        return new(x,maximum,z);
    }
}
