using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Simulation;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsDiagnosisTests
{
    private static readonly EntityId Facility = new(12, 3);
    private static readonly EntityId Source = new(14, 2);
    private static readonly EntityId Truck = new(18, 7);

    [Fact]
    public void CopiesSelectedPolicyDemandWithoutCountingIncomingTwice()
    {
        var policy = new PlayerStockPolicyActionReadModel(
            new(100, 5), ResourceIds.Steel, "Steel", 80, 50, 200, 300,
            LogisticsStockPriority.Normal, true, PlayerDistributionActionState.Waiting,
            LogisticsTransportRequestFailureReason.NoRoute,
            LogisticsBottleneckReason.SaturatedLinkOrHub, Truck)
        {
            ReservedQuantity = 20,
            RequestedQuantity = 90,
            AnnouncedQuantity = 40,
            Source = Source
        };
        var order = new PlayerLogisticsOrderReadModel(new(17), ResourceIds.Steel, "Steel",
            LogisticsTransportRequestState.InTransit, LogisticsTransportRequestFailureReason.None,
            LogisticsBottleneckReason.None, 90, 10, Truck, Source, Facility, "SOURCE", "FACILITY");
        var logistics = new PlayerLogisticsActionReadModel(
            Facility, [policy], null, 500, 80, orders: [order]);
        var captured = Snapshot(logistics);
        var diagnosis = Assert.IsType<OperationsDiagnosisSnapshot>(OperationsDiagnosisSnapshot.Resolve(captured));
        Assert.Equal(captured.SessionId, diagnosis.Session);
        Assert.Equal(captured.Tick, diagnosis.Tick);
        Assert.Equal(new PlayerId(1), diagnosis.Player);

        var stock = Assert.Single(diagnosis.Rows, row => row.Subject == OperationsDiagnosisSubject.StockPolicy);
        Assert.Equal(120, stock.Demand);
        Assert.Equal(80, stock.LocalQuantity);
        Assert.Equal(60, stock.FreeQuantity);
        Assert.Equal(20, stock.ReservedQuantity);
        Assert.Equal(90, stock.OutstandingRequestQuantity);
        Assert.Equal(40, stock.ConfirmedIncomingQuantity);
        Assert.Null(stock.LoadedCargoQuantity);
        Assert.Null(stock.SourceFreeQuantity);
        Assert.Equal(OperationsDiagnosisReason.NoRoute, stock.PrimaryReason);
        Assert.Equal(OperationsDiagnosisReason.Capacity, stock.SecondaryReason);
        var request = Assert.Single(diagnosis.Rows, row => row.Subject == OperationsDiagnosisSubject.TransportRequest);
        Assert.Equal(90, request.OutstandingRequestQuantity);
        Assert.Equal(10, request.LoadedCargoQuantity);
        Assert.Null(request.ConfirmedIncomingQuantity);
        Assert.Equal(17UL, request.RequestId.Value);
        Assert.Equal(new[] { new OperationsDiagnosisLink(new(17), Source, Facility, ResourceIds.Steel) },
            diagnosis.TraceUpstream(Facility));
        Assert.Contains("CONFIRMED 40", diagnosis.ToDiagnosticLines()[1]);
    }

    [Fact]
    public void UnconfiguredPolicyDoesNotBecomeDemandOrATransportBlocker()
    {
        var draft = new PlayerStockPolicyActionReadModel(default, ResourceIds.Fuel, "Fuel",
            0, 50, 50, 100, LogisticsStockPriority.Normal, true, default,
            LogisticsTransportRequestFailureReason.NoTruckAvailable, default, default);
        var logistics = new PlayerLogisticsActionReadModel(Facility, [draft], null);
        Assert.Null(Snapshot(logistics).Diagnosis);
    }

    [Fact]
    public void ProductionWithoutPowerAndMissingInputUseObservedBlockReasons()
    {
        var noPower = new PlayerProductionFacilityActionReadModel(Facility, default, default,
            ProductionStatus.NoPower, ProductionBlockReason.NoPower, 0, [], []);
        var noInput = new PlayerProductionFacilityActionReadModel(
            Facility, default, default, ProductionStatus.NoInput, ProductionBlockReason.NoInput, 0, [], []);
        Assert.Equal(OperationsDiagnosisReason.NoPower,
            Assert.Single(Snapshot(production: noPower).Diagnosis!.Rows).PrimaryReason);
        Assert.Equal(OperationsDiagnosisReason.NoInput,
            Assert.Single(Snapshot(production: noInput).Diagnosis!.Rows).PrimaryReason);
        Assert.Equal("NO POWER", OperationsDiagnosisSnapshot.Label(OperationsDiagnosisReason.NoPower));
    }

    [Fact]
    public void LoadedTruckIsNotAssumedDeliveredAndDockQueueIsObservedOnly()
    {
        var cargo = new PlayerCargoStatusReadModel(Truck, CargoTransportLifecycleState.Waiting,
            CargoTransportWaitReason.DestinationCapacity, CargoTransportFailureReason.None, 40, 100)
        {
            Source = Source,
            Destination = Facility,
            Resource = ResourceIds.Steel,
            RequestedQuantity = 50
        };
        var service = PlayerWorkSummary.None with
        {
            VehicleQueue = new PlayerVehicleQueueSummary(Facility, 1,
                VehicleQueueService.CargoDelivery, 3, 4, VehicleDockStatus.Approaching,
                VehicleQueueWaitReason.Full, VehicleDockBlockReason.None, new SimulationTick(9))
        };
        var logistics = new PlayerLogisticsActionReadModel(Truck, [], cargo, service: service);
        var row = Assert.Single(Snapshot(logistics).Diagnosis!.Rows);
        Assert.Equal(40, row.LoadedCargoQuantity);
        Assert.Equal(50, row.OutstandingRequestQuantity);
        Assert.Equal(OperationsDiagnosisReason.WaitingForBay, row.PrimaryReason);
        Assert.Equal(OperationsDiagnosisReason.DestinationFull, row.SecondaryReason);
        Assert.Equal(3, row.DockQueuePosition);
        Assert.Empty(Snapshot(new PlayerLogisticsActionReadModel(Truck, [], cargo)).Diagnosis!.Links);
    }

    [Fact]
    public void StaleSessionTickTerminalAndMissingOwnerObservationsAreRejected()
    {
        Assert.Null(Snapshot(production: Production(), session: 4, actionSession: 3).Diagnosis);
        Assert.Null(Snapshot(production: Production(), tick: 10, actionTick: 9).Diagnosis);
        Assert.Null(Snapshot(production: Production(), tick: 10, experienceTick: 9).Diagnosis);
        Assert.Null(Snapshot(production: Production(), terminal: true).Diagnosis);
        Assert.Null(Snapshot(production: Production(), player: 0).Diagnosis);
        var valid = Snapshot(production: Production());
        Assert.NotNull(OperationsDiagnosisSnapshot.Resolve(valid));
        Assert.Null(OperationsDiagnosisSnapshot.Resolve(null));
    }

    [Fact]
    public void CopiesAreImmutableAndTraversalsBoundedEvenWithCycles()
    {
        var input = Enumerable.Range(1, 1000)
            .Select(i => new OperationsDiagnosisRow(OperationsDiagnosisSubject.StockPolicy,
                new((uint)i, 1), ResourceIds.Steel, default, "PENDING",
                20, 5, 5, 0, 15, 0, null, null, default, default, default,
                null, null, OperationsDiagnosisReason.None)).ToArray();
        var chain = new[]
        {
            new OperationsDiagnosisLink(new(1), new(1, 1), new(2, 1), ResourceIds.Steel),
            new OperationsDiagnosisLink(new(2), new(2, 1), new(3, 1), ResourceIds.Steel),
            new OperationsDiagnosisLink(new(3), new(3, 1), new(1, 1), ResourceIds.Steel)
        };
        var data = new OperationsDiagnosisSnapshot(new(3), new(9), new(1), input, chain);
        input[0] = default;
        chain[0] = default;
        Assert.Equal(OperationsDiagnosisSnapshot.MaximumRows, data.Rows.Count);
        Assert.Equal(1000, data.TotalRows);
        Assert.Equal(new EntityId(1, 1), data.Rows[0].Entity);
        Assert.Equal(2, data.TraceUpstream(new(3, 1)).Count);
        Assert.Throws<NotSupportedException>(() => ((IList<OperationsDiagnosisRow>)data.Rows)[0] = default);
        Assert.Throws<NotSupportedException>(() => ((IList<OperationsDiagnosisLink>)data.Links)[0] = default);
    }

    private static PlayerProductionFacilityActionReadModel Production() =>
        new(Facility, default, default, ProductionStatus.NoPower, ProductionBlockReason.NoPower, 0, [], []);

    private static PresentationSnapshot Snapshot(PlayerLogisticsActionReadModel? logistics = null,
        PlayerProductionFacilityActionReadModel? production = null, ulong session = 3, ulong actionSession = 3,
        ulong tick = 9, ulong actionTick = 9, ulong experienceTick = 9, ulong player = 1, bool terminal = false)
    {
        var experience = default(PlayerExperienceSnapshot) with
        {
            Player = new(player),
            Tick = new(experienceTick),
            MatchStatus = terminal ? PlayerMatchStatus.Victory : default
        };
        var actions = new PlayerActionSnapshot(new(actionSession), new(actionTick), [], 0, production, null, logistics);
        return new PresentationSnapshot(new(tick), TimeSpan.FromMilliseconds(50), 0, [],
            sessionId: new(session), playerExperience: experience, playerActions: actions);
    }
}
