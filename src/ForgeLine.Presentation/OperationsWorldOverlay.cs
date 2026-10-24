using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;

namespace ForgeLine.Presentation;

internal readonly record struct OperationsWorldMarker(EntityId Entity, Vector2 Position,
    OperationsFacility? Facility, OperationsDiagnosisRow? Diagnosis, int Priority);

internal readonly record struct OperationsWorldLink(EntityId Source, EntityId Destination,
    Vector2 Start, Vector2 End, bool Enabled, bool Request, bool Loaded, bool Conveyor = false);

/// <summary>Bounded render-owner projection of copied facts; no live world queries or command authority.</summary>
internal sealed class OperationsWorldOverlay
{
    internal const int MaximumMarkers = 64;
    internal const int MaximumLinks = 64;
    internal const int MaximumSelection = 1024;
    // Facilities + diagnosis subjects + both endpoints of all bounded route/request lists.
    private const int MaximumPositions = OperationsSnapshot.MaximumFacilities + OperationsDiagnosisSnapshot.MaximumRows +
        2 * (OperationsSnapshot.MaximumRoutes + OperationsDiagnosisSnapshot.MaximumLinks);
    private readonly OperationsWorldMarker[] _markers = new OperationsWorldMarker[MaximumMarkers];
    private readonly OperationsWorldLink[] _links = new OperationsWorldLink[MaximumLinks];
    private readonly HashSet<EntityId> _chain = new(OperationsDiagnosisSnapshot.MaximumRows);
    private readonly HashSet<EntityId> _selection = new(MaximumSelection);
    private readonly HashSet<EntityId> _wanted = new(MaximumPositions);
    private readonly Dictionary<EntityId, Vector2> _positions = new(MaximumPositions);
    private int _markerCount, _linkCount;
    private EntityId _focus, _hover;

    internal ReadOnlySpan<OperationsWorldMarker> Markers => _markers.AsSpan(0, _markerCount);
    internal ReadOnlySpan<OperationsWorldLink> Links => _links.AsSpan(0, _linkCount);
    internal int OmittedMarkers { get; private set; }

    internal static bool IsOwnedVisible(in RenderInstance instance, PlayerId player) =>
        player.IsSpecified && instance.Entity.IsValid && instance.Selectable.Owner == player &&
        (instance.Visibility & RenderVisibilityMask.World) != 0;

    internal static bool CanFocus(PresentationSnapshot? snapshot, EntityId entity)
    {
        if (!entity.IsValid || snapshot is not { PlayerExperience: { IsMatchComplete: false } experience } ||
            !snapshot.SessionId.IsSpecified || experience.Tick != snapshot.Tick) return false;
        foreach (ref readonly var instance in snapshot.Instances)
            if (instance.Entity == entity) return IsOwnedVisible(instance, experience.Player);
        return false;
    }

    internal void Build(in GameplayHudRenderContext context)
    {
        // Release references to the previous tick as well as resetting the emitted counts.
        Array.Clear(_markers, 0, _markerCount);
        _markerCount = _linkCount = OmittedMarkers = 0;
        _selection.Clear(); _chain.Clear(); _wanted.Clear(); _positions.Clear();
        _focus = _hover = default;
        var snapshot = context.Snapshot;
        var data = OperationsSnapshot.Resolve(snapshot);
        if (data is null || !data.Player.IsSpecified || context.PreAlphaUx.Mode != PreAlphaUxMode.None ||
            context.PreAlphaUx.Placement.Visible || context.ActionPanel.SelectionPending ||
            context.InformationLayer.IsDragSelecting || context.Layout.SafeArea.IsEmpty ||
            context.Graphics.Width != context.Layout.ViewportWidth ||
            context.Graphics.Height != context.Layout.ViewportHeight) return;

        bool operations = context.Operations.Open && !context.Operations.Suppressed &&
            !context.Operations.Notifications && context.Operations.Session == snapshot.SessionId;
        _focus = operations ? context.Operations.Selected : default;
        var currentSelection = context.InformationLayer.SelectedEntities;
        bool selectionMatches = snapshot.SelectedOrders is { } orders && orders.Tick == snapshot.Tick &&
            orders.MatchesSelection(snapshot.SessionId, currentSelection);
        if (selectionMatches && currentSelection is not null)
            for (int i = 0; i < Math.Min(currentSelection.Length, MaximumSelection); i++)
                _selection.Add(currentSelection[i]);
        var hover = context.HoverTooltip;
        if (hover.Ready && hover.DockControl == 0 && !hover.SelectionStatus &&
            hover.SessionId == snapshot.SessionId && hover.Scale == context.Layout.Scale &&
            hover.ViewportWidth == context.Graphics.Width && hover.ViewportHeight == context.Graphics.Height &&
            snapshot.Hover is { } captured && captured.SessionId == snapshot.SessionId &&
            captured.Tick == snapshot.Tick && captured.Entity == hover.Entity && captured.OwnedDetails.Count == 1)
            _hover = captured.Entity;
        var diagnosis = selectionMatches ? OperationsDiagnosisSnapshot.Resolve(snapshot) : null;

        if (diagnosis is { Root.IsValid: true })
            for (int index = 0; index < diagnosis.Rows.Count; index++)
            {
                var row = diagnosis.Rows[index];
                _chain.Add(row.Entity);
                if (row.CauseEntity.IsValid) _chain.Add(row.CauseEntity);
            }

        for (int i = 0; i < data.Facilities.Count; i++)
        {
            var facility = data.Facilities[i];
            if (Relevant(facility.Entity) || operations && MatchesFilter(facility, context.Operations.Filter))
                _wanted.Add(facility.Entity);
        }
        if (diagnosis is not null)
            for (int i = 0; i < diagnosis.Rows.Count; i++)
            {
                var row = diagnosis.Rows[i];
                if (Relevant(row.Entity)) _wanted.Add(row.Entity);
            }
        for (int i = 0; i < data.Routes.Count; i++)
        {
            var route = data.Routes[i];
            if (Relevant(route.Source) || Relevant(route.Destination)) WantEndpoints(route.Source, route.Destination);
        }
        if (diagnosis is not null)
            for (int i = 0; i < diagnosis.Links.Count; i++)
            {
                var link = diagnosis.Links[i];
                if (Relevant(link.Source) || Relevant(link.Destination)) WantEndpoints(link.Source, link.Destination);
            }
        if (_wanted.Count == 0) return;

        Matrix4x4 projection = context.Camera.GetMatrices(context.Graphics.Width, context.Graphics.Height).ViewProjection;
        foreach (ref readonly var instance in snapshot.Instances)
        {
            if (!_wanted.Contains(instance.Entity) || !IsOwnedVisible(instance, data.Player)) continue;
            var position = instance.Transform.Position;
            if (!Finite(position) || !Finite(instance.Transform.Scale)) continue;
            position.Y += MathF.Max(3, MathF.Abs(instance.Transform.Scale.Y) * .65f);
            Vector4 clip = Vector4.Transform(new Vector4(position, 1), projection);
            if (!float.IsFinite(clip.W) || clip.W <= 0) continue;
            Vector3 ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
            if (!Finite(ndc) || ndc.X is < -1 or > 1 || ndc.Y is < -1 or > 1 || ndc.Z is < 0 or > 1) continue;
            _positions[instance.Entity] = new((ndc.X + 1) * .5f * context.Graphics.Width,
                (1 - ndc.Y) * .5f * context.Graphics.Height);
        }
        for (int i = 0; i < data.Facilities.Count; i++)
        {
            var facility = data.Facilities[i];
            if ((Relevant(facility.Entity) || operations && MatchesFilter(facility, context.Operations.Filter)) &&
                _positions.TryGetValue(facility.Entity, out var position))
                AddMarker(new(facility.Entity, position, facility,
                    OperationsActionGuidance.ForEntity(diagnosis, facility.Entity), Priority(facility.Entity, facility.Cause.Length > 0)));
        }
        if (diagnosis is not null)
            for (int i = 0; i < diagnosis.Rows.Count; i++)
            {
                var row = diagnosis.Rows[i];
                if (Relevant(row.Entity) && _positions.TryGetValue(row.Entity, out var position))
                    AddMarker(new(row.Entity, position, null, OperationsActionGuidance.ForEntity(diagnosis, row.Entity),
                        Priority(row.Entity, row.PrimaryReason != OperationsDiagnosisReason.None)));
            }

        // A reported request is still not a completed delivery. Prefer it to an overlapping topology link.
        if (diagnosis is not null)
            for (int i = 0; i < diagnosis.Links.Count; i++)
            {
                var link = diagnosis.Links[i];
                if ((link.Kind == OperationsDiagnosisLinkKind.TransportRequest && !link.RequestId.IsSpecified) ||
                    !(Relevant(link.Source) || Relevant(link.Destination))) continue;
                bool loaded = false;
                for (int rowIndex = 0; rowIndex < diagnosis.Rows.Count; rowIndex++)
                {
                    var row = diagnosis.Rows[rowIndex];
                    if (row.RequestId == link.RequestId && row.Subject == OperationsDiagnosisSubject.TransportRequest)
                    {
                        // The player extractor has already checked the live truck/order generation,
                        // endpoints and resource and subtracts delivered progress from loaded progress.
                        loaded = row.AssignedTruck.IsValid && row.LoadedCargoQuantity is > 0 &&
                            row.Status is (nameof(LogisticsTransportRequestState.Assigned) or nameof(LogisticsTransportRequestState.InTransit));
                        break;
                    }
                }
                AddLink(link.Source, link.Destination, true,
                    link.Kind == OperationsDiagnosisLinkKind.TransportRequest, loaded,
                    link.Kind == OperationsDiagnosisLinkKind.ConveyorConnection);
            }
        for (int i = 0; i < data.Routes.Count; i++)
        {
            var route = data.Routes[i];
            if (Relevant(route.Source) || Relevant(route.Destination))
                AddLink(route.Source, route.Destination, route.Enabled, false, false);
        }
    }

    private void WantEndpoints(EntityId source, EntityId destination)
    {
        if (source.IsValid) _wanted.Add(source);
        if (destination.IsValid) _wanted.Add(destination);
    }

    private bool Relevant(EntityId entity) => entity.IsValid &&
        (entity == _focus || entity == _hover || _selection.Contains(entity) || _chain.Contains(entity));

    private int Priority(EntityId entity, bool blocked) => entity == _focus ? 0 : entity == _hover ? 1 :
        _selection.Contains(entity) ? 2 : _chain.Contains(entity) ? 3 : blocked ? 4 : 5;

    private static bool MatchesFilter(in OperationsFacility row, OperationsCategory filter) =>
        filter == OperationsCategory.All || row.Category == filter ||
        filter == OperationsCategory.Blocked && row.Cause.Length > 0 ||
        filter == OperationsCategory.Power && (row.PowerDemand.HasValue || row.GenerationCapacity.HasValue);

    private void AddMarker(in OperationsWorldMarker marker)
    {
        for (int i = 0; i < _markerCount; i++)
            if (_markers[i].Entity == marker.Entity) return;
        int insert = 0;
        while (insert < _markerCount && Compare(_markers[insert], marker) <= 0) insert++;
        if (_markerCount == MaximumMarkers)
        {
            OmittedMarkers++;
            if (insert == MaximumMarkers) return;
        }
        else _markerCount++;
        for (int i = _markerCount - 1; i > insert; i--) _markers[i] = _markers[i - 1];
        _markers[insert] = marker;
    }

    private static int Compare(in OperationsWorldMarker a, in OperationsWorldMarker b)
    {
        int priority = a.Priority.CompareTo(b.Priority);
        return priority != 0 ? priority : a.Entity.CompareTo(b.Entity);
    }

    private void AddLink(EntityId source, EntityId destination, bool enabled, bool request, bool loaded, bool conveyor = false)
    {
        if (source == destination || !_positions.TryGetValue(source, out var start) ||
            !_positions.TryGetValue(destination, out var end)) return;
        for (int i = 0; i < _linkCount; i++)
            if (_links[i].Source == source && _links[i].Destination == destination) return;
        var link = new OperationsWorldLink(source, destination, start, end, enabled, request, loaded, conveyor);
        int insert = 0;
        while (insert < _linkCount && Compare(_links[insert], link) <= 0) insert++;
        if (insert == MaximumLinks) return;
        if (_linkCount < MaximumLinks) _linkCount++;
        for (int i = _linkCount - 1; i > insert; i--) _links[i] = _links[i - 1];
        _links[insert] = link;
    }

    private static int Compare(in OperationsWorldLink a, in OperationsWorldLink b)
    {
        int request = b.Request.CompareTo(a.Request);
        if (request != 0) return request;
        int source = a.Source.CompareTo(b.Source);
        return source != 0 ? source : a.Destination.CompareTo(b.Destination);
    }

    private static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
}
