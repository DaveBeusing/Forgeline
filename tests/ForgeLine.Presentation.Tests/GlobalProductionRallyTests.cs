using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.World;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class GlobalProductionRallyTests
{
    [Fact]
    public void ValidGroundEmitsOneFactoryAndSessionBoundIntent()
    {
        var snapshot = GlobalProductionTests.Snapshot();
        var controller = new RtsTacticalTargetingController();
        controller.BeginRally(new(1,1),snapshot.SessionId);
        var input = new InputState();
        input.Apply(PlatformInputEvent.PointerMoved(800,450));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,800,450));
        controller.Update(input,Camera(),new Terrain(),snapshot,1600,900,FormationTemplate.Compact);
        Assert.True(controller.PointerCaptured); Assert.False(controller.IsActive);
        Assert.True(controller.TryTakeRallyPoint(out var intent));
        Assert.Equal(new EntityId(1,1),intent.Factory); Assert.Equal(snapshot.SessionId,intent.Session);
        Assert.InRange(intent.Target.Length(),0,.01f);
        Assert.False(controller.TryTakeRallyPoint(out _));
        Assert.False(controller.TryTakeRequest(out _));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void InvalidGroundOutsideViewportBlockedPointerOrLostContextNeverSubmits(int change)
    {
        var snapshot=GlobalProductionTests.Snapshot(); var input=new InputState();
        var controller=new RtsTacticalTargetingController(); var camera=Camera();
        controller.BeginRally(new(1,1),snapshot.SessionId);
        controller.Update(input,camera,new Terrain(),snapshot,1600,900,FormationTemplate.Compact);
        input.BeginFrame();
        if(change==2) snapshot=GlobalProductionTests.Snapshot(session:2);
        if(change==3) snapshot=GlobalProductionTests.Snapshot(factory:new(1,2));
        if(change==5) input.Apply(PlatformInputEvent.FocusLost());
        int x=change==1 ? -1 : 800;
        input.Apply(PlatformInputEvent.PointerMoved(x,450));
        input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Left,x,450));
        controller.Update(input,camera,new Terrain(change!=0),snapshot,change==4?1920:1600,900,FormationTemplate.Compact,pointerBlocked:change==6);
        Assert.False(controller.TryTakeRallyPoint(out _));
        if(change is 2 or 3 or 4 or 5) Assert.False(controller.IsActive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EscapeAndRightClickCancelWithoutChangingAuthoritativeRally(bool right)
    {
        var snapshot=GlobalProductionTests.Snapshot(); var controller=new RtsTacticalTargetingController();
        controller.BeginRally(new(1,1),snapshot.SessionId); var input=new InputState();
        input.Apply(PlatformInputEvent.PointerMoved(800,450));
        if(right) input.Apply(PlatformInputEvent.MouseButtonChanged(PlatformInputEventKind.MouseButtonDown,PlatformMouseButton.Right,800,450));
        else input.Apply(PlatformInputEvent.KeyChanged(PlatformInputEventKind.KeyDown,PlatformKey.Escape));
        controller.Update(input,Camera(),new Terrain(),snapshot,1600,900,FormationTemplate.Compact);
        Assert.False(controller.IsActive); Assert.False(controller.TryTakeRallyPoint(out _));
        Assert.Equal(new Vector3(10,0,20),GlobalProductionSnapshot.Resolve(snapshot)!.Factories[0].RallyPoint);
    }
    private static RtsCamera Camera()=>new(new RtsCameraSettings {EdgeScrollEnabled=false,InitialTarget=Vector3.Zero,InitialDistance=100});
    private sealed class Terrain(bool valid=true):ITerrainQuery
    {
        public AxisAlignedBounds WorldBounds=>new(new(-10000,-100,-10000),new(10000,100,10000));
        public bool TrySampleHeight(float x,float z,out float height){height=0;return valid;}
        public bool TrySampleNormal(float x,float z,out Vector3 normal){normal=Vector3.UnitY;return valid;}
    }
}
