using System.Numerics;
using System.Text.Json;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class CrossingVisualLayoutTests
{
    [Theory]
    [InlineData("crossing.north_bridge",18)]
    [InlineData("crossing.south_ford",12)]
    public void MeshForwardFollowsTheExistingEdgeWithoutEditingTheMap(string key,float width)
    {
        var map=CentralDivideBattlefield.Create();
        var terrain=CentralDivideTerrainFactory.Create(map);
        var options=new JsonSerializerOptions { IncludeFields=true };
        string before=JsonSerializer.Serialize(new { map.RoadNodes,map.RoadEdges,map.Crossings,map.WorldObjects },options);
        var layout=CrossingVisualLayout.Create(map,terrain);
        var crossing=map.Crossings.Single(x=>x.Key==key);
        var edge=map.RoadEdges.Single(x=>x.Key==crossing.LogisticsEdgeKey);
        Vector3 a=map.RoadNodes.Single(x=>x.Key==edge.SourceNodeKey).Position;
        Vector3 b=map.RoadNodes.Single(x=>x.Key==edge.DestinationNodeKey).Position;
        Vector3 forward=Vector3.Transform(Vector3.UnitZ,layout.Crossings[key].Rotation);
        forward=Vector3.Normalize(new(forward.X,0,forward.Z));
        Assert.True(Vector3.Dot(forward,Vector3.Normalize(b-a))>.99999f);
        Assert.Equal(width,layout.Crossings[key].Scale.X);
        Assert.Equal(before,JsonSerializer.Serialize(new { map.RoadNodes,map.RoadEdges,map.Crossings,map.WorldObjects },options));
    }

    [Fact]
    public void SourceApronTopsMeetTheActualDeckEndsAndRoadAtMillimetreTolerance()
    {
        var map=CentralDivideBattlefield.Create();var terrain=CentralDivideTerrainFactory.Create(map);
        var layout=CrossingVisualLayout.Create(map,terrain);
        var feature=new InfrastructureFeaturePresentationMetadata(InfrastructurePresentationKind.RoadBridge,InfrastructurePresentationState.Operational);
        Matrix4x4 bridge=InfrastructurePresentationCatalog.AdjustWorldTransform(feature,layout.Crossings["crossing.north_bridge"].ToMatrix());
        Vector3 near=Vector3.Transform(new(0,-.07f,-.5f),bridge),far=Vector3.Transform(new(0,-.07f,.5f),bridge);
        Check("kit.bridge-apron.north",near,1507);
        Check("kit.bridge-apron.south",far,1565);
        void Check(string key,Vector3 expectedHigh,float outsideX)
        {
            var world=layout.Aprons[key].ToMatrix();
            Vector3 high=Vector3.Transform(new(0,.23f,-.5f),world);
            Vector3 low=Vector3.Transform(new(0,-.35f,.5f),world);
            Assert.InRange(Vector3.Distance(high,expectedHigh),0,.001f);
            Assert.InRange(MathF.Abs(low.X-outsideX),0,.001f);
            Assert.InRange(MathF.Abs(low.Z-920),0,.001f);
            Assert.True(terrain.TrySampleHeight(outsideX,920,out float height));
            Assert.InRange(MathF.Abs(low.Y-height-.021f),0,.001f);
        }
    }

    [Fact]
    public void ApproachesKeepTheTwelveMetreLaneAndPairedTwoPointFourMetreShoulders()
    {
        var map=CentralDivideBattlefield.Create();var layout=CrossingVisualLayout.Create(map,CentralDivideTerrainFactory.Create(map));
        Assert.Equal(234,layout.Approaches.Count);
        Assert.Equal(78,layout.Approaches.Count(x=>x.InfrastructureFeature.Kind==InfrastructurePresentationKind.RoadSegment));
        Assert.Equal(156,layout.Approaches.Count(x=>x.InfrastructureFeature.Kind==InfrastructurePresentationKind.RoadShoulder));
        Assert.Equal(234,layout.Approaches.Select(x=>x.Entity).Distinct().Count());
        Assert.All(layout.Approaches,x=>
        {
            Assert.False(x.Selectable.IsSelectable);
            float width=x.InfrastructureFeature.Kind==InfrastructurePresentationKind.RoadShoulder?x.Transform.Scale.X*.16f:x.Transform.Scale.X;
            Assert.Equal(x.InfrastructureFeature.Kind==InfrastructurePresentationKind.RoadShoulder?2.4f:12,width,4);
        });
    }

    [Theory]
    [InlineData(920)]
    [InlineData(2200)]
    public void DrawnTruckContactMatchesTheSurfaceWhileItsHorizontalPoseAndScaleStayFixed(float z)
    {
        var map=CentralDivideBattlefield.Create();var terrain=CentralDivideTerrainFactory.Create(map);
        var layout=CrossingVisualLayout.Create(map,terrain);
        var units=DirectorateContent.CreateUnitCatalog();
        foreach(float x in new float[] { 1511,1520,1536,1552,1561 })
        foreach(bool cargo in new bool[] { true,false })
        {
            var definition=units[cargo?UnitIds.CargoTruck:UnitIds.SupplyTruck];
            Assert.True(terrain.TrySampleHeight(x,z,out float ground));
            var original=new RenderTransform(new(x,ground+definition.Movement.HeightOffset,z),Quaternion.CreateFromAxisAngle(Vector3.UnitY,MathF.PI/2),definition.VisualScale);
            var drawn=layout.GroundTruck(original,cargo,true,true);
            Assert.Equal(original.Position.X,drawn.Position.X);Assert.Equal(original.Position.Z,drawn.Position.Z);
            Assert.Equal(original.Scale,drawn.Scale);
            Vector3 up=Vector3.Transform(Vector3.UnitY,drawn.Rotation);
            Vector3 contact=drawn.Position-up*definition.Movement.HeightOffset;
            Assert.True(layout.TryDrivingSurface(contact,out float floor,out _));
            Assert.InRange(MathF.Abs(contact.Y-floor),0,.001f);
            Assert.Equal(original,layout.GroundTruck(original,cargo,false,false));
        }
    }

    [Theory]
    [InlineData(920)]
    [InlineData(2200)]
    public void TheDrivingPlaneClearsCanonicalTerrainAcrossTheLane(float z)
    {
        var map=CentralDivideBattlefield.Create();var terrain=CentralDivideTerrainFactory.Create(map);
        var layout=CrossingVisualLayout.Create(map,terrain);
        for(int i=-21;i<=21;i++)
        foreach(float side in new float[] { -6,0,6 })
        {
            Vector3 point=new(1536+i,0,z+side);
            Assert.True(layout.TryDrivingSurface(point,out float floor,out _));
            Assert.True(terrain.TrySampleHeight(point.X,point.Z,out float ground));
            Assert.True(floor>=ground-.001f);
        }
    }
}
