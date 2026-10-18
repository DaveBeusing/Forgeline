using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class InfantryAnimationTests
{
    static InfantryActionInput Live => new(true,false,false,false,false,0,false,false);
    [Fact]
    public void ActionsFollowCopiedGameplayStateAndNeverWallClock()
    {
        InfantryAnimationPose Step(InfantryActionInput input,double speed=0,double shot=-1)=>InfantryAnimationState.Advance(default,input,.05,speed,shot);
        Assert.Equal(InfantryAction.Idle,Step(Live).Action);
        Assert.Equal(InfantryAction.Walk,Step(Live,1.8).Action);
        Assert.Equal(InfantryAction.Run,Step(Live,3.2).Action);
        Assert.Equal(InfantryAction.Aim,Step(Live with {Aiming=true}).Action);
        Assert.Equal(InfantryAction.Suppression,Step(Live with {Suppressed=true},3.2).Action);
        Assert.Equal(InfantryAction.Fire,Step(Live,shot:0).Action);
        Assert.Equal(InfantryAction.Reload,Step(Live with {Reloading=true,ReloadRemainingSeconds=2}).Action);
        Assert.Equal(InfantryAction.Hit,Step(Live with {Hit=true},shot:0).Action);
        var death=Step(Live with {Wreck=true,FreshDeath=true});Assert.Equal(InfantryAction.Death,death.Action);
        Assert.False(Step(Live with {Wreck=true}).IsSpecified);
        Assert.False(InfantryAnimationState.Advance(death with{Phase=.99},Live with{Wreck=true},.05,0,-1).IsSpecified);
        var pose=new InfantryAnimationPose(true,InfantryAction.Walk,1,true);
        Assert.Equal(pose,InfantryAnimationState.Advance(pose,Live,0,1.8,-1));
        Assert.Equal(1.05,InfantryAnimationState.Advance(pose,Live,.05,1.8,-1).Phase,6);
    }
    [Fact]
    public void RepeatedTicksSessionsHiddenEntitiesAndReusedIdentityDoNotInheritMotion()
    {
        var history=new MechanicalPoseHistory();var id=new EntityId(1,1);
        history.Begin(new(1),1,.05);history.Capture(id,Vector3.Zero,Quaternion.Identity,false,false,0,false,infantry:Live);
        history.Begin(new(1),2,.05);
        var walk=history.Capture(id,new(.09f,0,0),Quaternion.Identity,false,true,0,false,infantry:Live);
        Assert.Equal(InfantryAction.Fire,walk.Infantry.Action);
        history.Begin(new(1),2,.05);Assert.Equal(walk,history.Capture(id,Vector3.One,Quaternion.Identity,false,false,0,false,infantry:Live));
        history.Begin(new(2),2,.05);
        Assert.Equal(InfantryAction.Idle,history.Capture(id,Vector3.One,Quaternion.Identity,false,false,0,false,infantry:Live).Infantry.Action);
        history.Begin(new(2),3,.05);history.Begin(new(2),4,.05);
        Assert.Equal(InfantryAction.Idle,history.Capture(id,new(20),Quaternion.Identity,false,false,0,false,infantry:Live).Infantry.Action);
        history.Begin(new(2),5,.05);
        Assert.Equal(0,history.Capture(new(1,2),new(22),Quaternion.Identity,false,false,0,false,infantry:Live).Infantry.Phase);
    }
    [Fact]
    public void DeathUsesTheLiveNearMeshOnlyDuringItsBoundedPoseAndDistantMeshesStayStatic()
    {
        var instance = new RenderInstance(new EntityId(1,1),new(Vector3.Zero,Quaternion.Identity,new(1.8f)),new(1),RenderMaterialHandle.Default,RenderVisibilityMask.World,
            UnitFeature:new(ForgeLine.Game.UnitIds.RifleSquad,UnitPresentationDamageState.Intact));
        var bounds = PresentationBounds.ResolveAxisAlignedBounds(instance);
        instance = instance with {MechanicalPose = new MechanicalPose(true,0,0,1,-1,0,0){Infantry=new(true,InfantryAction.Walk,.5,true)}};
        Assert.Equal(0,SkinPresentationBounds.SelectLod(instance,200,0,1));
        Assert.Equal(1,SkinPresentationBounds.SelectLod(instance,50,0,1));
        Assert.Equal(2,SkinPresentationBounds.SelectLod(instance,3,0,1));
        Assert.Equal(bounds,PresentationBounds.ResolveAxisAlignedBounds(instance));
        Assert.NotEqual(bounds,SkinPresentationBounds.Resolve(instance));
        instance = instance with {UnitFeature=instance.UnitFeature with {DamageState=UnitPresentationDamageState.Wreck}};
        Assert.Equal(2,SkinPresentationBounds.SelectLod(instance,100,0,1));
        instance = instance with {MechanicalPose=instance.MechanicalPose with{Infantry=new(true,InfantryAction.Death,.5,false)}};
        Assert.Equal(0,SkinPresentationBounds.SelectLod(instance,100,0,1));
        Assert.Equal(2,SkinPresentationBounds.SelectLod(instance,3,0,1));
    }
    [Fact]
    public void InterpolationClampsAndSwitchesActionsWithoutBlendingUnrelatedStates()
    {
        var first=new InfantryAnimationPose(true,InfantryAction.Walk,1,true);var second=first with{Phase=2};
        Assert.Equal(1.5,InfantryAnimationPose.Interpolate(first,second,.5f).Phase);
        Assert.Equal(second,InfantryAnimationPose.Interpolate(first,second,2));
        var fire=new InfantryAnimationPose(true,InfantryAction.Fire,0,false);
        Assert.Equal(fire,InfantryAnimationPose.Interpolate(first,fire,.5f));
    }
}

