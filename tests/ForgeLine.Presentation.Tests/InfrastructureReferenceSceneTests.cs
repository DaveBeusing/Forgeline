using System.Numerics;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfrastructureReferenceSceneTests
{
    private static readonly string[] BridgeMeshes =
    [
        "infrastructure.directorate.bridge.road.intact","infrastructure.directorate.bridge.road.damaged",
        "infrastructure.directorate.bridge.road.destroyed"
    ];
    [Theory]
    [InlineData(VisualReferenceSceneKind.InfrastructureSingle,1)]
    [InlineData(VisualReferenceSceneKind.InfrastructureHundred,100)]
    [InlineData(VisualReferenceSceneKind.InfrastructureThousand,1000)]
    public void RepeatedLoadsHaveExactCountsAndExistingStateBindings(VisualReferenceSceneKind kind,int count)
    {
        var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene=VisualReferenceScene.Create(kind,terrain);
        var instances=scene.Snapshot.Instances.ToArray();
        Assert.Equal(count,instances.Length);
        Assert.Equal(count,instances.Select(x=>x.Entity).Distinct().Count());
        Assert.All(instances,x=>
        {
            Assert.Equal(InfrastructurePresentationKind.RoadBridge,x.InfrastructureFeature.Kind);
            Assert.False(x.Selectable.IsSelectable);
            Assert.Contains(InfrastructurePresentationCatalog.ResolveMeshAssetId(x.InfrastructureFeature),BridgeMeshes);
        });
        Assert.Equal(scene.Fingerprint,VisualReferenceScene.Create(kind,terrain).Fingerprint);
    }

    [Theory]
    [InlineData(VisualReferenceSceneKind.InfrastructureCurveShort)]
    [InlineData(VisualReferenceSceneKind.InfrastructureCurveLong)]
    [InlineData(VisualReferenceSceneKind.InfrastructureTJunction)]
    [InlineData(VisualReferenceSceneKind.InfrastructureCrossJunction)]
    public void EachJunctionContainsAnActualMovingTruckAndItsAuthoredSurface(VisualReferenceSceneKind kind)
    {
        var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        var scene=VisualReferenceScene.Create(kind,terrain);
        var instances=scene.Snapshot.Instances.ToArray();
        Assert.Single(instances,x=>x.InfrastructureFeature.IsSpecified);
        var truck=Assert.Single(instances,x=>x.UnitFeature.Unit==UnitIds.CargoTruck);
        Assert.True(truck.MechanicalPose.IsSpecified);
        Assert.True(float.IsFinite(truck.Transform.Position.Y));
        Assert.Single(instances,x=>x.WorldFeature.Visual==WorldVisualId.PropLoadingApron);
    }

    [Theory]
    [InlineData(920)]
    [InlineData(2200)]
    public void RealCargoMovementTraversesEachExistingCrossing(float z)
    {
        var terrain=CentralDivideTerrainFactory.Create(CentralDivideBattlefield.Create());
        Vector3[] path=[new(1486,0,z),new(1586,0,z)];
        var result=InfrastructureReferenceScene.SimulatePath(terrain,null,path,.5f);
        Assert.Equal(1,result.AcceptedCommands);
        Assert.True(result.Trace.Length>2);
        Assert.True(result.Trace[0].Position.X<1515);
        Assert.True(result.Trace[^1].Position.X>1557);
        Assert.InRange(result.Truck.Transform.Position.X,1526,1546);
        Assert.All(result.Trace,x=>Assert.InRange(MathF.Abs(x.Position.Z-z),0,6));
        Assert.True(result.Truck.MechanicalPose.IsSpecified);
    }
}
