using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ConveyorTerrainPresentationTests
{
    [Theory]
    [InlineData(2f)]
    [InlineData(-2f)]
    public void ExtractedGradedMeshMeetsItsActualSocketHeights(float rise)
    {
        var placement=new ConveyorPlacement(ConveyorModule.Standard,new(12,3,8),BuildingOrientation.East,0,RiseMeters:rise);
        RenderInstance instance=Extract(placement);
        var local=ConveyorGeometry.Sockets(ConveyorModule.Standard);
        for(int i=0;i<local.Length;i++)
        {
            Vector3 rendered=instance.Transform.Position+Vector3.Transform(local[i].Position*instance.Transform.Scale/18,
                instance.Transform.Rotation);
            Vector3 expected=new(12+(i==0?-4:4),3+5.8f+(i==0?-rise/2:rise/2),8);
            Assert.InRange(Vector3.Distance(rendered,expected),0,.0001f);
            Assert.InRange(Vector3.Distance(placement.Sockets[i].Position,expected),0,.0001f);
        }
        Assert.Equal(placement.Bounds,PresentationBounds.ResolveAxisAlignedBounds(instance));
        Assert.Equal(placement,instance.ConveyorFeature.Placement);
    }

    [Fact]
    public void ExtractedExtendedSupportStartsAtGroundAndReachesItsSpecifiedHeight()
    {
        var placement=new ConveyorPlacement(ConveyorModule.RaisedSupport,new(12,2,8),BuildingOrientation.North,1,
            SupportHeightMeters:9);
        var instance=Extract(placement);
        Vector3 renderedTop=instance.Transform.Position+Vector3.Transform(new Vector3(0,7.22f,0)*instance.Transform.Scale/18,
            instance.Transform.Rotation);
        Assert.InRange(Vector3.Distance(renderedTop,new(12,11,8)),0,.0001f);
        Assert.Equal(2,placement.Bounds.Minimum.Y,4);
        Assert.Equal(11,placement.Bounds.Maximum.Y,4);
    }

    private static RenderInstance Extract(ConveyorPlacement placement)
    {
        var simulation=new SimulationCoordinator();
        var buffer=new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer));
        var entity=simulation.Entities.CreateEntity();
        simulation.Entities.AddComponent(entity,new WorldTransform(placement.Bounds.Center,Quaternion.Identity,Vector3.One));
        simulation.Entities.AddComponent(entity,new VisualIdentity(1));
        simulation.Entities.AddComponent(entity,new ConveyorPiece(new PlayerId(1),placement,ConveyorGeometry.Cost(placement)));
        simulation.AdvanceOneTick();
        Assert.True(buffer.TryReadLatest(out var snapshot));
        return Assert.Single(snapshot.Instances.ToArray());
    }
}
