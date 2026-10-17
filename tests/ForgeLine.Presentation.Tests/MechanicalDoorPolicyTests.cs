using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class MechanicalDoorPolicyTests
{
    [Fact]
    public void ForeignAndUnspecifiedViewersCannotReadPrivateProductionStatus()
    {
        Assert.True(MechanicalDoorPolicy.CanReadProduction(new(1),new(1)));
        Assert.False(MechanicalDoorPolicy.CanReadProduction(new(1),new(2)));
        Assert.False(MechanicalDoorPolicy.CanReadProduction(default,default));
        Assert.False(MechanicalDoorPolicy.CanReadProduction(new(1),default));
    }

    [Theory]
    [InlineData(UnitProductionStatus.Idle,true)]
    [InlineData(UnitProductionStatus.Running,true)]
    [InlineData(UnitProductionStatus.NoInput,true)]
    [InlineData(UnitProductionStatus.NoPower,false)]
    [InlineData(UnitProductionStatus.Paused,false)]
    public void ProductionStateGatesDoorMotion(UnitProductionStatus status,bool expected) =>
        Assert.Equal(expected,MechanicalDoorPolicy.CanMove(BuildingPresentationState.Idle,status));

    [Theory]
    [InlineData(BuildingPresentationState.Unpowered)]
    [InlineData(BuildingPresentationState.Destroyed)]
    [InlineData(BuildingPresentationState.ConstructionFrame)]
    [InlineData(BuildingPresentationState.Critical)]
    public void NonOperationalMechanismsFreeze(BuildingPresentationState state) =>
        Assert.False(MechanicalDoorPolicy.CanMove(state,UnitProductionStatus.Running));

    [Fact]
    public void ProcessorPauseAndPowerLossAlsoFreezeDoors()
    {
        Assert.False(MechanicalDoorPolicy.CanMove(BuildingPresentationState.Idle,processing:ProductionStatus.Paused));
        Assert.False(MechanicalDoorPolicy.CanMove(BuildingPresentationState.Operational,processing:ProductionStatus.NoPower));
        Assert.True(MechanicalDoorPolicy.CanMove(BuildingPresentationState.Idle,processing:ProductionStatus.OutputFull));
    }

    [Fact]
    public void HalfOpenDoorFreezesThenClosesAfterPowerAndPauseResume()
    {
        var history=new MechanicalPoseHistory();var entity=new EntityId(7,1);
        MechanicalPose Capture(ulong tick,bool open,bool powered)
        {
            history.Begin(SimulationSessionId.None,tick,.05);
            return history.Capture(entity,Vector3.Zero,Quaternion.Identity,open,false,0,open,powered);
        }
        Capture(1,true,true);var opened=Capture(2,true,true);
        Assert.Equal(.05f,opened.Door,6);
        Assert.Equal(opened.Door,Capture(3,false,false).Door);
        Assert.Equal(opened.Door,Capture(4,false,false).Door);
        Assert.Equal(0,Capture(5,false,true).Door);
    }

    [Fact]
    public void WarmedDoorHistoryAndPolicyAllocateNothing()
    {
        var history=new MechanicalPoseHistory();var entity=new EntityId(7,1);
        void Step(ulong tick)
        {
            history.Begin(SimulationSessionId.None,tick,.05);
            bool drive=MechanicalDoorPolicy.CanMove(BuildingPresentationState.Idle,
                tick%2==0?UnitProductionStatus.Paused:UnitProductionStatus.Running);
            history.Capture(entity,Vector3.Zero,Quaternion.Identity,false,false,0,false,drive);
        }
        for(ulong i=1;i<=1024;i++)Step(i);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(ulong i=1025;i<=2048;i++)Step(i);
        Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before);
    }
}
