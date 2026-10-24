using ForgeLine.Core;
using Xunit;

namespace ForgeLine.Presentation.Tests;

public sealed class OperationsActionGuidanceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(17.0)]
    public void SourceFreeObservationPreservesUnknownZeroAndMeasuredStock(double? free)
    {
        EntityId selected = new(12, 3), source = new(14, 2);
        var observed = Row(selected, OperationsDiagnosisReason.NoInput) with
        { Source = source, Resource = ResourceIds.Electronics, Inventory = new(9), SourceFreeQuantity = free };
        var diagnosis = new OperationsDiagnosisSnapshot(new(1), new(7), new(1), [observed]);
        Assert.Equal(free, OperationsActionGuidance.SourceForEntity(diagnosis, selected)!.Value.SourceFreeQuantity);
        var ambiguous = new OperationsDiagnosisSnapshot(new(1), new(7), new(1),
            [observed, observed with { Source = new(15, 3), Inventory = new(10), SourceFreeQuantity = 0 }]);
        Assert.Null(OperationsActionGuidance.SourceForEntity(ambiguous, selected));
    }

    [Fact]
    public void ActualMissingIngredientNamesOutrankTheSameGenericProductionBlock()
    {
        EntityId entity = new(12, 3);
        var generic = Row(entity, OperationsDiagnosisReason.NoInput);
        var input = generic with { Subject = OperationsDiagnosisSubject.ProductionInput, Resource = ResourceIds.Silicates };
        var diagnosis = new OperationsDiagnosisSnapshot(new(1), new(7), new(1), [generic, input]);
        Assert.Equal(ResourceIds.Silicates, OperationsActionGuidance.ForEntity(diagnosis, entity)!.Value.Resource);
    }

    [Fact]
    public void OnlyExactOwnedDiagnosisIdentityCanSupplyAdvice()
    {
        var selected = new EntityId(12, 3);
        var diagnosis = new OperationsDiagnosisSnapshot(new(1), new(7), new(1),
        [
            Row(new(12, 2), OperationsDiagnosisReason.NoPower),
            Row(selected, OperationsDiagnosisReason.None, OperationsDiagnosisSubject.TransportRequest, "InTransit"),
            Row(new(13, 3), OperationsDiagnosisReason.NoRoute)
        ]);
        Assert.Null(OperationsActionGuidance.ForEntity(diagnosis, new(12, 4)));
        Assert.Null(OperationsActionGuidance.ForEntity(diagnosis, default));
        Assert.Null(OperationsActionGuidance.ForEntity(null, selected));
        var advice = Assert.IsType<OperationsDiagnosisRow>(OperationsActionGuidance.ForEntity(diagnosis, selected));
        Assert.Equal(OperationsDiagnosisReason.None, advice.PrimaryReason);
        Assert.Equal("InTransit", advice.Status);
        Assert.Equal(string.Empty, OperationsActionGuidance.NextAction(advice.PrimaryReason));
    }

    [Fact]
    public void KnownBlockerOutranksWaitingAndUnblockedTransportWithoutInventingDelivery()
    {
        var selected = new EntityId(12, 3);
        var diagnosis = new OperationsDiagnosisSnapshot(new(1), new(7), new(1),
        [
            Row(selected, OperationsDiagnosisReason.None, OperationsDiagnosisSubject.TransportRequest, "InTransit"),
            Row(selected, OperationsDiagnosisReason.WaitingForBay, OperationsDiagnosisSubject.CargoTransport, "Waiting"),
            Row(selected, OperationsDiagnosisReason.NoInput, OperationsDiagnosisSubject.Production, "NoInput")
        ]);
        var advice = Assert.IsType<OperationsDiagnosisRow>(OperationsActionGuidance.ForEntity(diagnosis, selected));
        Assert.Equal(OperationsDiagnosisSubject.Production, advice.Subject);
        Assert.Equal(OperationsDiagnosisReason.NoInput, advice.PrimaryReason);
        Assert.Equal("OPEN LOGISTICS", OperationsActionGuidance.NextAction(advice.PrimaryReason));
    }

    [Fact]
    public void ProductionFailureDoesNotEraseAnIndependentTransportLifecycle()
    {
        var selected = new EntityId(12, 3);
        var diagnosis = new OperationsDiagnosisSnapshot(new(1), new(7), new(1),
        [
            Row(selected, OperationsDiagnosisReason.NoInput, OperationsDiagnosisSubject.Production, "NoInput"),
            Row(selected, OperationsDiagnosisReason.None, OperationsDiagnosisSubject.StockPolicy, "Waiting"),
            Row(selected, OperationsDiagnosisReason.None, OperationsDiagnosisSubject.TransportRequest, "InTransit")
        ]);
        Assert.Equal(OperationsDiagnosisSubject.Production,
            OperationsActionGuidance.ForEntity(diagnosis, selected)!.Value.Subject);
        var transport = Assert.IsType<OperationsDiagnosisRow>(
            OperationsActionGuidance.TransportForEntity(diagnosis, selected));
        Assert.Equal(OperationsDiagnosisSubject.TransportRequest, transport.Subject);
        Assert.Equal("InTransit", transport.Status);
        Assert.Null(OperationsActionGuidance.TransportForEntity(diagnosis, new(12, 4)));
    }

    [Theory]
    [InlineData(OperationsDiagnosisReason.NoPower, "CHECK POWER")]
    [InlineData(OperationsDiagnosisReason.NoSourceStock, "CHECK SOURCE STOCK")]
    [InlineData(OperationsDiagnosisReason.NoRoute, "CHECK CONNECTIONS")]
    [InlineData(OperationsDiagnosisReason.NoTruck, "CHECK CARGO FLEET")]
    [InlineData(OperationsDiagnosisReason.Capacity, "CHECK LINK OR HUB")]
    [InlineData(OperationsDiagnosisReason.WaitingForBay, "INSPECT LOADING QUEUE")]
    [InlineData(OperationsDiagnosisReason.ProductionPaused, "OPEN PRODUCTION")]
    [InlineData(OperationsDiagnosisReason.OutputFull, "CHECK OUTPUT COLLECTION")]
    [InlineData(OperationsDiagnosisReason.NoDriveFuel, "OPEN SUPPLY")]
    [InlineData(OperationsDiagnosisReason.DestinationFull, "CHECK DESTINATION STOCK")]
    public void KnownReasonsUseInspectionAdviceNotDispatch(OperationsDiagnosisReason reason, string text)
    {
        Assert.Equal(text, OperationsActionGuidance.NextAction(reason));
        Assert.NotEqual(string.Empty, OperationsDiagnosisSnapshot.Label(reason));
    }

    [Fact]
    public void UnknownCauseDoesNotSuggestAnUnsupportedFix()
    {
        Assert.Equal(string.Empty, OperationsActionGuidance.NextAction(OperationsDiagnosisReason.Unknown));
        Assert.Equal(string.Empty, OperationsActionGuidance.NextAction(OperationsDiagnosisReason.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedDiagnosisLookupDoesNotAllocateOnTheRenderPath(bool transportOnly)
    {
        var selected = new EntityId(12, 3);
        var diagnosis = new OperationsDiagnosisSnapshot(new(1), new(7), new(1),
        [
            Row(selected, OperationsDiagnosisReason.NoInput),
            Row(selected, OperationsDiagnosisReason.None, OperationsDiagnosisSubject.TransportRequest, "InTransit")
        ]);
        OperationsDiagnosisRow? Lookup() => transportOnly
            ? OperationsActionGuidance.TransportForEntity(diagnosis, selected)
            : OperationsActionGuidance.ForEntity(diagnosis, selected);
        Assert.Equal(transportOnly ? OperationsDiagnosisSubject.TransportRequest : OperationsDiagnosisSubject.Production,
            Lookup()!.Value.Subject);
        for (int i = 0; i < 256; i++) _ = Lookup();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 512; i++) _ = Lookup();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static OperationsDiagnosisRow Row(EntityId entity, OperationsDiagnosisReason reason,
        OperationsDiagnosisSubject subject = OperationsDiagnosisSubject.Production, string status = "Blocked") =>
        new(subject, entity, default, default, status, null, null, null, null, null, null, null, null,
            default, default, default, null, null, reason);
}
