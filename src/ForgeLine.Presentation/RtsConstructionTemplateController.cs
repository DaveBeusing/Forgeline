using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

/// <summary>Session-local, bounded orchestration of ordinary paid commands.</summary>
public sealed class RtsConstructionTemplateController
{
    private SimulationSessionId _session;
    private ConstructionTemplatePreviewRequest? _request;
    private ulong _nextId;
    private Vector3 _anchor;
    private BuildingOrientation _orientation;
    private int _template, _step, _width, _height;
    private float _scale;
    private bool _left, _applying, _finished, _awaiting;
    private EntityId _first, _second;
    private PlayerCommandCorrelationId _correlation;
    private BuildingPlacementRequest? _building;
    private ConveyorBuildRequest? _conveyor;
    private readonly string[] _results = ["UNSENT", "UNSENT", "UNSENT"];
    public bool IsActive { get; private set; }
    public bool AwaitingResult => _awaiting;
    public ConstructionTemplatePreview? Preview { get; private set; }
    public bool Current { get; private set; }
    public Vector3? GridCenter => IsActive ? _anchor : null;
    public string Hint => IsActive ? PaidConstructionTemplates.Name(_template) +
        " / PARTIAL ACCEPTANCE / " + string.Join(" / ", _results) + " / ESC CANCEL UNSENT" : string.Empty;

    public void Begin(int template)
    {
        if (!PaidConstructionTemplates.IsSupported(template) || _awaiting) return;
        _template = template; IsActive = true; _applying = false; _finished = false; _step = 0;
        _first = default; _second = default; _orientation = default; _request = null;
        _building = null; _conveyor = null; Preview = null; Current = false;
        Array.Fill(_results, "UNSENT");
    }

    public void Cancel(PresentationInteractionState interaction)
    {
        _session = default;
        IsActive = false; _applying = false; _finished = false; _awaiting = false;
        _building = null; _conveyor = null; _correlation = default; _request = null;
        Preview = null; Current = false; interaction.SetTemplateRequest(null);
    }

    public void Update(InputState input, RtsCamera camera, ITerrainQuery terrain, PresentationSnapshot? snapshot,
        PresentationInteractionState interaction, PlayerId issuer, int width, int height, bool captured, float scale)
    {
        bool reset = _session.IsSpecified && snapshot?.SessionId != _session ||
            _width != 0 && (_width != width || _height != height || _scale != scale);
        _width = width; _height = height; _scale = scale;
        if (snapshot is not null) _session = snapshot.SessionId;
        bool left = input.IsMouseButtonDown(PlatformMouseButton.Left);
        bool click = (left || input.TryGetMousePressPosition(PlatformMouseButton.Left, out _)) && !_left;
        _left = left;
        if (reset || input.FocusLostThisFrame || snapshot?.PlayerExperience?.IsMatchComplete == true ||
            input.WasKeyPressed(PlatformKey.Escape) || input.TryGetMousePressPosition(PlatformMouseButton.Right, out _))
        { Cancel(interaction); return; }
        if (captured) { Current = false; Preview = null; interaction.SetTemplateRequest(null); return; }
        if (!_applying && !_finished)
        {
            if (input.WasKeyPressed(PlatformKey.Q)) _orientation = (BuildingOrientation)(((int)_orientation + 3) % 4);
            if (input.WasKeyPressed(PlatformKey.E)) _orientation = (BuildingOrientation)(((int)_orientation + 1) % 4);
            if (!input.HasPointerPosition || !camera.TryScreenPointToWorldOnHorizontalPlane(input.PointerPosition,
                camera.Target.Y, width, height, out Vector3 target) || !terrain.TrySampleHeight(target.X, target.Z, out float ground))
            { Current = false; Preview = null; interaction.SetTemplateRequest(null); return; }
            _anchor = ConstructionGrid.Snap(target with { Y = ground });
        }
        var proposed = new ConstructionTemplatePreviewRequest(0, issuer, _template, _anchor, _orientation, _first, _second);
        if (_request is not {} old || old with { Id = 0 } != proposed)
            _request = proposed with { Id = checked(++_nextId) };
        interaction.SetTemplateRequest(_request);
        Preview = snapshot?.ConstructionTemplate;
        Current = snapshot is not null && snapshot.SessionId == _session && _request is {} requested &&
            Preview is {} read && read.Request == requested && read.Tick == snapshot.Tick &&
            snapshot.PlayerExperience is { IsMatchComplete: false } experience && experience.Tick == snapshot.Tick &&
            experience.Player == issuer;
        if (!Current || Preview is not {} preview || _awaiting || _finished) return;
        if (!_applying)
        {
            if (!click || !preview.CanStart) return;
            _applying = true;
        }
        if (preview.Sites.Any(s => s == TemplateSiteState.Unavailable))
        { Stop("SKIPPED: ENDPOINT LOST"); return; }
        if (_step < PaidConstructionTemplates.BuildingCount)
        {
            var row = preview.Buildings[_step];
            if (!row.IsValid || preview.BuildingCosts[_step].Any(c => !c.IsAvailable))
            { Stop("SKIPPED: " + (!row.IsValid ? row.Failure.ToString() : "MISSING MATERIALS")); return; }
            _building = new(row.BuildingId, row.GroundPosition, row.Orientation, _request!.Value.Id, snapshot!.Tick);
            _awaiting = true; _correlation = default; _results[_step] = "REQUEST PENDING";
            return;
        }
        if (preview.Sites.Any(s => s != TemplateSiteState.Complete))
        { _results[2] = "WAITING FOR COMPLETED BUILDINGS"; return; }
        if (!preview.Link.IsValid)
        { Stop("SKIPPED: " + RtsConveyorConstructionController.Explain(preview.Link.Failure)); return; }
        var source = preview.Buildings[PaidConstructionTemplates.Source].ConveyorOutput!.Value;
        var destination = preview.Buildings[PaidConstructionTemplates.Destination].ConveyorInput!.Value;
        _conveyor = new(ConveyorEdit.Build, ConveyorModule.Route, source.Position - Vector3.UnitY * 5.8f,
            destination.Position - Vector3.UnitY * 5.8f, _orientation, 0, true,
            SourcePortBuilding: _first, DestinationPortBuilding: _second, AutomaticRouting: true, AutomaticHeight: true);
        _awaiting = true; _correlation = default; _results[2] = "REQUEST PENDING";
    }

    public bool TryTakeBuilding(out BuildingPlacementRequest request)
    { request = _building ?? default; bool found = _building.HasValue; _building = null; return found; }
    public bool TryTakeConveyor(out ConveyorBuildRequest request)
    { request = _conveyor ?? default; bool found = _conveyor.HasValue; _conveyor = null; return found; }
    public void ObserveSubmission(PlayerCommandSubmissionReceipt? receipt)
    {
        if (!_awaiting || _correlation.IsSpecified) return;
        PlayerCommandKind expected = _step < 2 ? PlayerCommandKind.Construction : PlayerCommandKind.ConveyorConstruction;
        if (receipt is { Accepted: true } value && value.SessionId == _session && value.Kind == expected)
        { _correlation = value.CorrelationId; return; }
        Stop("NOT SUBMITTED");
    }
    public void ObserveResult(in PlayerCommandResultReadModel result)
    {
        if (!_awaiting || !_correlation.IsSpecified || result.SessionId != _session || result.CorrelationId != _correlation) return;
        _awaiting = false; _correlation = default;
        if (result.State != PlayerCommandFeedbackState.Accepted)
        { Stop("REJECTED: " + (_step < 2 ? result.BuildRejection + " / " + result.PlacementFailure :
            RtsConveyorConstructionController.Explain(result.ConveyorResult))); return; }
        if (_step < 2 && !result.CreatedSite.IsValid) { Stop("ACCEPTED / SITE UNAVAILABLE"); return; }
        _results[_step] = _step < 2 ? "SITE ACCEPTED" : "LINK ACCEPTED";
        if (_step == 0) _first = result.CreatedSite;
        if (_step == 1) _second = result.CreatedSite;
        _step++;
        if (_step == 3) { _finished = true; _applying = false; }
    }
    private void Stop(string reason)
    {
        _results[Math.Min(_step, 2)] = reason;
        for (int i = _step + 1; i < _results.Length; i++) _results[i] = "CANCELLED UNSENT";
        _awaiting = false; _correlation = default; _applying = false; _finished = true;
        _building = null; _conveyor = null;
    }

    public PlacementContextFeedbackView Context(PresentationSnapshot? snapshot)
    {
        var state = _finished ? (_step == 3 ? PlacementContextState.Valid : PlacementContextState.Invalid) :
            _applying || _awaiting || !Current ? PlacementContextState.Pending :
            Preview?.CanStart == true ? PlacementContextState.Valid : PlacementContextState.Invalid;
        string reason = _applying || _finished ? string.Join(" / ", _results) :
            !Current ? "CHECKING COMPLETE DESIGN" : Preview is { HasCompleteQuote: false } ? "CONNECTION INVALID / COST UNQUOTED" : Preview?.CanStart == true ?
                "CLICK: PAY EACH STEP / NO UPFRONT RESERVATION" : "INVALID OR MISSING TOTAL MATERIALS / SEE MARKED PARTS";
        return new(state, PaidConstructionTemplates.Name(_template), reason, "Q E ROTATE / RMB ESC CANCEL UNSENT",
            Preview is { HasCompleteQuote: true } quote ? quote.TotalCosts : null, snapshot?.SessionId ?? default, snapshot?.Tick ?? default);
    }
    public void Draw(DebugDraw draw)
    {
        if (!IsActive || Preview is not {} preview || !Current) return;
        draw.Enabled = true;
        for (int i = 0; i < preview.Buildings.Count; i++)
        {
            var row = preview.Buildings[i];
            Vector4 color = preview.Sites[i] is TemplateSiteState.Building or TemplateSiteState.Complete ?
                new(.3f, .7f, 1, 1) : row.IsValid ? new(.2f, 1, .4f, 1) : new(1, .2f, .1f, 1);
            draw.Box(row.Bounds, color);
            if (row.LoadingCorridor is {} corridor) draw.Box(corridor, color);
            if (row.ConveyorInput is {} input) ConveyorNetworkOverlay.DrawSocket(draw, input.Position, input.Facing, true);
            if (row.ConveyorOutput is {} output) ConveyorNetworkOverlay.DrawSocket(draw, output.Position, output.Facing, false);
            string costs = string.Join(" + ", preview.BuildingCosts[i].Select(c => c.RequiredQuantity + " " + c.DisplayName));
            draw.Label(row.Bounds.Center + Vector3.UnitY * (row.Bounds.Extents.Y + 3),
                (i + 1) + ": " + row.DisplayName + " / " + costs + " / " +
                (preview.Sites[i] != TemplateSiteState.Unsent ? preview.Sites[i].ToString() : row.IsValid ? "DESIGN" : row.Failure.ToString()), color);
        }
        Vector4 beltColor = _finished && _step == 3 ? new(.3f, .7f, 1, 1) : preview.Link.IsValid ? new(.2f, 1, .4f, 1) : new(1, .2f, .1f, 1);
        foreach (var piece in preview.Link.Pieces) draw.Box(piece.Bounds, beltColor);
        if (preview.Link.FailurePosition is {} failed) draw.Circle(failed, 2.5f, beltColor, 16);
        draw.Label(_anchor + Vector3.UnitY * 20, "1 OUT -> 2 IN / " +
            (preview.HasCompleteQuote ? preview.Link.Steel + " STEEL / " : "COST UNAVAILABLE / ") +
            (_applying || _finished ? _results[2] : preview.Link.Failure.ToString()), beltColor);
    }
}
