using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class RadialLogisticsOrderTests
{
    [Fact]
    public void FiftyUnitEditorAdvancesWithoutTouchingLegacyValues()
    {
        Assert.Equal(50, LogisticsQuantityEditor.Adjust(0, 1));
        Assert.Equal(100, LogisticsQuantityEditor.Adjust(50, 1));
        Assert.Equal(150, LogisticsQuantityEditor.Adjust(100, 1));
        Assert.Equal(100, LogisticsQuantityEditor.Adjust(150, -1));
        Assert.Equal(50, LogisticsQuantityEditor.Adjust(50, -1));
        Assert.Equal(50, LogisticsQuantityEditor.Adjust(100, -1));
        Assert.Equal(50, LogisticsQuantityEditor.Adjust(37, 1));
        Assert.Equal(37, LogisticsQuantityEditor.Adjust(37, -1));
        Assert.Equal(100, LogisticsQuantityEditor.Adjust(73, 1));
        Assert.Equal(50, LogisticsQuantityEditor.Adjust(73, -1));
        Assert.Equal(50, LogisticsQuantityEditor.Adjust(50, 1, upper: 80));
        Assert.Equal(100, LogisticsQuantityEditor.Adjust(100, -1, lower: 75));
        Assert.Equal(50, LogisticsQuantityEditor.Adjust(50, 1, lower: 150));
        Assert.Equal(100, LogisticsQuantityEditor.Adjust(100, 1, upper: 100));
        Assert.Equal(double.MaxValue, LogisticsQuantityEditor.Adjust(double.MaxValue, 1));
        Assert.Equal(0, LogisticsQuantityEditor.Adjust(0, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(150)]
    public void CopiedActiveOrdersRemainBoundedAndPageableWithStableIdentities(int total)
    {
        var orders = Enumerable.Range(0, total).Select(i =>
            new PlayerLogisticsOrderReadModel(new((ulong)i + 1), ResourceIds.Steel,
                "Steel", i % 2 == 0 ? LogisticsTransportRequestState.InTransit :
                    LogisticsTransportRequestState.RetryPending,
                i % 2 == 0 ? LogisticsTransportRequestFailureReason.None :
                    LogisticsTransportRequestFailureReason.NoTruckAvailable,
                LogisticsBottleneckReason.None, 50 * (i + 1), 0,
                EntityId.Invalid, EntityId.Invalid, EntityId.Invalid, "", "")).ToArray();
        var model = new PlayerLogisticsActionReadModel(new(7, 1), [], null, orders: orders)
        { TotalOrderCount = total };
        Assert.Equal(Math.Min(total, PlayerLogisticsActionReadModel.MaximumVisibleOrders), model.Orders.Count);
        Assert.Equal(model.Orders.Count, model.ItemCount);
        var actions = new PlayerActionSnapshot(new(1), new(4), [], 0, null, null, model);
        for (int i = 0; i < model.ItemCount; i++)
        {
            Assert.Equal("READ ONLY", PlayerActionDockHudModel.ResolveItemState(
                PlayerActionPanelMode.Logistics, i, actions).DisabledReason);
            Assert.Equal("Steel", PlayerActionDockHudModel.ResolveItemTitle(
                PlayerActionPanelMode.Logistics, i, actions));
            var key = ObjectActionRingView.ItemKey(PlayerActionPanelMode.Logistics, i, actions);
            Assert.Equal((byte)7, key.Kind);
            Assert.Equal((uint)(i + 1), key.Id);
        }
        Assert.True(model.ItemCount <= 128);
        Assert.Equal((model.ItemCount + 3) / 4, (int)Math.Ceiling(model.ItemCount / 4d));
    }

    [Fact]
    public void HighRequestIdKeepsBothHalvesAndCopiedListCannotBeMutated()
    {
        ulong stable = (1UL << 40) + 12;
        var order = new PlayerLogisticsOrderReadModel(new(stable), ResourceIds.Steel, "Steel",
            LogisticsTransportRequestState.Assigned, LogisticsTransportRequestFailureReason.None,
            LogisticsBottleneckReason.None, 150, 50, new(5, 2), new(8, 1), new(7, 1),
            "Depot", "Factory");
        var source = new List<PlayerLogisticsOrderReadModel> { order };
        var model = new PlayerLogisticsActionReadModel(new(7, 1), [], null, orders: source);
        source.Clear();
        Assert.Single(model.Orders);
        var actions = new PlayerActionSnapshot(new(1), new(4), [], 0, null, null, model);
        var key = ObjectActionRingView.ItemKey(PlayerActionPanelMode.Logistics, 0, actions);
        Assert.Equal((uint)12, key.Id);
        Assert.Equal((uint)(1UL << 8), key.Generation);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<PlayerLogisticsOrderReadModel>)model.Orders)[0] = order with { RequestedQuantity = 0 });
    }

    [Fact]
    public void OldSnapshotCannotAuthorizeLogisticsOrdersInANewSession()
    {
        var model = new PlayerLogisticsActionReadModel(new(7, 1), [], null, orders:
            [new(new(1), ResourceIds.Steel, "Steel", LogisticsTransportRequestState.Pending,
                LogisticsTransportRequestFailureReason.None, LogisticsBottleneckReason.None,
                50, 0, default, default, default, "", "")]);
        var actions = new PlayerActionSnapshot(new(1), new(4), [], 0, null, null, model);
        var stale = new PresentationSnapshot(new(4), TimeSpan.Zero, 0, [],
            sessionId: new(2), playerActions: actions);
        Assert.Null(PlayerActionDockHudModel.ResolveActions(stale));
        var staleTick = new PresentationSnapshot(new(5), TimeSpan.Zero, 0, [],
            sessionId: new(1), playerActions: actions);
        Assert.Null(PlayerActionDockHudModel.ResolveActions(staleTick));
    }

    [Theory]
    [InlineData(LogisticsTransportRequestState.Pending)]
    [InlineData(LogisticsTransportRequestState.Assigned)]
    [InlineData(LogisticsTransportRequestState.InTransit)]
    [InlineData(LogisticsTransportRequestState.RetryPending)]
    [InlineData(LogisticsTransportRequestState.Failed)]
    public void ActiveOrderStatesRemainLiteralNotImpliedDelivery(LogisticsTransportRequestState state)
    {
        Assert.NotEqual(LogisticsTransportRequestState.Completed, state);
        var order = new PlayerLogisticsOrderReadModel(new(8), ResourceIds.Steel, "Steel", state,
            LogisticsTransportRequestFailureReason.NoRoute, LogisticsBottleneckReason.DisconnectedRoute,
            100, 0, default, default, default, "", "");
        Assert.Equal(state, order.State);
        Assert.Equal(0, order.LoadedQuantity);
        Assert.Equal(100, order.RequestedQuantity);
    }
}
