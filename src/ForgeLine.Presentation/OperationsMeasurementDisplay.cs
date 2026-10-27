using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal static class OperationsMeasurementDisplay
{
    internal static OperationsWindowMeasurement? Find(OperationsDiagnosisSnapshot? diagnosis, EntityId entity, int page)
    {
        if (diagnosis?.Observation is not { } observation || observation.Session != diagnosis.Session ||
            observation.Tick != diagnosis.Tick || observation.Player != diagnosis.Player) return null;
        int count = 0;
        for (int i = 0; i < observation.Measurements.Count; i++)
            if (observation.Measurements[i].Entity == entity) count++;
        if (count == 0) return null;
        int wanted = Math.Max(0, page) % count;
        for (int i = 0; i < observation.Measurements.Count; i++)
        {
            var flow = observation.Measurements[i];
            if (flow.Entity == entity && wanted-- == 0) return flow;
        }
        return null;
    }
    internal static OperationsDiagnosisRow? Current(OperationsDiagnosisSnapshot? diagnosis, in OperationsWindowMeasurement flow)
    {
        OperationsDiagnosisRow? result = null;
        if (diagnosis is null) return null;
        for (int i = 0; i < diagnosis.Rows.Count; i++)
        {
            var row = diagnosis.Rows[i];
            if (row.Entity == flow.Entity && row.Resource == flow.Resource && row.Subject == OperationsDiagnosisSubject.StockPolicy)
            { if (result.HasValue) return null; result = row; }
        }
        return !diagnosis.DistributionObserved && result is { } current
            ? current with { OutstandingRequestQuantity = null, ConfirmedIncomingQuantity = null } : result;
    }
    internal static double? Loaded(OperationsDiagnosisSnapshot? diagnosis, in OperationsWindowMeasurement flow)
    {
        double? quantity = null;
        bool found = false;
        if (diagnosis is null) return null;
        for (int i = 0; i < diagnosis.Rows.Count; i++)
        {
            var row = diagnosis.Rows[i];
            if (row.Entity == flow.Entity && row.Resource == flow.Resource && row.Subject == OperationsDiagnosisSubject.TransportRequest)
            { if (found) return null; found = true; quantity = row.LoadedCargoQuantity; }
        }
        return quantity;
    }
}
