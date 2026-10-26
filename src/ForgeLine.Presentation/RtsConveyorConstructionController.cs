using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Game;
using ForgeLine.Input;
using ForgeLine.Platform;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public readonly record struct ConveyorPreviewRequest(ulong Id, PlayerId Issuer, ConveyorBuildRequest Request);
public sealed record ConveyorConstructionReadModel(ulong RequestId, SimulationTick Tick,
    ConveyorPlan? Preview, double AvailableSteel, IReadOnlyList<ConveyorOwnedPiece> Pieces,
    IReadOnlyList<ConveyorBuildingPort> Ports);
public readonly record struct ConveyorFeaturePresentationMetadata(ConveyorPlacement Placement, string State)
{
    public bool IsSpecified => State is not null;
    public string Mesh(int lod=0) => "infrastructure.directorate.conveyor."+
        ConveyorGeometry.Slug(Placement.Module)+"."+State+(lod==0?"":".lod"+lod);
    public static string Surface(ConveyorLinkStatus status)=>status switch
    {
        ConveyorLinkStatus.Flowing=>"flowing",ConveyorLinkStatus.Offline=>"unpowered",
        ConveyorLinkStatus.Structural=>"structural_intact",ConveyorLinkStatus.Disconnected=>"disconnected",
        ConveyorLinkStatus.Blocked=>"blocked",ConveyorLinkStatus.Congested=>"congested",
        ConveyorLinkStatus.Damaged=>"damaged",ConveyorLinkStatus.Critical=>"critical",
        ConveyorLinkStatus.Destroyed=>"destroyed",_=>"idle_no_input"
    };
}

public sealed class RtsConveyorConstructionController
{
    private readonly ConveyorEndpointPicker _endpoints = new();
    private Vector3? _anchor;
    private Vector2? _anchorPointer;
    private ConveyorBuildRequest? _pending;
    private ConveyorBuildRequest? _requested;
    private ulong _requestId;
    private PlayerCommandCorrelationId _correlation;
    private SimulationSessionId _session;
    private bool _leftDown;
    private bool _awaiting;
    private int _width,_height;
    private float _scale;
    private EntityId _sourcePort;
    private ConveyorEndpoint? _snappedEndpoint;
    private Vector3? _pointerTarget;
    private double _availableSteel;
    public bool IsActive { get; private set; }
    public ConveyorModule Module { get; private set; }
    public ConveyorEdit Edit { get; private set; }
    public BuildingOrientation Orientation { get; private set; }
    public int Tier { get; private set; }
    public bool XFirst { get; private set; }=true;
    public ConveyorPlan? Preview { get; private set; }
    public bool Current { get; private set; }
    public string Feedback { get; private set; }=string.Empty;
    public string Hint => $"CONVEYOR {Edit} / {ConveyorGeometry.Name(Module)} / TIER {Tier} / "+
        (_awaiting?"REQUEST PENDING":Edit!=ConveyorEdit.Build?"CLICK OWN CONVEYOR":Module!=ConveyorModule.Route?
            "CLICK TO PLACE PART":_anchor.HasValue?"CLICK INPUT BUILDING OR ROUTE END":"CLICK OUTPUT BUILDING OR ROUTE START")+
        " / Q E ROTATE / TAB BEND / T TIER / V EDIT / ESC CANCEL";
    public void Begin(ConveyorModule module)
    {
        Module=module; IsActive=true; Edit=ConveyorEdit.Build; _anchor=null;_anchorPointer=null;_sourcePort=default;
        _snappedEndpoint=null;_pointerTarget=null;
        _requested=null;Preview=null;Current=false; Feedback=string.Empty;
    }
    public void Cancel(PresentationInteractionState interaction)
    {
        IsActive=false;_anchor=null;_anchorPointer=null;_pending=null;_requested=null;Preview=null;_sourcePort=default;
        _snappedEndpoint=null;_pointerTarget=null;
        Current=false;interaction.SetConveyorRequest(null);
        // An already submitted command remains authoritative; UI cancellation
        // cannot retract it. Its correlation is still consumed on completion.
    }
    public void Update(InputState input,RtsCamera camera,ITerrainQuery terrain,
        PresentationSnapshot? snapshot,PresentationInteractionState interaction,
        PlayerId issuer,int width,int height,bool captured,float scale)
    {
        bool reset=_session.IsSpecified && snapshot?.SessionId!=_session ||
            _width!=0 && (_width!=width || _height!=height || _scale!=scale);
        _width=width;_height=height;_scale=scale;
        if(snapshot is not null)_session=snapshot.SessionId;
        if(reset){_awaiting=false;_correlation=default;}
        if(reset || input.FocusLostThisFrame || snapshot?.PlayerExperience?.IsMatchComplete==true)
        {Cancel(interaction);_leftDown=input.IsMouseButtonDown(PlatformMouseButton.Left);return;}
        if(input.WasKeyPressed(PlatformKey.Escape) || input.TryGetMousePressPosition(PlatformMouseButton.Right,out _))
        {Cancel(interaction);return;}
        bool left=input.IsMouseButtonDown(PlatformMouseButton.Left);
        bool click=(left || input.TryGetMousePressPosition(PlatformMouseButton.Left,out _)) && !_leftDown;
        _leftDown=left;
        if(captured){Preview=null;Current=false;_requested=null;_snappedEndpoint=null;_pointerTarget=null;interaction.SetConveyorRequest(null);return;}
        if(_awaiting)return;
        if(input.WasKeyPressed(PlatformKey.Q))Orientation=(BuildingOrientation)(((int)Orientation+3)%4);
        if(input.WasKeyPressed(PlatformKey.E))Orientation=(BuildingOrientation)(((int)Orientation+1)%4);
        if(input.WasKeyPressed(PlatformKey.Tab))XFirst=!XFirst;
        if(input.WasKeyPressed(PlatformKey.T))Tier=1-Tier;
        if(input.WasKeyPressed(PlatformKey.V)){Edit=(ConveyorEdit)(((int)Edit+1)%4);_anchor=null;_anchorPointer=null;}
        if(!input.HasPointerPosition || !camera.TryScreenPointToWorldOnHorizontalPlane(input.PointerPosition,
            camera.Target.Y,width,height,out Vector3 pointer) || !terrain.TrySampleHeight(pointer.X,pointer.Z,out float ground))
        {Preview=null;Current=false;_requested=null;_snappedEndpoint=null;_pointerTarget=null;interaction.SetConveyorRequest(null);return;}
        Vector3 target=ConveyorGeometry.Snap(pointer with {Y=ground});
        float departurePixels=4*(float.IsFinite(scale)?Math.Clamp(scale,.5f,4):1);
        if(_anchorPointer is {} anchorPointer && Vector2.DistanceSquared(input.PointerPosition,anchorPointer)>departurePixels*departurePixels)
            _anchorPointer=null;
        if(_anchorPointer.HasValue && _anchor is {} anchor)target=anchor;
        EntityId destination=default;
        _snappedEndpoint=null;
        bool snapModule=false;
        if(!_anchor.HasValue)_sourcePort=default;
        if(Edit==ConveyorEdit.Build && Module==ConveyorModule.Route && !_anchorPointer.HasValue &&
            _endpoints.TryPick(snapshot,issuer,camera,input.PointerPosition,width,height,scale,_anchor.HasValue,out var endpoint))
        {
            _snappedEndpoint=endpoint;
            target=endpoint.Position;
            if(_anchor.HasValue)destination=endpoint.Building;
            else {_sourcePort=endpoint.Building;Tier=endpoint.Tier;}
        }
        else if(Edit==ConveyorEdit.Build && Module is not (ConveyorModule.Route or ConveyorModule.Support or ConveyorModule.RaisedSupport) &&
            _endpoints.TryPick(snapshot,issuer,camera,input.PointerPosition,width,height,scale,
                Module==ConveyorModule.OutputAdapter,out var mate,
                includeBuildings:Module is ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter))
        {
            foreach(var local in ConveyorGeometry.Sockets(Module))
            {
                if(local.Input==mate.Input)continue;
                Orientation=ConveyorGeometry.Facing(local.Input?mate.Facing:-mate.Facing);
                // Curves and branches have side-facing sockets; choose the exact opposing rotation.
                for(int turn=0;turn<4;turn++)
                {
                    var rotation=BuildingFootprint.GetRotation(Orientation);
                    if(Vector3.Dot(Vector3.Transform(local.Facing,rotation),mate.Facing)<-.999f)
                    {
                        target=mate.Position+Vector3.UnitY*5.8f-Vector3.Transform(local.Position,rotation);
                        Tier=mate.Tier-(int)MathF.Round((local.Position.Y-5.8f)/2);
                        _snappedEndpoint=mate;snapModule=true;
                        if(mate.Input)destination=mate.Building;else _sourcePort=mate.Building;
                        break;
                    }
                    Orientation=(BuildingOrientation)(((int)Orientation+1)%4);
                }
                if(snapModule)break;
            }
        }
        _pointerTarget=_snappedEndpoint?.Position??target;
        EntityId entity=EntityId.Invalid;
        if(Edit!=ConveyorEdit.Build && snapshot?.Conveyors is {} read &&
            snapshot.SessionId.IsSpecified && read.Tick==snapshot.Tick)
        {
            float nearest=float.PositiveInfinity;
            foreach(var row in read.Pieces)
            {
                if(row.Piece.Owner!=issuer || !OperationsWorldOverlay.CanFocus(snapshot,row.Entity))continue;
                var b=row.Piece.Placement.Bounds;
                if(target.X<b.Minimum.X-1 || target.X>b.Maximum.X+1 || target.Z<b.Minimum.Z-1 || target.Z>b.Maximum.Z+1)continue;
                float distance=Vector2.DistanceSquared(new(pointer.X,pointer.Z),new(b.Center.X,b.Center.Z));
                if(distance>=nearest)continue;
                nearest=distance; entity=row.Entity;
                target=row.Piece.Placement.Pivot;
            }
        }
        var request=new ConveyorBuildRequest(Edit,Module,_anchor??target,target,Orientation,Tier,XFirst,entity,
            _sourcePort,destination,AutomaticRouting:Module==ConveyorModule.Route,SnapToSocket:snapModule,
            AutomaticHeight:Edit==ConveyorEdit.Build);
        if(_requested!=request)
        {
            _requested=request;_requestId++;
            interaction.SetConveyorRequest(new(_requestId,issuer,request));
        }
        var result=snapshot?.Conveyors;
        _availableSteel=result?.AvailableSteel??0;
        Current=result is not null && snapshot!.SessionId.IsSpecified && result.RequestId==_requestId && result.Tick==snapshot.Tick &&
            snapshot.PlayerExperience is {IsMatchComplete:false} experience && experience.Tick==snapshot.Tick && experience.Player==issuer;
        Preview=Current?result!.Preview:null;
        if(!click)return;
        if(Edit==ConveyorEdit.Build && Module==ConveyorModule.Route && !_anchor.HasValue)
        { _anchor=target;_anchorPointer=input.PointerPosition;_requested=null;return; }
        if(ChoosingDestination)return;
        if(!Current || Preview is not {IsValid:true})
        {Feedback=!Current?"WAIT FOR PLACEMENT CHECK":FailureReason(snapshot);return;}
        _pending=request;_awaiting=true;_correlation=default;
        Feedback="CONVEYOR REQUEST PENDING";
    }
    public bool TryTakeRequest(out ConveyorBuildRequest request)
    {request=_pending??default;bool found=_pending.HasValue;_pending=null;return found;}
    public void ObserveSubmission(PlayerCommandSubmissionReceipt? receipt,PresentationInteractionState interaction)
    {
        if(!_awaiting || _correlation.IsSpecified)return;
        if(receipt is {Accepted:true,Kind:PlayerCommandKind.ConveyorConstruction} value && value.SessionId==_session)
        {_correlation=value.CorrelationId;return;}
        _awaiting=false;_requested=null;Feedback="CONVEYOR REQUEST NOT SUBMITTED";interaction.SetConveyorRequest(null);
    }
    public void ObserveResult(in PlayerCommandResultReadModel result,PresentationInteractionState interaction)
    {
        if(!_awaiting || result.SessionId!=_session || !_correlation.IsSpecified || result.CorrelationId!=_correlation)return;
        _awaiting=false;_correlation=default;_requested=null;_anchor=null;_anchorPointer=null;Current=false;Preview=null;
        Feedback=result.State==PlayerCommandFeedbackState.Accepted?"CONVEYOR CHANGE ACCEPTED":Explain(result.ConveyorResult);
        _sourcePort=default;
        interaction.SetConveyorRequest(null);
    }
    public PlacementContextFeedbackView Context(PresentationSnapshot? snapshot)
    {
        string controls=Module==ConveyorModule.Route?$"T{Tier} / TAB BEND / T TIER / RMB CANCEL":
            $"T{Tier} / Q E ROTATE / T TIER / RMB CANCEL";
        if(!_anchor.HasValue && Module==ConveyorModule.Route && Edit==ConveyorEdit.Build && !_awaiting)
            return new(PlacementContextState.Pending,"CONVEYOR",_snappedEndpoint.HasValue?
                "OUTPUT SNAPPED / CLICK TO START":"CHOOSE OUTPUT / CLICK BUILDING OR BELT OUTPUT",controls,
                SessionId:snapshot?.SessionId??default,Tick:snapshot?.Tick??default);
        if(ChoosingDestination)
            return new(PlacementContextState.Pending,"CONVEYOR","CHOOSE INPUT / CLICK BUILDING OR BELT INPUT",controls,
                SessionId:snapshot?.SessionId??default,Tick:snapshot?.Tick??default);
        string reason=_awaiting?"REQUEST PENDING":!Current?"CHECKING PLACEMENT":Preview is {} p?
            p.IsValid?$"READY / {p.Steel:0} STEEL / "+(_snappedEndpoint.HasValue?"SNAPPED / CLICK TO BUILD":"CLICK TO BUILD"):FailureReason(snapshot):"CHOOSE START AND END";
        IReadOnlyList<PlayerActionResourceAmount>? costs=Preview is {} plan?
            new[]{new PlayerActionResourceAmount(ResourceIds.Steel,"Steel",plan.Steel,snapshot?.Conveyors?.AvailableSteel??0)}:null;
        return new(_awaiting || !Current?PlacementContextState.Pending:Preview?.IsValid==true?
            PlacementContextState.Valid:Preview?.Failure==ConveyorFailure.InsufficientStock?
            PlacementContextState.MissingMaterials:PlacementContextState.Invalid,"CONVEYOR",reason,controls,costs,
            snapshot?.SessionId??default,snapshot?.Tick??default);
    }
    private bool ChoosingDestination => Edit==ConveyorEdit.Build && Module==ConveyorModule.Route && !_awaiting &&
        _anchor.HasValue && (_anchorPointer.HasValue ||
            _requested is {} request && Vector3.DistanceSquared(request.Start,request.End)<.0004f);
    private string FailureReason(PresentationSnapshot? snapshot) => Preview?.Failure==ConveyorFailure.InsufficientStock && Preview is {} plan?
        $"NEED {plan.Steel:0} STEEL / AVAILABLE {snapshot?.Conveyors?.AvailableSteel??0:0}":ExplainPlan(Preview);
    private static string ExplainPlan(ConveyorPlan? plan) => plan?.Obstruction switch
    {
        ConveyorObstruction.Building=>"BUILDING BLOCKS ROUTE / MOVE END OR CHANGE BEND",
        ConveyorObstruction.Conveyor=>"BELT BLOCKS ROUTE / SNAP TO AN IN OR OUT PORT",
        ConveyorObstruction.VehicleOrObject=>"VEHICLE OR OBJECT BLOCKS ROUTE / CLEAR MARKED AREA",
        ConveyorObstruction.LoadingLane=>"SUPPORT BLOCKS LOADING LANE / MOVE ROUTE",
        _=>Explain(plan?.Failure??ConveyorFailure.InvalidRequest)
    };
    public void Draw(DebugDraw draw)
    {
        if(!IsActive)return;
        draw.Enabled=true;
        if(_snappedEndpoint is {} endpoint)
        {
            Vector3 socket=endpoint.Position+Vector3.UnitY*5.8f;
            draw.Circle(socket,1.6f,new(1,1,1,1),16);
            draw.Label(socket+Vector3.UnitY*3,endpoint.Input?"INPUT SNAPPED":"OUTPUT SNAPPED",new(1,1,1,1));
        }
        if(Preview is null || Edit==ConveyorEdit.Build && Module==ConveyorModule.Route && (!_anchor.HasValue || ChoosingDestination))return;
        Vector4 color=Current && Preview.IsValid?new(.15f,1,.35f,1):new(1,.2f,.15f,1);
        Vector3? label=Preview.FailurePosition is {} failed?failed+Vector3.UnitY*4:
            _pointerTarget.HasValue?_pointerTarget.Value+Vector3.UnitY*10:
            Preview.Pieces.Count>0?Preview.Pieces[0].Bounds.Center:null;
        if(Current && !Preview.IsValid && Preview.FailurePosition is {} failure)
            draw.Circle(failure,2.5f,color,16);
        if(label is {} position)draw.Label(position,
            !Current?"CHECKING PLACEMENT":Preview.IsValid?$"READY / {Preview.Steel:0} STEEL":
            Preview.Failure==ConveyorFailure.InsufficientStock?$"NEED {Preview.Steel:0} STEEL / AVAILABLE {_availableSteel:0}":ExplainPlan(Preview),color);
        foreach(var part in Preview.Pieces)
        {
            draw.Box(part.Bounds,color);
            foreach(var socket in part.Sockets)
                ConveyorNetworkOverlay.DrawSocket(draw,socket.Position,socket.Facing,socket.Input);
        }
    }
    public static string Explain(ConveyorFailure failure)=>failure switch
    {
        ConveyorFailure.None=>"READY",ConveyorFailure.LimitReached=>"INFRASTRUCTURE LIMIT: REMOVE UNUSED PARTS",ConveyorFailure.Terrain=>"TERRAIN EXCEEDS SUPPORT OR SLOPE LIMITS / CHANGE ROUTE",
        ConveyorFailure.SupportOnRoad=>"SUPPORT BLOCKS ROAD / MOVE THE ROUTE",
        ConveyorFailure.OutsideBuildArea=>"OUTSIDE OWN BUILD AREA / MOVE CLOSER TO BASE",ConveyorFailure.Obstructed=>"BLOCKED BY BUILDING, VEHICLE OR BELT / MOVE ROUTE",
        ConveyorFailure.InsufficientStock=>"NOT ENOUGH STEEL IN COMMAND CORE",
        ConveyorFailure.PortMismatch=>"PORTS DO NOT ALIGN / MATCH TIER AND IN-OUT DIRECTION",
        ConveyorFailure.Occupied=>"DRAIN CARGO BEFORE EDITING",ConveyorFailure.NotOwned=>"OWN INFRASTRUCTURE ONLY",
        ConveyorFailure.UnsupportedTier=>"ONLY TIERS 0 AND 1 / RAMPS FROM TIER 0",
        ConveyorFailure.RouteTooLong=>"ROUTE MUST BE 4 TO 256 METRES",
        ConveyorFailure.InvalidSource=>"OWN COMPLETED COMMAND CORE WITH STOCK REQUIRED",
        ConveyorFailure.TargetUnavailable=>"POINT AT OWN CONVEYOR",ConveyorFailure.InvalidRequest=>"INVALID EDIT / CHECK PART AND REPAIR STATE",
        ConveyorFailure.MatchUnavailable=>"MATCH UNAVAILABLE / RESUME AN ACTIVE MATCH",_=>"CONVEYOR CHANGE UNAVAILABLE"
    };
}
