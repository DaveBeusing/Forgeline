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
        Module=module; IsActive=true; Edit=ConveyorEdit.Build; _anchor=null;_sourcePort=default;
        _requested=null;Preview=null;Current=false; Feedback=string.Empty;
    }
    public void Cancel(PresentationInteractionState interaction)
    {
        IsActive=false;_anchor=null;_pending=null;_requested=null;Preview=null;_sourcePort=default;
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
        if(captured){Preview=null;Current=false;_requested=null;interaction.SetConveyorRequest(null);return;}
        if(_awaiting)return;
        if(input.WasKeyPressed(PlatformKey.Q))Orientation=(BuildingOrientation)(((int)Orientation+3)%4);
        if(input.WasKeyPressed(PlatformKey.E))Orientation=(BuildingOrientation)(((int)Orientation+1)%4);
        if(input.WasKeyPressed(PlatformKey.Tab))XFirst=!XFirst;
        if(input.WasKeyPressed(PlatformKey.T))Tier=1-Tier;
        if(input.WasKeyPressed(PlatformKey.V)){Edit=(ConveyorEdit)(((int)Edit+1)%4);_anchor=null;}
        if(!input.HasPointerPosition || !camera.TryScreenPointToWorldOnHorizontalPlane(input.PointerPosition,
            camera.Target.Y,width,height,out Vector3 pointer) || !terrain.TrySampleHeight(pointer.X,pointer.Z,out float ground))
        {Preview=null;Current=false;_requested=null;interaction.SetConveyorRequest(null);return;}
        Vector3 target=ConveyorGeometry.Snap(pointer with {Y=ground});
        EntityId destination=default;
        if(!_anchor.HasValue)_sourcePort=default;
        if(Edit==ConveyorEdit.Build && Module==ConveyorModule.Route &&
            _endpoints.TryPick(snapshot,issuer,camera,input.PointerPosition,width,height,scale,_anchor.HasValue,out var endpoint))
        {
            target=endpoint.Position;
            if(_anchor.HasValue)destination=endpoint.Building;
            else {_sourcePort=endpoint.Building;Tier=endpoint.Tier;}
        }
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
            _sourcePort,destination,AutomaticRouting:Module==ConveyorModule.Route);
        if(_requested!=request)
        {
            _requested=request;_requestId++;
            interaction.SetConveyorRequest(new(_requestId,issuer,request));
        }
        var result=snapshot?.Conveyors;
        Current=result is not null && snapshot!.SessionId.IsSpecified && result.RequestId==_requestId && result.Tick==snapshot.Tick &&
            snapshot.PlayerExperience is {IsMatchComplete:false} experience && experience.Tick==snapshot.Tick && experience.Player==issuer;
        Preview=Current?result!.Preview:null;
        if(!click)return;
        if(Edit==ConveyorEdit.Build && Module==ConveyorModule.Route && !_anchor.HasValue)
        { _anchor=target;_requested=null;return; }
        if(!Current || Preview is not {IsValid:true})return;
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
        _awaiting=false;_correlation=default;_requested=null;_anchor=null;Current=false;Preview=null;
        Feedback=result.State==PlayerCommandFeedbackState.Accepted?"CONVEYOR CHANGE ACCEPTED":Explain(result.ConveyorResult);
        _sourcePort=default;
        interaction.SetConveyorRequest(null);
    }
    public PlacementContextFeedbackView Context(PresentationSnapshot? snapshot)
    {
        if(!_anchor.HasValue && Module==ConveyorModule.Route && Edit==ConveyorEdit.Build && !_awaiting)
            return new(PlacementContextState.Pending,"CONVEYOR INFRASTRUCTURE","CLICK THE SOURCE BUILDING OR ROUTE START / CONNECTIONS ARE AUTOMATIC",Hint);
        string reason=_awaiting?"REQUEST PENDING":!Current?"CHECKING PLACEMENT":Preview is {} p?
            p.IsValid?$"VALID / {p.Pieces.Count} PARTS / {p.Steel:0} STEEL":Explain(p.Failure):"CHOOSE START AND END";
        IReadOnlyList<PlayerActionResourceAmount>? costs=Preview is {} plan?
            new[]{new PlayerActionResourceAmount(ResourceIds.Steel,"Steel",plan.Steel,snapshot?.Conveyors?.AvailableSteel??0)}:null;
        return new(_awaiting || !Current?PlacementContextState.Pending:Preview?.IsValid==true?
            PlacementContextState.Valid:PlacementContextState.Invalid,"CONVEYOR INFRASTRUCTURE",reason,Hint,costs);
    }
    public void Draw(DebugDraw draw)
    {
        if(!IsActive || Preview is null)return;
        draw.Enabled=true;
        Vector4 color=Current && Preview.IsValid?new(.15f,1,.35f,1):new(1,.2f,.15f,1);
        foreach(var part in Preview.Pieces)
        {
            draw.Box(part.Bounds,color);
            foreach(var socket in part.Sockets)
            {
                Vector3 direction=socket.Input?-socket.Facing:socket.Facing;
                Vector3 tip=socket.Position+direction*.8f;
                Vector3 side=Vector3.Cross(Vector3.UnitY,direction)*.18f;
                draw.Line(socket.Position,tip,color);
                draw.Line(tip,tip-direction*.25f+side,color);
                draw.Line(tip,tip-direction*.25f-side,color);
                draw.Label(socket.Position,socket.Input?"IN":"OUT",color);
            }
        }
        if(Preview.Pieces.Count>0)draw.Label(Preview.Pieces[0].Bounds.Center,
            Preview.IsValid?$"VALID / {Preview.Steel:0} STEEL":Explain(Preview.Failure),color);
    }
    public static string Explain(ConveyorFailure failure)=>failure switch
    {
        ConveyorFailure.None=>"READY",ConveyorFailure.LimitReached=>"INFRASTRUCTURE LIMIT: REMOVE UNUSED PARTS",ConveyorFailure.Terrain=>"TERRAIN: KEEP 5M CLEARANCE AND LEVEL SUPPORT FEET",
        ConveyorFailure.SupportOnRoad=>"SUPPORT BLOCKS ROAD / MOVE THE ROUTE",
        ConveyorFailure.OutsideBuildArea=>"OUTSIDE OWN BUILD AREA",ConveyorFailure.Obstructed=>"OBSTRUCTION / PORT DIRECTION CONFLICT",
        ConveyorFailure.InsufficientStock=>"NOT ENOUGH STEEL IN COMMAND CORE",
        ConveyorFailure.PortMismatch=>"PORT HEIGHT / DIRECTION / SHAPE MISMATCH",
        ConveyorFailure.Occupied=>"DRAIN CARGO BEFORE EDITING",ConveyorFailure.NotOwned=>"OWN INFRASTRUCTURE ONLY",
        ConveyorFailure.UnsupportedTier=>"ONLY TIERS 0 AND 1 / RAMPS FROM TIER 0",
        ConveyorFailure.RouteTooLong=>"ROUTE MUST BE 4 TO 256 METRES",
        ConveyorFailure.InvalidSource=>"OWN COMPLETED COMMAND CORE WITH STOCK REQUIRED",
        ConveyorFailure.TargetUnavailable=>"POINT AT OWN CONVEYOR",_=>"CONVEYOR CHANGE UNAVAILABLE"
    };
}
