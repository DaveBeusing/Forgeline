using ForgeLine.Core;
using ForgeLine.Economy;

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
        for (int i = 0; i < diagnosis.Rows.Count; i++)
        {
            var row = diagnosis.Rows[i];
            if (row.Entity != entity) continue;
            int priority = Priority(row);
            if (priority > bestPriority || priority == bestPriority &&
                (row.Subject != OperationsDiagnosisSubject.ProductionInput || selected?.Subject == OperationsDiagnosisSubject.ProductionInput)) continue;
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
        for (int i = 0; i < diagnosis.Rows.Count; i++)
        {
            var row = diagnosis.Rows[i];
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
        OperationsDiagnosisReason.DestroyedRoute or OperationsDiagnosisReason.DestroyedSupport => "INSPECT REPAIR",
        OperationsDiagnosisReason.DisconnectedPort or OperationsDiagnosisReason.IncompatibleResource => "CHECK CONNECTIONS",
        OperationsDiagnosisReason.ReservedStock or OperationsDiagnosisReason.RetainedStock => "CHECK SOURCE STOCK",
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
        OperationsDiagnosisReason.DestinationTargetCovered => "CHECK STOCK POLICY",
        _ => string.Empty
    };

    internal static OperationsDiagnosisRow? SourceForEntity(OperationsDiagnosisSnapshot? diagnosis, EntityId entity)
    {
        OperationsDiagnosisRow? observed = null;
        if (diagnosis is null) return null;
        ResourceId wanted = ForEntity(diagnosis, entity)?.Resource ?? default;
        for (int index = 0; index < diagnosis.Rows.Count; index++)
        {
            var row = diagnosis.Rows[index];
            if (row.Entity != entity || !row.Source.IsValid || wanted.IsSpecified && row.Resource != wanted) continue;
            if (observed is { } prior &&
                (prior.Source != row.Source || prior.Resource != row.Resource || prior.Inventory != row.Inventory)) return null;
            if (row.Inventory.IsSpecified) observed = row;
        }
        return observed;
    }

    internal static EntityId NextChainEntity(OperationsDiagnosisSnapshot? diagnosis, EntityId entity, EntityId after = default)
    {
        if (diagnosis is null || !diagnosis.Root.IsValid) return default;
        var issue = ForEntity(diagnosis, entity);
        ResourceId wanted = issue?.Resource ?? default;
        if (issue is { } row && row.CauseEntity.IsValid && row.CauseEntity != entity && row.CauseEntity != after) return row.CauseEntity;
        EntityId first = default, next = default;
        for (int index = 0; index < diagnosis.Links.Count; index++)
        {
            var link = diagnosis.Links[index];
            if (link.Destination != entity || link.Source == entity ||
                wanted.IsSpecified && link.Resource.IsSpecified && link.Resource != wanted) continue;
            if (!first.IsValid || link.Source.CompareTo(first) < 0) first = link.Source;
            if (link.Source.CompareTo(after) > 0 && (!next.IsValid || link.Source.CompareTo(next) < 0)) next = link.Source;
        }
        return next.IsValid ? next : first;
    }

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
