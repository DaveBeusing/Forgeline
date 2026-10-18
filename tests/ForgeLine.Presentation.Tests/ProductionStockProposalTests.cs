using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class ProductionStockProposalTests
{
    [Fact]
    public void SharedCapacityRejectsIndividuallyFittingInputsAndLeavesPoliciesUntouched()
    {
        var logistics = Logistics(100, Policy(ResourceIds.Steel), Policy(ResourceIds.Fuel));
        var requirement = Requirement(70, 60);
        var proposal = ProductionStockProposal.Create(logistics, requirement, 1);
        Assert.False(proposal.FitsCapacity);
        Assert.Equal(10, logistics.Policies[0].DesiredTarget);
        Assert.All(logistics.Policies, p => Assert.False(p.HasPolicy));
        Assert.True(ProductionStockProposal.Create(Logistics(130, logistics.Policies.ToArray()), requirement, 1).FitsCapacity);
    }

    [Fact]
    public void ActiveReservationsRemainInStockUntilConsumptionAndDeliveriesAreCountedOnce()
    {
        var steel = Policy(ResourceIds.Steel) with { CurrentQuantity = 60, ReservedQuantity = 30, AnnouncedQuantity = 20 };
        var logistics = new PlayerLogisticsActionReadModel(new(10,1), [steel, Policy(ResourceIds.Fuel)], null, 200, 60);
        var proposal = ProductionStockProposal.Create(logistics, Requirement(70, 60), 1);
        var target = Assert.Single(proposal.Thresholds, t => t.Resource == ResourceIds.Steel);
        Assert.Equal(100, target.Target);
        Assert.Equal(30, target.Available);
        Assert.Equal(20, target.Missing);
        Assert.True(proposal.FitsCapacity);
        Assert.False(ProductionStockProposal.Create(logistics, Requirement(70,60), 2).FitsCapacity);
    }

    [Fact]
    public void PendingDemandAndAssignedLoadAreNotAddedTwiceOrDeclaredArrived()
    {
        var steel = Policy(ResourceIds.Steel) with { RequestedQuantity = 70, AnnouncedQuantity = 20 };
        var logistics = Logistics(130, steel, Policy(ResourceIds.Fuel));
        var proposal = ProductionStockProposal.Create(logistics, Requirement(70,60),1);
        Assert.True(proposal.FitsCapacity);
        Assert.Equal(0, proposal.Thresholds[0].Missing);
        Assert.Equal(0, proposal.Thresholds[0].Available);
        Assert.Equal(20, proposal.Thresholds[0].Announced);
        Assert.Equal(70, proposal.Thresholds[0].Target);
    }

    [Theory]
    [InlineData(VehicleDockStatus.Busy, VehicleQueueWaitReason.Predecessor, "WAITING FOR LOADING BAY")]
    [InlineData(VehicleDockStatus.Aligning, VehicleQueueWaitReason.None, "ALIGNING")]
    [InlineData(VehicleDockStatus.Exiting, VehicleQueueWaitReason.None, "LEAVING BAY")]
    [InlineData(VehicleDockStatus.Blocked, VehicleQueueWaitReason.GeometryBlocked, "ACCESS BLOCKED")]
    public void ServiceLabelsUseConfirmedQueueState(VehicleDockStatus status, VehicleQueueWaitReason reason, string expected)
    {
        var work = new PlayerWorkSummary(default,default,0,"","",new(new(70,3),9,VehicleQueueService.CargoPickup,4,8,status,reason,default,new(20)));
        Assert.Equal(expected, LogisticsStatusCatalog.Service(work));
        Assert.Equal(reason == VehicleQueueWaitReason.GeometryBlocked,LogisticsStatusCatalog.IsBlocked(work));
        if (!LogisticsStatusCatalog.IsBlocked(work)) Assert.Empty(LogisticsStatusCatalog.BlockedCause(work));
        Assert.Contains("POSITION 4", LogisticsStatusCatalog.ServiceDetails(work));
        Assert.Equal("ACCESS NOT OBSERVED", LogisticsStatusCatalog.Service(default));
    }

    [Fact]
    public void CatalogChangesAndAdditionalStepCountChangeProposalWithoutTextConstants()
    {
        var logistics = Logistics(1000, Policy(ResourceIds.Steel), Policy(ResourceIds.Fuel));
        var proposal = ProductionStockProposal.Create(logistics, Requirement(91,37), 3);
        Assert.Equal(273, proposal.Thresholds[0].Target);
        Assert.Equal(111, proposal.Thresholds[1].Target);
        Assert.Equal(3, proposal.Steps);
        Assert.Throws<ArgumentOutOfRangeException>(() => ProductionStockProposal.Create(logistics, Requirement(1,1),0));
    }

    private static PlayerProductionRequirement Requirement(double steel, double fuel) => new(2,1,"Catalog vehicle",
        [new(ResourceIds.Steel,"Steel",steel,0), new(ResourceIds.Fuel,"Fuel",fuel,0)]);
    private static PlayerStockPolicyActionReadModel Policy(ResourceId resource) => new(default,resource,"Resource",0,5,10,20,
        LogisticsStockPriority.Normal,true,default,default,default,default);
    private static PlayerLogisticsActionReadModel Logistics(double capacity, params PlayerStockPolicyActionReadModel[] policies) =>
        new(new(10,1), policies, null, capacity);
}
