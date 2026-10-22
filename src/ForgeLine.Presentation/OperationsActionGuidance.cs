using ForgeLine.Core;

namespace ForgeLine.Presentation;

/// <summary>
/// Selects a single copied, owner-filtered diagnosis for an existing Operations detail.
/// No world lookup, transport prediction or command dispatch is performed here.
/// </summary>
internal static class OperationsActionGuidance
{
    public static OperationsDiagnosisRow? ForEntity(OperationsDiagnosisSnapshot? diagnosis, EntityId entity)
    {
        if (diagnosis is null || !entity.IsValid) return null;

        OperationsDiagnosisRow? selected = null;
        int bestPriority = int.MaxValue;
        foreach (var row in diagnosis.Rows)
        {
            if (row.Entity != entity) continue;
            int priority = Priority(row);
            if (priority >= bestPriority) continue;
            selected = row;
            bestPriority = priority;
        }
        return selected;
    }

    /// <summary>
    /// A separate observed transport lifecycle may accompany a production blocker.
    /// Prefer request status, then policy status, then physical truck status; never
    /// combine their non-additive quantity fields.
    /// </summary>
    public static OperationsDiagnosisRow? TransportForEntity(OperationsDiagnosisSnapshot? diagnosis, EntityId entity)
    {
        if (diagnosis is null || !entity.IsValid) return null;

        OperationsDiagnosisRow? selected = null;
        int bestPriority = int.MaxValue;
        foreach (var row in diagnosis.Rows)
        {
            if (row.Entity != entity) continue;
            int priority = row.Subject switch
            {
                OperationsDiagnosisSubject.TransportRequest => 0,
                OperationsDiagnosisSubject.StockPolicy => 1,
                OperationsDiagnosisSubject.CargoTransport => 2,
                _ => int.MaxValue
            };
            if (priority >= bestPriority) continue;
            selected = row;
            bestPriority = priority;
        }
        return selected;
    }

    /// <summary>The advice names an existing inspection path, never an implicit gameplay action.</summary>
    public static string NextAction(OperationsDiagnosisReason reason) => reason switch
    {
        OperationsDiagnosisReason.NoPower => "CHECK POWER",
        OperationsDiagnosisReason.NoInput => "OPEN LOGISTICS",
        OperationsDiagnosisReason.NoSourceStock => "CHECK SOURCE STOCK",
        OperationsDiagnosisReason.NoRoute => "CHECK CONNECTIONS",
        OperationsDiagnosisReason.NoTruck => "CHECK CARGO FLEET",
        OperationsDiagnosisReason.Capacity => "CHECK LINK OR HUB",
        OperationsDiagnosisReason.WaitingForBay => "INSPECT LOADING QUEUE",
        OperationsDiagnosisReason.ProductionPaused => "OPEN PRODUCTION",
        OperationsDiagnosisReason.OutputFull => "CHECK OUTPUT COLLECTION",
        OperationsDiagnosisReason.NoDriveFuel => "OPEN SUPPLY",
        OperationsDiagnosisReason.DestinationFull => "CHECK DESTINATION STOCK",
        _ => string.Empty
    };

    private static int Priority(in OperationsDiagnosisRow row)
    {
        // Known blockers outrank opaque failures and running/pending observations.
        if (row.PrimaryReason is not (OperationsDiagnosisReason.None or OperationsDiagnosisReason.Unknown))
            return (int)row.PrimaryReason;
        if (row.PrimaryReason == OperationsDiagnosisReason.Unknown) return 90;
        return row.Subject switch
        {
            OperationsDiagnosisSubject.TransportRequest => 100,
            OperationsDiagnosisSubject.StockPolicy => 101,
            OperationsDiagnosisSubject.CargoTransport => 102,
            OperationsDiagnosisSubject.Production => 103,
            _ => 104
        };
    }
}
