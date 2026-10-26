using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;
using Xunit;
namespace ForgeLine.Game.Tests;
public sealed class QuantizedStraightForwardTests
{
    [Theory]
    [InlineData(0,true)]
    [InlineData(1,true)]
    [InlineData(2,false)]
    public void RoundedStraightClearancePreservesHeadingWhileRealLateralTargetsStillSteer(int control,bool moves)
    {
        using var scene=CentralDivideScenario.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Gameplay,2026) with
        { Participants=CentralDivideScenario.CreateDefaultParticipants(false,false) },TestContext.Current.CancellationToken);
        var caps=NavigationCapabilities.For(NavigationMovementClass.Wheeled);var world=scene.Services.Navigation.World;
        var snapshot=new NavigationWorldSnapshot(world,world.GetSectorGraph(caps),caps,scene.Terrain,0,1,NavigationModuleAvailability.NotConnected,[],[]);
        var entity=new EntityId(5000,1);var owner=new PlayerId(2); const float heading=-1.5500659f;
        var pose=new NavigationPose(new(2624.428f,9.33169f,1558.8304f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,heading));
        var profile=new NavigationVehicleProfile(new(1.6f,1.2f,3.2f),.25f,14.5f,7,10,2.5f);
        var root=new MovementOrder(owner,new(2568,8.693897f,1560),SimulationTick.Zero,new(1));
        var vehicle=new NavigationVehicleObservation(entity,owner,pose,profile,GroundMovementState.Stationary(heading),root,root,null,null,null,null);
        var binding=new NavigationOrderBinding(entity,root,1,1,0,0);
        var request=new NavigationSearchSpecification(1,pose,root.WorldTarget,NavigationTargetSemantics.Transit,new(0,.1f,null,0),caps,profile,snapshot.Revision,snapshot.KnowledgeScope,10000,100000,4);
        var route=new NavigationRouteProposal(request,root.WorldTarget,[root.WorldTarget],[],default,128);
        var bodies=new List<NavigationCollisionBody>{new(NavigationReadAdapter.GetObstacleIdentity(entity),NavigationObstacleKind.Vehicle,pose,profile.HalfExtents,Vector3.Zero)};
        if(control!=1)
        {
            bodies.Add(new(new(NavigationObstacleSource.Entity,5001,1),NavigationObstacleKind.Vehicle,new(new(2622.5999f,9.324579f,1553.7333f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,-.020107135f)),profile.HalfExtents,Vector3.Zero));
            bodies.Add(new(new(NavigationObstacleSource.Entity,5002,1),NavigationObstacleKind.Vehicle,new(new(2617.5093f,9.397788f,1558.7842f),Quaternion.CreateFromAxisAngle(Vector3.UnitY,-1.5468751f)),profile.HalfExtents,Vector3.Zero));
        }
        var collision=new NavigationCollisionFrame(snapshot,50,new AxisAlignedBounds(new(2500,-100,1450),new(2750,100,1650)),true,bodies.ToArray(),10,bodyPosesAreFixed:true);
        var target=pose.Position+new Vector3(MathF.Sin(heading),0,MathF.Cos(heading))*.25f;
        if(control==1) target+=Vector3.Transform(Vector3.UnitX,pose.Rotation)*.25f;
        var traffic=new NavigationTrafficDirective(NavigationModuleAvailability.Available,binding,new(50),NavigationTrafficAction.Proceed,1,target,NavigationBlockReason.MovingVehicle);
        var input=new NavigationMotionInput(binding,new(50),.05f,vehicle,traffic,route,14.5f,control!=2);
        var geometry=new NavigationVehicleGeometry(snapshot,scene.Terrain,128,.5f);
        var proposal=new ContinuousNavigationMotionPlanner(new(128,8,12,2,3)).Propose(input,collision,geometry,100000,out int work);
        Assert.InRange(work,0,100000);Assert.Equal(binding,proposal.Binding);
        Assert.True(moves==(proposal.Start!=proposal.End),$"proposal={proposal}");
        if(!moves){Assert.Equal(0,proposal.DesiredSpeed);return;}
        Assert.InRange(proposal.DesiredSpeed,.0001f,1);
        if(control==0) Assert.Equal(pose.Rotation,proposal.End.Rotation);
        else Assert.NotEqual(pose.Rotation,proposal.End.Rotation);
        Assert.Equal(NavigationGeometryStatus.Clear,geometry.Sweep(collision,new(NavigationReadAdapter.GetObstacleIdentity(entity),proposal.Start,proposal.End,profile,.05f),10000).Status);
    }
}
