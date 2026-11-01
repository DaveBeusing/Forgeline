using System.Numerics;
using System.Text.Json.Serialization;
using ForgeLine.Core;
using ForgeLine.Economy;
using ForgeLine.Ecs;
using ForgeLine.Intelligence;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Game;

public enum ConveyorModule : byte
{
    Route, ShortSegment, Standard, CurveLeft, CurveRight, RampUp, RampDown,
    Merger, Splitter, InputAdapter, OutputAdapter, Endcap, Support, RaisedSupport, Overpass
}
public enum ConveyorEdit : byte { Build, Remove, Replace, Repair }
public enum ConveyorFailure : byte
{
    None, InvalidRequest, RouteTooLong, Terrain, OutsideBuildArea, Obstructed,
    SupportOnRoad, InsufficientStock, InvalidSource, TargetUnavailable, NotOwned,
    Occupied, UnsupportedTier, PortMismatch, MatchUnavailable, LimitReached, RecoveryFull
}
public enum ConveyorObstruction : byte { None, Building, Conveyor, VehicleOrObject, LoadingLane }
public readonly record struct ConveyorBuildRequest(
    ConveyorEdit Edit, ConveyorModule Module, Vector3 Start, Vector3 End,
    BuildingOrientation Orientation, int Tier, bool XFirst,
    EntityId Target = default, EntityId SourcePortBuilding = default,
    EntityId DestinationPortBuilding = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AutomaticRouting = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool SnapToSocket = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool AutomaticHeight = false);
public readonly record struct ConveyorSocket(Vector3 Position, Vector3 Facing, bool Input);
public readonly record struct BuildingConveyorSocket(string Key, ConveyorSocket Socket, bool Adaptable);
public enum ConveyorPortState : byte { Free, ConnectedInput, ConnectedOutput, Blocked }
public readonly record struct ConveyorPlacement(ConveyorModule Module, Vector3 Pivot,
    BuildingOrientation Orientation, int Tier,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] float SpanMeters = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] float RiseMeters = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] float SupportHeightMeters = 0)
{
    public bool IsSupport => Module is ConveyorModule.Support or ConveyorModule.RaisedSupport;
    public Quaternion Rotation => RiseMeters == 0 ? BuildingFootprint.GetRotation(Orientation) :
        Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitX,-MathF.Atan2(RiseMeters,HorizontalSpan)),
            BuildingFootprint.GetRotation(Orientation));
    [JsonIgnore]
    public float HorizontalSpan => Module == ConveyorModule.Standard ? 8 : SpanMeters > 0 ? SpanMeters : 4;
    [JsonIgnore]
    public Vector3 LocalScale => new(1,
        IsSupport && SupportHeightMeters > 0 ? SupportHeightMeters / ConveyorGeometry.LocalBounds(Module).Maximum.Y : 1,
        RiseMeters != 0 ? MathF.Sqrt(HorizontalSpan*HorizontalSpan+RiseMeters*RiseMeters)/(Module==ConveyorModule.Standard?8:4) :
        Module == ConveyorModule.ShortSegment && SpanMeters > 0 ? SpanMeters/4 : 1);
    [JsonIgnore]
    public Vector3 RenderPivot => RiseMeters == 0 ? Pivot : Pivot + Vector3.UnitY*5.8f -
        Vector3.Transform(Vector3.UnitY*5.8f*LocalScale,Rotation);
    public Vector3 Point(Vector3 local) => RenderPivot + Vector3.Transform(local * LocalScale, Rotation);
    public float UndersideAt(float x, float z)
    {
        if(RiseMeters==0)return Pivot.Y+ConveyorGeometry.LocalBounds(Module).Minimum.Y*LocalScale.Y;
        Vector3 direction=Vector3.Transform(Vector3.UnitZ,BuildingFootprint.GetRotation(Orientation));
        float along=(x-Pivot.X)*direction.X+(z-Pivot.Z)*direction.Z;
        float grade=RiseMeters/HorizontalSpan;
        return Pivot.Y+5.8f+along*grade-.6f*MathF.Sqrt(1+grade*grade);
    }
    public ConveyorSocket[] Sockets
    {
        get
        {
            ConveyorPlacement placement=this;
            return ConveyorGeometry.Sockets(Module).Select(s => new ConveyorSocket(
                placement.Point(s.Position), Vector3.Transform(s.Facing, BuildingFootprint.GetRotation(placement.Orientation)), s.Input)).ToArray();
        }
    }
    public AxisAlignedBounds Bounds
    {
        get
        {
            var (lo, hi) = ConveyorGeometry.LocalBounds(Module);
            Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            for (int i=0;i<8;i++)
            {
                Vector3 p=Point(new((i&1)==0?lo.X:hi.X,(i&2)==0?lo.Y:hi.Y,(i&4)==0?lo.Z:hi.Z));
                min=Vector3.Min(min,p); max=Vector3.Max(max,p);
            }
            return new(min,max);
        }
    }
}
public readonly record struct ConveyorPiece(PlayerId Owner, ConveyorPlacement Placement,
    double PaidSteel, EntityId Attachment = default, double BufferedQuantity = 0, bool AutomaticSupport = false);
public sealed record ConveyorPlan(ConveyorFailure Failure, IReadOnlyList<ConveyorPlacement> Pieces, double Steel)
{
    public bool IsValid => Failure == ConveyorFailure.None;
    public ConveyorCargoRecovery[] CargoRecovery { get; init; } = [];
    public ConveyorObstruction Obstruction { get; init; }
    public Vector3? FailurePosition { get; init; }
}
public enum ConveyorLinkStatus : byte { Structural, NoInput, Disconnected, Blocked, Congested, Damaged, Critical, Destroyed, Flowing, Offline }
public readonly record struct ConveyorOwnedPiece(EntityId Entity, ConveyorPiece Piece, ConveyorLinkStatus Status=ConveyorLinkStatus.NoInput,
    ConveyorFlowState? Flow=null, IReadOnlyList<InventoryResourceQuantity>? Cargo=null,
    ConveyorRoutingState? Routing=null);

public static class ConveyorGeometry
{
    public const uint MenuBase = 2000;
    public const uint MenuCount = (uint)ConveyorModule.Overpass + 1;
    public static Vector3 BuildingPortDimensions(BuildingId id) =>
        id==BuildingIds.CommandCore?new(20,12,20):id==BuildingIds.Extractor?new(12,8,12):
        id==BuildingIds.StorageDepot?new(14,8,14):
        id==BuildingIds.Smelter || id==BuildingIds.Refinery?new(18,10,16):
        id==BuildingIds.ElectronicsPlant || id==BuildingIds.AmmunitionPlant?new(18,10,18):
        id==BuildingIds.LogisticsHub?new(18,9,18):id==BuildingIds.SupplyDepot?new(16,8,16):
        id==BuildingIds.Barracks?new(18,8,16):id==BuildingIds.VehicleFactory?new(26,12,24):default;
    public static string Slug(ConveyorModule module) => module switch
    {
        ConveyorModule.ShortSegment=>"straight_short", ConveyorModule.Standard=>"straight_standard",
        ConveyorModule.CurveLeft=>"curve_90", ConveyorModule.CurveRight=>"curve_90.right",
        ConveyorModule.RampUp=>"ramp_up", ConveyorModule.RampDown=>"ramp_down",
        ConveyorModule.Merger=>"merger_2to1", ConveyorModule.Splitter=>"splitter_1to2",
        ConveyorModule.InputAdapter=>"io_in", ConveyorModule.OutputAdapter=>"io_out",
        ConveyorModule.Endcap=>"endcap", ConveyorModule.Support=>"support_light",
        ConveyorModule.RaisedSupport=>"support_light.tier1", _=>"straight_standard"
    };
    public static double Cost(ConveyorModule module) => module switch
    {
        ConveyorModule.Overpass=>66,
        ConveyorModule.ShortSegment=>8, ConveyorModule.Standard=>14,
        ConveyorModule.Support=>4, ConveyorModule.RaisedSupport=>6,
        ConveyorModule.Merger or ConveyorModule.Splitter=>24,
        ConveyorModule.Endcap=>4, _=>16
    };
    public static double Cost(ConveyorPlacement placement) => placement.SupportHeightMeters > 0 ?
        Cost(placement.Module)*Math.Max(1,Math.Ceiling(placement.SupportHeightMeters/LocalBounds(placement.Module).Maximum.Y)) :
        placement.RiseMeters != 0 ? Math.Ceiling(Cost(placement.Module)*Math.Sqrt(1+
            Math.Pow(placement.RiseMeters/placement.HorizontalSpan,2))) : Cost(placement.Module);
    public static string Name(ConveyorModule module)=>module switch
    {
        ConveyorModule.Overpass=>"Conveyor overpass",
        ConveyorModule.Route=>"Draw route",ConveyorModule.ShortSegment=>"Short belt",ConveyorModule.Standard=>"Standard belt",
        ConveyorModule.CurveLeft=>"Curve, left",ConveyorModule.CurveRight=>"Curve, right",
        ConveyorModule.RampUp=>"Ramp up",ConveyorModule.RampDown=>"Ramp down",
        ConveyorModule.Merger=>"Merger",ConveyorModule.Splitter=>"Splitter",
        ConveyorModule.InputAdapter=>"Building output adapter",ConveyorModule.OutputAdapter=>"Building input adapter",
        ConveyorModule.Endcap=>"End cap",ConveyorModule.Support=>"Support",ConveyorModule.RaisedSupport=>"Raised support",
        _=>"Conveyor"
    };
    public static ConveyorSocket[] Sockets(ConveyorModule module)
    {
        ConveyorSocket In(float x,float y,float z,Vector3? facing=null)=>new(new(x,y,z),facing??-Vector3.UnitZ,true);
        ConveyorSocket Out(float x,float y,float z,Vector3? facing=null)=>new(new(x,y,z),facing??Vector3.UnitZ,false);
        return module switch
        {
            ConveyorModule.Overpass=>[In(0,5.8f,-12),Out(0,5.8f,12)],
            ConveyorModule.Support or ConveyorModule.RaisedSupport=>[],
            ConveyorModule.ShortSegment or ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter=>[In(0,5.8f,-2),Out(0,5.8f,2)],
            ConveyorModule.CurveLeft=>[In(-4,5.8f,0),Out(0,5.8f,4,Vector3.UnitX)],
            ConveyorModule.CurveRight=>[In(4,5.8f,0),Out(0,5.8f,4,-Vector3.UnitX)],
            ConveyorModule.RampUp=>[In(0,5.8f,-4),Out(0,7.8f,4)],
            ConveyorModule.RampDown=>[In(0,7.8f,-4),Out(0,5.8f,4)],
            ConveyorModule.Merger=>[In(-4,5.8f,-4),In(4,5.8f,-4),Out(0,5.8f,4)],
            ConveyorModule.Splitter=>[In(0,5.8f,-4),Out(-4,5.8f,4),Out(4,5.8f,4)],
            ConveyorModule.Endcap=>[In(0,5.8f,-.4f)],
            _=>[In(0,5.8f,-4),Out(0,5.8f,4)]
        };
    }
    public static (Vector3 Minimum,Vector3 Maximum) LocalBounds(ConveyorModule module) => module switch
    {
        ConveyorModule.Overpass=>(new(-1.4f,5.2f,-12),new(1.4f,8.3f,12)),
        ConveyorModule.Support=>(new(-.4f,0,-.4f),new(.4f,5.22f,.4f)),
        ConveyorModule.RaisedSupport=>(new(-.4f,0,-.4f),new(.4f,7.22f,.4f)),
        ConveyorModule.CurveLeft=>(new(-5.4f,5.2f,-.1f),new(.1f,6.3f,5.4f)),
        ConveyorModule.CurveRight=>(new(-.1f,5.2f,-.1f),new(5.4f,6.3f,5.4f)),
        ConveyorModule.Merger or ConveyorModule.Splitter=>(new(-5.4f,5.2f,-4),new(5.4f,6.3f,4)),
        ConveyorModule.RampUp or ConveyorModule.RampDown=>(new(-1.4f,5.2f,-4),new(1.4f,8.3f,4)),
        ConveyorModule.Endcap=>(new(-1.4f,5.2f,-.4f),new(1.4f,6.32f,.4f)),
        ConveyorModule.ShortSegment or ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter=>(new(-1.4f,5.2f,-2),new(1.4f,6.3f,2)),
        _=>(new(-1.4f,5.2f,-4),new(1.4f,6.3f,4))
    };
    public static Vector3 Snap(Vector3 p)=>ConstructionGrid.Snap(p);

    public static ConveyorPlacement[] Overpass(Vector3 pivot, BuildingOrientation orientation)
    {
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, BuildingFootprint.GetRotation(orientation));
        return [new(ConveyorModule.RampUp, pivot - forward * 8, orientation, 0),
            new(ConveyorModule.Standard, pivot + Vector3.UnitY * 2, orientation, 1),
            new(ConveyorModule.RampDown, pivot + forward * 8, orientation, 0)];
    }

    public static bool TryGetBuildingPort(BuildingId building, Vector3 ground, Quaternion rotation, bool input,
        out ConveyorSocket socket)
    {
        Vector3 dimensions = BuildingPortDimensions(building);
        socket = default;
        if (dimensions == default || building == BuildingIds.Extractor && input ||
            !input && (building == BuildingIds.Barracks || building == BuildingIds.VehicleFactory)) return false;
        float sign = input ? -1 : 1;
        socket = new(ground + Vector3.Transform(new Vector3(sign * dimensions.X / 2, 5.8f, 0), rotation),
            Vector3.Transform(sign * Vector3.UnitX, rotation), input);
        return true;
    }
    /// <summary>Stable physical sockets. Existing west/east attachments retain their positions;
    /// secondary positions are away from the +Z loading lane.</summary>
    public static IReadOnlyList<BuildingConveyorSocket> BuildingSockets(
        BuildingId building, Vector3 ground, Quaternion rotation)
    {
        if (building == BuildingIds.CommandCore || building == BuildingIds.StorageDepot)
        {
            Vector3 dimensions = BuildingPortDimensions(building);
            float halfWidth = dimensions.X / 2;
            float secondaryZ = building == BuildingIds.CommandCore ? -6 : -4;
            BuildingConveyorSocket Physical(string key, float x, float z, float sign) =>
                new(key, new(ground + Vector3.Transform(new Vector3(x, 5.8f, z), rotation),
                    Vector3.Transform(sign * Vector3.UnitX, rotation), false), true);
            return [
                Physical("west.primary", -halfWidth, 0, -1),
                Physical("east.primary", halfWidth, 0, 1),
                Physical("west.secondary", -halfWidth, secondaryZ, -1),
                Physical("east.secondary", halfWidth, secondaryZ, 1)
            ];
        }
        var sockets = new List<BuildingConveyorSocket>(2);
        foreach (bool input in new[] { true, false })
            if (TryGetBuildingPort(building, ground, rotation, input, out ConveyorSocket socket))
                sockets.Add(new(input ? "input" : "output", socket, false));
        return sockets;
    }

    public static BuildingOrientation Facing(Vector3 direction)=> MathF.Abs(direction.X)>MathF.Abs(direction.Z)
        ? direction.X>0?BuildingOrientation.East:BuildingOrientation.West
        : direction.Z>0?BuildingOrientation.North:BuildingOrientation.South;
    public static bool Overlap(AxisAlignedBounds a,AxisAlignedBounds b,float inset=.03f)=>
        a.Minimum.X<b.Maximum.X-inset && a.Maximum.X>b.Minimum.X+inset &&
        a.Minimum.Y<b.Maximum.Y-inset && a.Maximum.Y>b.Minimum.Y+inset &&
        a.Minimum.Z<b.Maximum.Z-inset && a.Maximum.Z>b.Minimum.Z+inset;
}

/// <summary>Authority for constructed geometry and paid edits; no inventory transport is inferred.</summary>
public sealed class ConveyorConstructionService
{
    private readonly SimulationCoordinator _simulation;
    private readonly ITerrainQuery _terrain;
    private readonly BattlefieldBuildableAreaQuery _area;
    private readonly SpatialGridIndex _spatial;
    private readonly InventoryStore _stock;
    private readonly BattlefieldDefinition _battlefield;
    private readonly SpatialQueryBuffer _query=new();
    internal Action<SimulationContext>? NavigationChanged { get; set; }
    public ConveyorConstructionService(SimulationCoordinator simulation, ITerrainQuery terrain,
        SpatialGridIndex spatial, InventoryStore stock, BattlefieldDefinition battlefield)
    {
        _simulation=simulation; _terrain=terrain; _spatial=spatial; _stock=stock;
        _battlefield=battlefield; _area=new BattlefieldBuildableAreaQuery(battlefield);
    }
    public bool Matches(SimulationCoordinator simulation)=>ReferenceEquals(_simulation,simulation);
    public static IReadOnlyList<ConveyorOwnedPiece> Capture(EntityRegistry entities,PlayerId owner,InventoryStore? inventories=null)
    {
        var rows=new List<ConveyorOwnedPiece>();
        var inputs=new Dictionary<(int X,int Y,int Z,int DX,int DZ),(Vector3 Position,bool Cap)>();
        var outputs=new Dictionary<(int X,int Y,int Z,int DX,int DZ),Vector3>();
        foreach(EntityId e in entities.Query<ConveyorPiece>(QueryIterationOrder.StableByEntityIndex))
        {
            ConveyorPiece piece=entities.GetComponent<ConveyorPiece>(e);
            if(piece.Owner==owner)rows.Add(new(e,piece));
        }
        foreach(var row in rows)
        {
            if(entities.TryGetComponent(row.Entity,out ForgeLine.Combat.HealthState health) && health.IsDepleted)continue;
            foreach(var socket in row.Piece.Placement.Sockets)
                if(socket.Input)inputs[Key(socket.Position,socket.Facing)]=(socket.Position,row.Piece.Placement.Module==ConveyorModule.Endcap);
                else outputs[Key(socket.Position,socket.Facing)]=socket.Position;
        }
        foreach(var port in CapturePorts(entities,owner))
            if(port.Input)inputs[Key(port.Position,port.Facing)]=(port.Position,false);
            else outputs[Key(port.Position,port.Facing)]=port.Position;
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i];
            ConveyorLinkStatus status=row.Piece.Placement.IsSupport || row.Piece.Placement.Module==ConveyorModule.Endcap?
                ConveyorLinkStatus.Structural:ConveyorLinkStatus.NoInput;
            bool missing=false,capped=false;
            foreach(var socket in row.Piece.Placement.Sockets)
            {
                if(status==ConveyorLinkStatus.Structural)break;
                var key=Key(socket.Position,-socket.Facing);
                bool found=false;
                for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++)for(int z=-1;z<=1;z++)
                {
                    var adjacent=(key.Item1+x,key.Item2+y,key.Item3+z,key.Item4,key.Item5);
                    if(socket.Input && outputs.TryGetValue(adjacent,out Vector3 point) &&
                        Vector3.DistanceSquared(point,socket.Position)<.0004f)found=true;
                    if(!socket.Input && inputs.TryGetValue(adjacent,out var terminal) &&
                        Vector3.DistanceSquared(terminal.Position,socket.Position)<.0004f)
                    {found=true;capped|=terminal.Cap;}
                }
                missing|=!found;
            }
            if(status!=ConveyorLinkStatus.Structural)
                status=capped?ConveyorLinkStatus.Blocked:missing?ConveyorLinkStatus.Disconnected:ConveyorLinkStatus.NoInput;
            if(row.Piece.BufferedQuantity>0 && status is ConveyorLinkStatus.Disconnected or ConveyorLinkStatus.Blocked)
                status=ConveyorLinkStatus.Congested;
            if(entities.TryGetComponent(row.Entity,out ForgeLine.Combat.HealthState health) && health.Fraction<.7)
                status=health.IsDepleted?ConveyorLinkStatus.Destroyed:health.Fraction<.25?ConveyorLinkStatus.Critical:ConveyorLinkStatus.Damaged;
            if (entities.TryGetComponent(row.Entity, out ConveyorFlowState flow)) status=flow.Status;
            rows[i]=row with {Status=status,
                Routing=entities.TryGetComponent(row.Entity,out ConveyorRoutingState routing)?routing:null,
                Flow=entities.TryGetComponent(row.Entity,out ConveyorFlowState state)?state:null,
                Cargo=inventories is not null && entities.TryGetComponent(row.Entity,out ConveyorFlowState stock) && inventories.Contains(stock.Buffer)?
                    Array.AsReadOnly(inventories.GetResourceQuantities(stock.Buffer)):null};
        }
        return rows.AsReadOnly();

        static (int,int,int,int,int) Key(Vector3 point,Vector3 facing)=>
            ((int)MathF.Round(point.X*50),(int)MathF.Round(point.Y*50),(int)MathF.Round(point.Z*50),
             (int)MathF.Round(facing.X),(int)MathF.Round(facing.Z));
    }
    public static IReadOnlyList<ConveyorBuildingPort> CapturePorts(EntityRegistry entities, PlayerId owner)
    {
        var ports = new List<ConveyorBuildingPort>();
        // Resolve full-generation adapter attachments once for this completed-tick capture.
        var adapters = new Dictionary<EntityId, List<ConveyorPiece>>();
        foreach (EntityId id in entities.Query<ConveyorPiece>(QueryIterationOrder.StableByEntityIndex))
        {
            ConveyorPiece piece = entities.GetComponent<ConveyorPiece>(id);
            if (piece.Owner != owner || !piece.Attachment.IsValid ||
                piece.Placement.Module is not (ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter))
                continue;
            if (!adapters.TryGetValue(piece.Attachment, out List<ConveyorPiece>? attached))
            {
                attached = new List<ConveyorPiece>();
                adapters.Add(piece.Attachment, attached);
            }
            attached.Add(piece);
        }
        foreach (EntityId entity in entities.Query<CompletedBuilding, WorldTransform>(QueryIterationOrder.StableByEntityIndex))
        {
            CompletedBuilding building = entities.GetComponent<CompletedBuilding>(entity);
            if (building.Owner != owner) continue;
            if (entities.TryGetComponent(entity, out ForgeLine.Combat.HealthState health) && health.IsDepleted) continue;
            Vector3 expected = ConveyorGeometry.BuildingPortDimensions(building.BuildingId);
            if (expected == default) continue;
            WorldTransform transform = entities.GetComponent<WorldTransform>(entity);
            if (Vector3.DistanceSquared(transform.Scale, expected) > .001f) continue;
            Vector3 origin = transform.Position - Vector3.UnitY * (expected.Y / 2);
            foreach (BuildingConveyorSocket physical in ConveyorGeometry.BuildingSockets(
                         building.BuildingId, origin, transform.Rotation))
            {
                ConveyorSocket socket = physical.Socket;
                if (building.BuildingId == BuildingIds.Extractor &&
                    (socket.Input || !entities.TryGetComponent(entity, out ResourceExtractor extractor) ||
                     !ConveyorSimulationSystem.Eligible(extractor.ResourceId))) continue;
                if (!socket.Input && !physical.Adaptable &&
                    (building.BuildingId == BuildingIds.Barracks || building.BuildingId == BuildingIds.VehicleFactory))
                    continue;

                if (!physical.Adaptable)
                {
                    ports.Add(new(entity, owner, socket.Position, socket.Facing, socket.Input, physical.Key));
                    continue;
                }

                int attachmentCount = 0;
                ConveyorPortState state = ConveyorPortState.Free;
                if (adapters.TryGetValue(entity, out List<ConveyorPiece>? connections))
                    foreach (ConveyorPiece adapter in connections)
                    {
                        bool intake = adapter.Placement.Module == ConveyorModule.OutputAdapter;
                        ConveyorSocket terminal = intake ? adapter.Placement.Sockets[^1] : adapter.Placement.Sockets[0];
                        if (Vector3.DistanceSquared(terminal.Position, socket.Position) >= .0004f) continue;
                        attachmentCount++;
                        state = Vector3.Dot(terminal.Facing, socket.Facing) < -.999f
                            ? intake ? ConveyorPortState.ConnectedInput : ConveyorPortState.ConnectedOutput
                            : ConveyorPortState.Blocked;
                    }
                if (attachmentCount > 1) state = ConveyorPortState.Blocked;
                if (state == ConveyorPortState.Free)
                {
                    ports.Add(new(entity, owner, socket.Position, socket.Facing, true, physical.Key, true, state));
                    ports.Add(new(entity, owner, socket.Position, socket.Facing, false, physical.Key, true, state));
                }
                else
                {
                    // Exactly one resolved role can serve each occupied physical position.
                    ports.Add(new(entity, owner, socket.Position, socket.Facing,
                        state != ConveyorPortState.ConnectedOutput, physical.Key, true, state));
                }
            }
        }
        return ports.AsReadOnly();
    }

    /// <summary>Read-only proposal for future authored endpoints. Execution still requires real completed ports.</summary>
    public ConveyorPlan PreviewDesignRoute(EntityRegistry entities, PlayerId owner,
        ConveyorSocket output, ConveyorSocket input, EntityId payer)
    {
        if (!owner.IsSpecified || output.Input || !input.Input ||
            !entities.TryGetComponent(payer, out InventoryStorage storage) || !_stock.Contains(storage.InventoryId) ||
            !entities.TryGetComponent(payer, out ControllableEntity sourceOwner) || sourceOwner.Owner != owner ||
            !entities.HasComponent<CompletedBuilding>(payer))
            return new(ConveyorFailure.InvalidSource, Array.Empty<ConveyorPlacement>(), 0);
        var request = new ConveyorBuildRequest(ConveyorEdit.Build, ConveyorModule.Route,
            output.Position - Vector3.UnitY * 5.8f, input.Position - Vector3.UnitY * 5.8f,
            BuildingOrientation.North, 0, true, AutomaticRouting: true, AutomaticHeight: true);
        ConveyorBuildingPort[] ports = [new(default, owner, output.Position, output.Facing, false),
            new(default, owner, input.Position, input.Facing, true)];
        return PreviewAutomatic(entities, owner, request, storage, ports);
    }

    public ConveyorPlan Preview(EntityRegistry entities,PlayerId owner,ConveyorBuildRequest request,EntityId payer)
    {
        var pieces=new List<ConveyorPlacement>();
        ConveyorPlan Fail(ConveyorFailure f,double cost=0)=>new(f,Array.AsReadOnly(pieces.ToArray()),cost);
        if (!owner.IsSpecified || !Enum.IsDefined(request.Module) || !Enum.IsDefined(request.Edit) ||
            !Enum.IsDefined(request.Orientation) || !Finite(request.Start) || !Finite(request.End)) return Fail(ConveyorFailure.InvalidRequest);
        if (request.Tier is <0 or >1 || (request.Module is ConveyorModule.RampUp or ConveyorModule.RampDown or ConveyorModule.Overpass && request.Tier!=0))
            return Fail(ConveyorFailure.UnsupportedTier);
        if (!entities.TryGetComponent(payer,out InventoryStorage storage) || !_stock.Contains(storage.InventoryId) ||
            !entities.TryGetComponent(payer,out ControllableEntity sourceOwner) || sourceOwner.Owner!=owner ||
            !entities.HasComponent<CompletedBuilding>(payer)) return Fail(ConveyorFailure.InvalidSource);
        if (request.Module == ConveyorModule.Overpass && request.Edit == ConveyorEdit.Replace)
            return Fail(ConveyorFailure.InvalidRequest);
        if (request.AutomaticRouting && request.Edit == ConveyorEdit.Build && request.Module == ConveyorModule.Route)
            return PreviewAutomatic(entities, owner, request, storage);
        if (request.SnapToSocket)
        {
            if (request.Edit != ConveyorEdit.Build || request.Target.IsValid || request.Module is ConveyorModule.Route or
                ConveyorModule.Support or ConveyorModule.RaisedSupport)
                return Fail(ConveyorFailure.InvalidRequest);
            var placement = new ConveyorPlacement(request.Module, request.Start, request.Orientation, request.Tier);
            var ports = CapturePorts(entities, owner);
            var existing = Capture(entities, owner);
            bool matches = placement.Sockets.Any(socket =>
                existing.Any(row => row.Status != ConveyorLinkStatus.Destroyed && row.Piece.Placement.Sockets.Any(mate =>
                    socket.Input != mate.Input && Vector3.DistanceSquared(socket.Position, mate.Position) < .0004f &&
                    Vector3.Dot(socket.Facing, mate.Facing) < -.999f)) ||
                ports.Any(port => port.Building == (socket.Input ? request.SourcePortBuilding : request.DestinationPortBuilding) &&
                    port.Input != socket.Input && (!port.Adaptable || port.Status == ConveyorPortState.Free) &&
                    Vector3.DistanceSquared(port.Position, socket.Position) < .0004f &&
                    Vector3.Dot(port.Facing, socket.Facing) < -.999f));
            if (!matches) return Fail(ConveyorFailure.PortMismatch);
            if (request.Module == ConveyorModule.Overpass) pieces.AddRange(ConveyorGeometry.Overpass(placement.Pivot, placement.Orientation));
            else pieces.Add(placement);
            return CompletePlan(entities, owner, request, storage, pieces,
                placement.Pivot.Y - request.Tier * 2, default, existing);
        }
        ConveyorPiece old=default;
        if (request.Edit!=ConveyorEdit.Build)
        {
            if (!entities.TryGetComponent(request.Target,out old)) return Fail(ConveyorFailure.TargetUnavailable);
            if (old.Owner!=owner) return Fail(ConveyorFailure.NotOwned);
            if(request.Edit==ConveyorEdit.Replace)
                request=ResolveReplacementRequest(request,old);
            if (TryGetAdapterReattachment(entities, owner, request, old, out _))
            {
                double cost = ConveyorGeometry.Cost(old.Placement);
                return new(_stock.GetAvailableQuantity(storage.InventoryId, ResourceIds.Steel) < cost ?
                    ConveyorFailure.InsufficientStock : ConveyorFailure.None,
                    Array.AsReadOnly(new[] { old.Placement }), cost);
            }
            // Replacement is non-destructive with respect to cargo. Deliberate
            // removal instead inspects authoritative available stock at this tick.
            if (request.Edit == ConveyorEdit.Replace &&
                (!double.IsFinite(old.BufferedQuantity) || old.BufferedQuantity != 0 ||
                 entities.TryGetComponent(request.Target, out ConveyorFlowState replaced) &&
                 _stock.Contains(replaced.Buffer) && _stock.GetResourceQuantities(replaced.Buffer)
                     .Any(r => r.Quantity != 0 || r.ReservedQuantity != 0)))
                return Fail(ConveyorFailure.Occupied);
            ConveyorCargoRecovery[] cargo = [];
            if (request.Edit == ConveyorEdit.Remove)
            {
                if (entities.TryGetComponent(request.Target, out ConveyorFlowState held) &&
                    _stock.Contains(held.Buffer))
                {
                    InventoryResourceQuantity[] actual = _stock.GetResourceQuantities(held.Buffer);
                    // Outstanding reservations belong to another transaction. Never
                    // salvage, consume or discard those commitments implicitly.
                    if (actual.Any(r => r.ReservedQuantity != 0 ||
                        !ConveyorSimulationSystem.Eligible(r.ResourceId)))
                        return Fail(ConveyorFailure.Occupied);
                    cargo = actual.Where(r => r.Quantity > 0)
                        .Select(r => new ConveyorCargoRecovery(r.ResourceId,
                            r.Quantity, r.Quantity * 0.5, r.Quantity * 0.5)).ToArray();
                    if (cargo.Length != 0 &&
                        !ConveyorSalvageStock.CanAccept(entities, _stock, owner, cargo))
                        return Fail(ConveyorFailure.RecoveryFull);
                }
                else if (!old.Placement.IsSupport && old.Placement.Module != ConveyorModule.Endcap)
                    return Fail(ConveyorFailure.Occupied);
            }
            if (request.Edit==ConveyorEdit.Repair &&
                (!entities.TryGetComponent(request.Target,out ForgeLine.Combat.HealthState repairHealth) ||
                 repairHealth.Fraction>=1))return Fail(ConveyorFailure.InvalidRequest);
            if (old.Placement.IsSupport && request.Edit==ConveyorEdit.Remove &&
                Capture(entities,owner).Any(p=>!p.Piece.Placement.IsSupport &&
                    p.Piece.Placement.Sockets.Any(s=>Vector2.Distance(new(s.Position.X,s.Position.Z),
                        new(old.Placement.Pivot.X,old.Placement.Pivot.Z))<.1f)))return Fail(ConveyorFailure.Obstructed);
            if (request.Edit is ConveyorEdit.Remove or ConveyorEdit.Repair)
            {
                double cost=request.Edit==ConveyorEdit.Repair?
                    entities.TryGetComponent(request.Target,out ForgeLine.Combat.HealthState repairState) && repairState.IsDepleted?old.PaidSteel:4:0;
                return new(_stock.GetAvailableQuantity(storage.InventoryId,ResourceIds.Steel)<cost?
                    ConveyorFailure.InsufficientStock:ConveyorFailure.None,Array.AsReadOnly(new[]{old.Placement}),cost)
                { CargoRecovery = cargo };
            }
        }
        var existingPieces=Capture(entities,owner);
        bool socketStart=existingPieces.Any(p=>p.Piece.Placement.Sockets.Any(s=>!s.Input &&
            Vector3.DistanceSquared(s.Position,request.Start+Vector3.UnitY*5.8f)<.0004f));
        bool socketEnd=existingPieces.Any(p=>p.Piece.Placement.Sockets.Any(s=>s.Input &&
            Vector3.DistanceSquared(s.Position,request.End+Vector3.UnitY*5.8f)<.0004f));
        Vector3 start=request.SourcePortBuilding.IsValid || socketStart?request.Start:ConveyorGeometry.Snap(request.Start);
        Vector3 end=request.DestinationPortBuilding.IsValid || socketEnd?request.End:start+ConveyorGeometry.Snap(request.End-start);
        if(request.DestinationPortBuilding.IsValid && !request.SourcePortBuilding.IsValid)
            start=end+ConveyorGeometry.Snap(request.Start-end);
        if (!_terrain.TrySampleHeight(start.X,start.Z,out float ground)) return Fail(ConveyorFailure.Terrain);
        if(request.SourcePortBuilding.IsValid || socketStart)ground=request.Start.Y-request.Tier*2;
        if((request.DestinationPortBuilding.IsValid || socketEnd) && MathF.Abs(request.End.Y-ground-request.Tier*2)>.02f)
            return Fail(ConveyorFailure.PortMismatch);
        start.Y=ground+request.Tier*2; end.Y=start.Y;
        if (request.Edit==ConveyorEdit.Replace)
        {
            start=old.Placement.Pivot; end=start;
            if (ConveyorGeometry.Sockets(old.Placement.Module).Length!=ConveyorGeometry.Sockets(request.Module).Length)
                return Fail(ConveyorFailure.PortMismatch);
        }
        if (request.Module==ConveyorModule.Route)
        {
            Vector3 delta=end-start;
            if(MathF.Abs(delta.X/4-MathF.Round(delta.X/4))>.005f || MathF.Abs(delta.Z/4-MathF.Round(delta.Z/4))>.005f)
                return Fail(ConveyorFailure.PortMismatch);
            ConveyorPlacement? destinationAdapter=null;
            if(request.SourcePortBuilding.IsValid)
            {
                var port=CapturePorts(entities,owner).FirstOrDefault(p=>p.Building==request.SourcePortBuilding && !p.Input &&
                    (!p.Adaptable || p.Status==ConveyorPortState.Free) &&
                    Vector3.DistanceSquared(p.Position,start+Vector3.UnitY*5.8f)<.0004f);
                if(!port.Building.IsValid)return Fail(ConveyorFailure.PortMismatch);
                pieces.Add(new(ConveyorModule.InputAdapter,start+port.Facing*2,ConveyorGeometry.Facing(port.Facing),request.Tier));
                start+=port.Facing*4;
            }
            if(request.DestinationPortBuilding.IsValid)
            {
                var port=CapturePorts(entities,owner).FirstOrDefault(p=>p.Building==request.DestinationPortBuilding && p.Input &&
                    (!p.Adaptable || p.Status==ConveyorPortState.Free) &&
                    Vector3.DistanceSquared(p.Position,end+Vector3.UnitY*5.8f)<.0004f);
                if(!port.Building.IsValid)return Fail(ConveyorFailure.PortMismatch);
                destinationAdapter=new(ConveyorModule.OutputAdapter,end+port.Facing*2,ConveyorGeometry.Facing(-port.Facing),request.Tier);
                end+=port.Facing*4;
            }
            float length=MathF.Abs(end.X-start.X)+MathF.Abs(end.Z-start.Z);
            if (length<4 || length>256) return Fail(ConveyorFailure.RouteTooLong);
            Vector3 corner=request.XFirst?new(end.X,start.Y,start.Z):new(start.X,start.Y,end.Z);
            if (corner==start || corner==end) Straight(start,end);
            else
            {
                Vector3 incoming=Vector3.Normalize(corner-start),outgoing=Vector3.Normalize(end-corner);
                Vector3 a=corner-incoming*4,b=corner+outgoing*4;
                Straight(start,a);
                BuildingOrientation facing=ConveyorGeometry.Facing(incoming);
                Quaternion rotation=BuildingFootprint.GetRotation(facing);
                bool left=Vector3.Dot(Vector3.Transform(Vector3.UnitX,rotation),outgoing)>0;
                ConveyorModule kind=left?ConveyorModule.CurveLeft:ConveyorModule.CurveRight;
                Vector3 localInput=ConveyorGeometry.Sockets(kind)[0].Position-new Vector3(0,5.8f,0);
                pieces.Add(new(kind,a-Vector3.Transform(localInput,rotation),facing,request.Tier));
                Straight(b,end);
            }
            if(destinationAdapter.HasValue)pieces.Add(destinationAdapter.Value);
        }
        else if (request.Module == ConveyorModule.Overpass)
            pieces.AddRange(ConveyorGeometry.Overpass(start, request.Orientation));
        else
        {
            ConveyorModule module=request.Module==ConveyorModule.Support && request.Tier==1?ConveyorModule.RaisedSupport:request.Module;
            if(module==ConveyorModule.Endcap && request.Edit==ConveyorEdit.Build)
            {
                ConveyorSocket? mate=null;
                foreach(var piece in existingPieces)
                    foreach(var socket in piece.Piece.Placement.Sockets)
                        if(!socket.Input && Vector2.Distance(new(socket.Position.X,socket.Position.Z),new(request.Start.X,request.Start.Z))<2)
                        {mate=socket;break;}
                if(!mate.HasValue)return Fail(ConveyorFailure.PortMismatch);
                BuildingOrientation facing=ConveyorGeometry.Facing(mate.Value.Facing);
                Vector3 pivot=mate.Value.Position-Vector3.Transform(ConveyorGeometry.Sockets(module)[0].Position,
                    BuildingFootprint.GetRotation(facing));
                pieces.Add(new(module,pivot,facing,Math.Clamp((int)MathF.Round((pivot.Y-ground)/2),0,1)));
            }
            else
            pieces.Add(new(module,module is ConveyorModule.Support or ConveyorModule.RaisedSupport?
                start with {Y=ground}:start,request.Orientation,module==ConveyorModule.RaisedSupport?1:request.Tier,
                SpanMeters:request.Edit==ConveyorEdit.Replace && module==old.Placement.Module?old.Placement.SpanMeters:0,
                RiseMeters:request.Edit==ConveyorEdit.Replace && module==old.Placement.Module?old.Placement.RiseMeters:0,
                SupportHeightMeters:request.Edit==ConveyorEdit.Replace && module==old.Placement.Module?old.Placement.SupportHeightMeters:0));
        }
        return CompletePlan(entities, owner, request, storage, pieces, ground, old, existingPieces);

        void Straight(Vector3 a,Vector3 b)
        {
            float distance=Vector3.Distance(a,b); if (distance<.1f)return;
            Vector3 direction=Vector3.Normalize(b-a);
            while(distance>1)
            {
                float span=distance>=8?8:4;
                pieces.Add(new(span==8?ConveyorModule.Standard:ConveyorModule.ShortSegment,a+direction*(span/2),ConveyorGeometry.Facing(direction),request.Tier));
                a+=direction*span;distance-=span;
            }
        }
    }

    private ConveyorPlan PreviewAutomatic(EntityRegistry entities, PlayerId owner,
        ConveyorBuildRequest request, InventoryStorage storage, IReadOnlyList<ConveyorBuildingPort>? designPorts = null)
    {
        ConveyorPlan Fail(ConveyorFailure failure) => new(failure, Array.Empty<ConveyorPlacement>(), 0);
        if (request.Target.IsValid) return Fail(ConveyorFailure.InvalidRequest);
        IReadOnlyList<ConveyorBuildingPort> ports = designPorts ?? CapturePorts(entities, owner);
        IReadOnlyList<ConveyorOwnedPiece> existing = Capture(entities, owner);
        Vector3 start = request.Start, end = request.End;
        Vector3? departure = null, arrival = null;
        ConveyorBuildingPort? sourcePort = null, destinationPort = null;
        bool startSocket = false, endSocket = false;

        if (designPorts is not null)
        {
            sourcePort = ports[0]; departure = Cardinal(ports[0].Facing);
        }
        else if (request.SourcePortBuilding.IsValid)
        {
            ConveyorBuildingPort port = ports.FirstOrDefault(port =>
                port.Building == request.SourcePortBuilding && !port.Input &&
                (!port.Adaptable || port.Status == ConveyorPortState.Free) &&
                Vector3.DistanceSquared(port.Position, start + Vector3.UnitY * 5.8f) < .0004f);
            if (!port.Building.IsValid || !request.AutomaticHeight && request.Tier!=0 ||
                Vector3.DistanceSquared(port.Position, start + Vector3.UnitY * 5.8f) >= .0004f)
                return Fail(ConveyorFailure.PortMismatch);
            sourcePort = port;
            departure = Cardinal(port.Facing);
        }
        else if (!ResolveSocket(start, false, out startSocket, out departure))
            return Fail(ConveyorFailure.PortMismatch);

        if (designPorts is not null)
        {
            destinationPort = ports[1]; arrival = Cardinal(-ports[1].Facing);
        }
        else if (request.DestinationPortBuilding.IsValid)
        {
            ConveyorBuildingPort port = ports.FirstOrDefault(port =>
                port.Building == request.DestinationPortBuilding && port.Input &&
                (!port.Adaptable || port.Status == ConveyorPortState.Free) &&
                Vector3.DistanceSquared(port.Position, end + Vector3.UnitY * 5.8f) < .0004f);
            if (!port.Building.IsValid || !request.AutomaticHeight && request.Tier!=0 ||
                Vector3.DistanceSquared(port.Position, end + Vector3.UnitY * 5.8f) >= .0004f)
                return Fail(ConveyorFailure.PortMismatch);
            destinationPort = port;
            arrival = Cardinal(-port.Facing);
        }
        else if (!ResolveSocket(end, true, out endSocket, out arrival))
            return Fail(ConveyorFailure.PortMismatch);

        if (sourcePort is null && !startSocket) start = ConveyorGeometry.Snap(start);
        if (destinationPort is null && !endSocket) end = start + ConveyorGeometry.Snap(end - start);
        if (!_terrain.TrySampleHeight(start.X, start.Z, out float ground)) return Fail(ConveyorFailure.Terrain);
        if (sourcePort is not null || startSocket) ground = start.Y - request.Tier * 2;
        if(!request.AutomaticHeight && (destinationPort is not null || endSocket) &&
            MathF.Abs(end.Y-ground-request.Tier*2)>.02f)return Fail(ConveyorFailure.PortMismatch);
        start.Y = !request.AutomaticHeight || (sourcePort is null && !startSocket) ? ground+request.Tier*2 : start.Y;
        if(!request.AutomaticHeight)end.Y=start.Y;
        else if(destinationPort is null && !endSocket)
        {
            if(!_terrain.TrySampleHeight(end.X,end.Z,out float endGround))return Fail(ConveyorFailure.Terrain);
            end.Y=endGround+request.Tier*2;
        }
        float startHeight=start.Y,endHeight=end.Y;

        ConveyorPlacement? sourceAdapter = null, destinationAdapter = null;
        if (sourcePort is { } output)
        {
            sourceAdapter = new(ConveyorModule.InputAdapter, start + output.Facing * 2,
                ConveyorGeometry.Facing(output.Facing), request.Tier);
            start = sourceAdapter.Value.Sockets[^1].Position - Vector3.UnitY * 5.8f;
        }
        if (destinationPort is { } input)
        {
            destinationAdapter = new(ConveyorModule.OutputAdapter, end + input.Facing * 2,
                ConveyorGeometry.Facing(-input.Facing), request.Tier);
            end = destinationAdapter.Value.Sockets[0].Position - Vector3.UnitY * 5.8f;
        }
        float adapterLength = (sourceAdapter.HasValue ? 4 : 0) + (destinationAdapter.HasValue ? 4 : 0);
        if (MathF.Abs(end.X - start.X) + MathF.Abs(end.Z - start.Z) + adapterLength > 256)
            return Fail(ConveyorFailure.RouteTooLong);
        var candidates = ConveyorAutomaticRouter.CreateCandidates(start, end with {Y=start.Y}, departure, arrival,
            request.Tier, request.XFirst, 256 - adapterLength);
        ConveyorPlan? firstFailure = null, affordableFailure = null;
        foreach (ConveyorRouteCandidate candidate in candidates)
        {
            if (candidate.LengthMeters + adapterLength < 4 - .001f) continue;
            var pieces = new List<ConveyorPlacement>();
            if (sourceAdapter.HasValue) pieces.Add(sourceAdapter.Value);
            pieces.AddRange(candidate.Pieces);
            if (destinationAdapter.HasValue) pieces.Add(destinationAdapter.Value);
            if(request.AutomaticHeight)
            {
                if(!ConveyorTerrainRouter.TryFit(pieces,startHeight,endHeight,_terrain,out var fitted))
                {firstFailure??=Fail(ConveyorFailure.Terrain);continue;}
                pieces=fitted;
            }
            if (pieces.Count == 0 || !ConveyorAutomaticRouter.IsConnected(pieces) ||
                ConveyorAutomaticRouter.HasSelfIntersection(pieces)) continue;
            ConveyorPlan plan = CompletePlan(entities, owner, request, storage, pieces, ground, default, existing, designPorts);
            if (plan.IsValid) return plan;
            firstFailure ??= plan;
            if (plan.Failure == ConveyorFailure.InsufficientStock) affordableFailure ??= plan;
        }
        return affordableFailure ?? firstFailure ?? Fail(ConveyorFailure.PortMismatch);

        bool ResolveSocket(Vector3 point, bool input, out bool found, out Vector3? direction)
        {
            found = false;
            direction = null;
            foreach (ConveyorOwnedPiece row in existing)
            {
                if (row.Status == ConveyorLinkStatus.Destroyed) continue;
                foreach (ConveyorSocket socket in row.Piece.Placement.Sockets)
                {
                    if (socket.Input != input ||
                        Vector3.DistanceSquared(socket.Position, point + Vector3.UnitY * 5.8f) >= .0004f) continue;
                    if (found) return false;
                    found = true;
                    direction = Cardinal(input ? -socket.Facing : socket.Facing);
                }
            }
            return true;
        }

        static Vector3 Cardinal(Vector3 direction) => MathF.Abs(direction.X) > MathF.Abs(direction.Z)
            ? direction.X > 0 ? Vector3.UnitX : -Vector3.UnitX
            : direction.Z > 0 ? Vector3.UnitZ : -Vector3.UnitZ;
    }

    private ConveyorPlan CompletePlan(EntityRegistry entities, PlayerId owner,
        ConveyorBuildRequest request, InventoryStorage storage, List<ConveyorPlacement> pieces,
        float ground, ConveyorPiece old, IReadOnlyList<ConveyorOwnedPiece> existingPieces,
        IReadOnlyList<ConveyorBuildingPort>? designPorts = null)
    {
        Vector3? failurePosition = null;
        ConveyorObstruction obstruction = ConveyorObstruction.None;
        ConveyorPlan Fail(ConveyorFailure failure, double cost = 0) =>
            new(failure, Array.AsReadOnly(pieces.ToArray()), cost)
            { FailurePosition = failurePosition, Obstruction = obstruction };
        if (pieces.Any(piece => !float.IsFinite(piece.RiseMeters) || !float.IsFinite(piece.SupportHeightMeters) ||
            piece.SupportHeightMeters < 0 || piece.SupportHeightMeters > ConveyorTerrainRouter.MaximumSupportHeight ||
            piece.SupportHeightMeters > 0 && !piece.IsSupport ||
            piece.RiseMeters != 0 && (piece.Module is not (ConveyorModule.Standard or ConveyorModule.ShortSegment or
                ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter) ||
                MathF.Abs(piece.RiseMeters)>piece.HorizontalSpan*ConveyorTerrainRouter.MaximumGrade+.001f) ||
            !float.IsFinite(piece.SpanMeters) || piece.SpanMeters != 0 &&
            (piece.Module != ConveyorModule.ShortSegment ||
             piece.SpanMeters < ConveyorAutomaticRouter.MinimumSpanMeters || piece.SpanMeters > 4)))
            return Fail(ConveyorFailure.InvalidRequest);
        IReadOnlyList<ConveyorBuildingPort> ports = designPorts ?? CapturePorts(entities, owner);
        bool localSupports=request.AutomaticHeight || old.Placement.RiseMeters!=0 ||
            old.Placement.SupportHeightMeters>0;
        // Each footing reaches the local ground, including independently elevated building foundations.
        foreach (ConveyorPlacement part in pieces.ToArray())
        {
            if (part.IsSupport || part.Module==ConveyorModule.Endcap) continue;
            foreach (ConveyorSocket socket in part.Sockets)
            {
                Vector3 p=socket.Position-Vector3.UnitY*5.8f;
                int tier=(int)MathF.Round((p.Y-ground)/2);
                Vector3 foot=new(p.X,ground,p.Z);
                if(ports.Any(port=>
                    (port.Building==request.SourcePortBuilding || port.Building==request.DestinationPortBuilding) &&
                    Vector3.DistanceSquared(port.Position,socket.Position)<.0004f))continue;
                if (pieces.Any(v=>v.IsSupport && Vector3.DistanceSquared(v.Pivot,foot)<.01f)) continue;
                float supportHeight=0;
                if(localSupports)
                {
                    if(!_terrain.TrySampleHeight(p.X,p.Z,out float localGround))return Fail(ConveyorFailure.Terrain);
                    foot.Y=localGround;
                    supportHeight=p.Y+5.22f-localGround;
                    if(!float.IsFinite(supportHeight) || supportHeight<=0 || supportHeight>ConveyorTerrainRouter.MaximumSupportHeight)
                        return Fail(ConveyorFailure.Terrain);
                    tier=supportHeight>5.221f?1:0;
                    float nominal=tier==0?5.22f:7.22f;
                    if(MathF.Abs(supportHeight-nominal)<.001f)supportHeight=0;
                    if(pieces.Any(v=>v.IsSupport && Vector3.DistanceSquared(v.Pivot,foot)<.01f))continue;
                }
                // Skip road footings; only a bounded 16m support interval may bridge them.
                if(!OnRoad(foot))pieces.Add(new(tier==0?ConveyorModule.Support:ConveyorModule.RaisedSupport,foot,
                    BuildingOrientation.North,tier,SupportHeightMeters:supportHeight));
            }
        }
        double price=pieces.Sum(ConveyorGeometry.Cost);
        if(entities.GetComponentCount<ConveyorPiece>()+pieces.Count>4096)return Fail(ConveyorFailure.LimitReached,price);
        if (request.Edit==ConveyorEdit.Replace)
        {
            ConveyorPlacement replacement=pieces[0];
            ConveyorSocket[] before=old.Placement.Sockets,after=replacement.Sockets;
            if (before.Where((s,i)=>Vector3.DistanceSquared(s.Position,after[i].Position)>.0004f ||
                Vector3.Dot(s.Facing,after[i].Facing)<.999f || s.Input!=after[i].Input).Any()) return Fail(ConveyorFailure.PortMismatch);
        }
        for(int i=0;i<pieces.Count;i++)
        {
            ConveyorPlacement part=pieces[i]; AxisAlignedBounds bounds=part.Bounds;
            failurePosition=bounds.Center;
            if (!_area.IsBuildable(owner,bounds)) return Fail(ConveyorFailure.OutsideBuildArea,price);
            // Dense bounded samples across actual footprint, including span underside.
            int xSteps=Math.Max(1,(int)MathF.Ceiling((bounds.Maximum.X-bounds.Minimum.X)/.5f));
            int zSteps=Math.Max(1,(int)MathF.Ceiling((bounds.Maximum.Z-bounds.Minimum.Z)/.5f));
            for(int xi=0;xi<=xSteps;xi++)
                for(int zi=0;zi<=zSteps;zi++)
                {
                    float x=float.Lerp(bounds.Minimum.X,bounds.Maximum.X,(float)xi/xSteps);
                    float z=float.Lerp(bounds.Minimum.Z,bounds.Maximum.Z,(float)zi/zSteps);
                    if (!_terrain.TrySampleHeight(x,z,out float height)) return Fail(ConveyorFailure.Terrain,price);
                    if (part.IsSupport?MathF.Abs(height-part.Pivot.Y)>.2f:
                        height>(part.RiseMeters==0?bounds.Minimum.Y:part.UndersideAt(x,z))-5)
                        return Fail(ConveyorFailure.Terrain,price);
                }
            if (part.IsSupport && OnRoad(part.Pivot)) return Fail(ConveyorFailure.SupportOnRoad,price);
            if(part.IsSupport)
            {
                foreach(EntityId occupied in entities.Query<VehicleDockSlot>())
                    if(entities.GetComponent<VehicleDockSlot>(occupied).Pose.IntersectsLane(bounds))
                    {
                        obstruction=ConveyorObstruction.LoadingLane;
                        return Fail(ConveyorFailure.Obstructed,price);
                    }
                foreach(EntityId building in entities.Query<BuildingLoadingDock,WorldTransform>())
                {
                    var design=WorldTransform.Identity with {Scale=new(3.4f,2.5f,6.8f)};
                    if(VehicleDockPose.TryCreate(entities.GetComponent<BuildingLoadingDock>(building),
                        entities.GetComponent<WorldTransform>(building),design,out var pose) && pose.IntersectsLane(bounds))
                    {
                        obstruction=ConveyorObstruction.LoadingLane;
                        return Fail(ConveyorFailure.Obstructed,price);
                    }
                }
            }
            _spatial.QueryAabb(bounds,_query,order:SpatialQueryOrder.StableEntityId);
            foreach(EntityId entity in _query.Results)
            {
                if (entity==request.Target) continue;
                if (entities.TryGetComponent(entity,out ConveyorPiece existing))
                {
                    if (part.IsSupport && existing.Placement.IsSupport && existing.Owner==owner &&
                        Vector3.DistanceSquared(part.Pivot,existing.Placement.Pivot)<.01f && part.Module==existing.Placement.Module &&
                        MathF.Abs(part.Bounds.Maximum.Y-existing.Placement.Bounds.Maximum.Y)<.01f)
                    { pieces.RemoveAt(i--); price-=ConveyorGeometry.Cost(part); break; }
                    // A graded carrier's conservative AABB includes the space below its high end.
                    // Its own exact endpoint mount is a valid contact, not an obstruction.
                    if(existing.Owner==owner && (part.RiseMeters!=0 || part.SupportHeightMeters>0 ||
                        existing.Placement.RiseMeters!=0 || existing.Placement.SupportHeightMeters>0) &&
                        (Mounts(part,existing.Placement) || Mounts(existing.Placement,part)))continue;
                    foreach(var a in part.Sockets)
                        foreach(var b in existing.Placement.Sockets)
                            if(Vector3.DistanceSquared(a.Position,b.Position)<.0004f &&
                                (existing.Owner!=owner || a.Input==b.Input || Vector3.Dot(a.Facing,b.Facing)>-.999f))
                                return Fail(ConveyorFailure.PortMismatch,price);
                    if(!ConveyorGeometry.Overlap(bounds,existing.Placement.Bounds))continue;
                    bool mates=part.Sockets.Any(a=>existing.Placement.Sockets.Any(b=>
                        a.Input!=b.Input && Vector3.DistanceSquared(a.Position,b.Position)<.0004f && Vector3.Dot(a.Facing,b.Facing)<-.999f));
                    if (existing.Owner==owner && mates) continue;
                }
                if (entity==request.SourcePortBuilding || entity==request.DestinationPortBuilding)
                {
                    // Only the explicit terminal adapter may touch its own building.
                    if(part.Module is ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter)continue;
                }
                obstruction=entities.HasComponent<ConveyorPiece>(entity)?ConveyorObstruction.Conveyor:
                    entities.HasComponent<CompletedBuilding>(entity)?ConveyorObstruction.Building:ConveyorObstruction.VehicleOrObject;
                return Fail(ConveyorFailure.Obstructed,price);
            }
        }
        // Building endpoints require explicit authored sockets, never inferred proximity.
        foreach(var part in pieces.Where(p=>!p.IsSupport && p.Module!=ConveyorModule.Endcap))
            if(!pieces.Any(s=>CanSupport(s,part)) && !existingPieces.Any(s=>CanSupport(s.Piece.Placement,part)))
            {
                failurePosition=part.Bounds.Center;
                return Fail(ConveyorFailure.SupportOnRoad,price);
            }
        failurePosition=null;
        if (!PortMatches(request.SourcePortBuilding,false,true) || !PortMatches(request.DestinationPortBuilding,true,false))
            return Fail(ConveyorFailure.PortMismatch,price);
        if (_stock.GetAvailableQuantity(storage.InventoryId,ResourceIds.Steel)<price)
            return Fail(ConveyorFailure.InsufficientStock,price);
        return new(ConveyorFailure.None,Array.AsReadOnly(pieces.ToArray()),price);

        static bool CanSupport(ConveyorPlacement foot,ConveyorPlacement carrier)=>foot.IsSupport &&
            foot.Bounds.Maximum.Y>=carrier.Bounds.Minimum.Y-.03f && foot.Bounds.Maximum.Y<=carrier.Bounds.Maximum.Y &&
            Vector2.Distance(new(foot.Pivot.X,foot.Pivot.Z),new(carrier.Bounds.Center.X,carrier.Bounds.Center.Z))<=8;

        static bool Mounts(ConveyorPlacement carrier,ConveyorPlacement foot)=>!carrier.IsSupport && foot.IsSupport &&
            carrier.Sockets.Any(socket=>Vector2.Distance(new(socket.Position.X,socket.Position.Z),
                new(foot.Pivot.X,foot.Pivot.Z))<.02f && MathF.Abs(foot.Bounds.Maximum.Y-(socket.Position.Y-.58f))<.02f);

        bool PortMatches(EntityId building,bool input,bool first)
        {
            if (!building.IsValid) return true;
            var carriers=pieces.Where(p=>!p.IsSupport).ToArray();
            if(carriers.Length==0)return false;
            ConveyorSocket socket=first?carriers[0].Sockets[0]:carriers[^1].Sockets[^1];
            return ports.Any(port=>port.Building==building && port.Input==input &&
                (!port.Adaptable || port.Status==ConveyorPortState.Free ||
                 request.Edit==ConveyorEdit.Replace && request.Target.IsValid &&
                 old.Attachment==building &&
                 port.Status==(input ? ConveyorPortState.ConnectedInput : ConveyorPortState.ConnectedOutput)) &&
                Vector3.DistanceSquared(port.Position,socket.Position)<.0004f &&
                Vector3.Dot(port.Facing,socket.Facing)<-.999f);
        }
    }

    private static bool TryGetAdapterReattachment(
        EntityRegistry entities, PlayerId owner, ConveyorBuildRequest request,
        ConveyorPiece existing, out EntityId attachment)
    {
        attachment = EntityId.Invalid;
        if (request.Edit != ConveyorEdit.Replace || request.Module != existing.Placement.Module ||
            request.Module is not (ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter) ||
            !existing.Attachment.IsValid || entities.IsAlive(existing.Attachment))
            return false;

        bool output = request.Module == ConveyorModule.OutputAdapter;
        EntityId replacement = output ? request.DestinationPortBuilding : request.SourcePortBuilding;
        EntityId opposite = output ? request.SourcePortBuilding : request.DestinationPortBuilding;
        if (!replacement.IsValid || opposite.IsValid || replacement == existing.Attachment)
            return false;

        ConveyorSocket[] sockets = existing.Placement.Sockets;
        ConveyorSocket terminal = output ? sockets[^1] : sockets[0];
        foreach (ConveyorBuildingPort port in CapturePorts(entities, owner))
        {
            if (port.Building != replacement || port.Input != output ||
                Vector3.DistanceSquared(port.Position, terminal.Position) >= .0004f ||
                Vector3.Dot(port.Facing, terminal.Facing) >= -.999f)
                continue;
            attachment = replacement;
            return true;
        }
        return false;
    }

    private static ConveyorBuildRequest ResolveReplacementRequest(ConveyorBuildRequest request,ConveyorPiece old) =>
        request with
        {
            Tier=old.Placement.Tier,
            Orientation=old.Placement.Orientation,
            SourcePortBuilding=request.Module==ConveyorModule.InputAdapter && old.Placement.Module==ConveyorModule.InputAdapter &&
                !request.SourcePortBuilding.IsValid?old.Attachment:request.SourcePortBuilding,
            DestinationPortBuilding=request.Module==ConveyorModule.OutputAdapter && old.Placement.Module==ConveyorModule.OutputAdapter &&
                !request.DestinationPortBuilding.IsValid?old.Attachment:request.DestinationPortBuilding
        };
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private bool OnRoad(Vector3 p)
    {
        foreach(var edge in _battlefield.RoadEdges)
        {
            Vector3 a=_battlefield.RoadNodes.First(n=>n.Key==edge.SourceNodeKey).Position;
            Vector3 b=_battlefield.RoadNodes.First(n=>n.Key==edge.DestinationNodeKey).Position;
            Vector2 d=new(b.X-a.X,b.Z-a.Z),q=new(p.X-a.X,p.Z-a.Z);
            float t=Math.Clamp(Vector2.Dot(q,d)/MathF.Max(d.LengthSquared(),.01f),0,1);
            if ((q-d*t).Length()<5.4f)return true;
        }
        return false;
    }
    internal ConveyorFailure Apply(SimulationContext context,PlayerId owner,ConveyorBuildRequest request,EntityId payer) =>
        Apply(context, owner, request, payer, out _);

    internal ConveyorFailure Apply(SimulationContext context,PlayerId owner,ConveyorBuildRequest request,
        EntityId payer, out ConveyorCargoRecovery[] recovered)
    {
        recovered = [];
        bool running=false;
        foreach(EntityId e in context.Entities.Query<MatchState>())
            running |= context.Entities.GetComponent<MatchState>(e).Lifecycle==MatchLifecyclePhase.Running;
        if (!running) return ConveyorFailure.MatchUnavailable;
        ConveyorPlan plan=Preview(context.Entities,owner,request,payer);
        if (!plan.IsValid)return plan.Failure;
        InventoryId inventory=context.Entities.GetComponent<InventoryStorage>(payer).InventoryId;
        if (request.Edit==ConveyorEdit.Remove)
        {
            // Transfer the recoverable half before deleting the authoritative
            // buffer. The remaining half is deliberately lost; construction
            // steel is never refunded by this operation.
            if (plan.CargoRecovery.Length != 0)
            {
                if (!context.Entities.TryGetComponent(request.Target, out ConveyorFlowState flow) ||
                    !ConveyorSalvageStock.Deposit(context.Entities, _stock, owner,
                        flow.Buffer, plan.CargoRecovery))
                    return ConveyorFailure.RecoveryFull;
                recovered = plan.CargoRecovery;
            }
            DestroyEmptyBuffer(request.Target);
            _spatial.Remove(request.Target);context.Entities.DestroyEntity(request.Target);
        }
        else if(request.Edit==ConveyorEdit.Repair)
        {
            if (!_stock.Remove(inventory,ResourceIds.Steel,plan.Steel).Succeeded)return ConveyorFailure.InsufficientStock;
            if (context.Entities.TryGetComponent(request.Target,out ForgeLine.Combat.HealthState health))
                context.Entities.SetComponent(request.Target,ForgeLine.Combat.HealthState.Full(health.Maximum));
            if (context.Entities.TryGetComponent(request.Target,out PowerConsumer consumer))
                context.Entities.SetComponent(request.Target,consumer.WithEnabled(true));
        }
        else
        {
            if (!_stock.Remove(inventory,ResourceIds.Steel,plan.Steel).Succeeded)return ConveyorFailure.InsufficientStock;
            if (context.Entities.TryGetComponent(request.Target, out ConveyorPiece adapter) &&
                TryGetAdapterReattachment(context.Entities, owner, request, adapter, out EntityId replacementAttachment))
            {
                // Explicit replacement of a dead full-generation endpoint keeps the paid carrier,
                // its buffer, damage and supports. Cargo is never discarded to reconnect a line.
                context.Entities.SetComponent(request.Target, adapter with { Attachment = replacementAttachment });
                return ConveyorFailure.None;
            }
            if(request.Edit==ConveyorEdit.Replace)
            {
                request=ResolveReplacementRequest(request,context.Entities.GetComponent<ConveyorPiece>(request.Target));
                DestroyEmptyBuffer(request.Target);
                _spatial.Remove(request.Target);context.Entities.DestroyEntity(request.Target);
            }
            foreach(ConveyorPlacement part in plan.Pieces)
            {
                EntityId e=context.Entities.CreateEntity();
                var transform=new WorldTransform(part.Bounds.Center,part.Rotation,new Vector3(18) * part.LocalScale);
                var presence=new SpatialPresence((part.Bounds.Maximum-part.Bounds.Minimum)/2,
                    new SpatialEntryMetadata(owner.Value,(ulong)ControllableEntityCategory.Logistics,SpatialMobility.Static));
                EntityId attachment=part.Module==ConveyorModule.InputAdapter?request.SourcePortBuilding:
                    part.Module==ConveyorModule.OutputAdapter?request.DestinationPortBuilding:EntityId.Invalid;
                context.Entities.AddComponent(e,new ConveyorPiece(owner,part,ConveyorGeometry.Cost(part),attachment,
                    AutomaticSupport:part.IsSupport && request.Module is not (ConveyorModule.Support or ConveyorModule.RaisedSupport)));
                if (!part.IsSupport && part.Module!=ConveyorModule.Endcap)
                {
                    double capacity=ConveyorSimulationSystem.Capacity(part.Module);
                    InventoryId buffer=_stock.CreateInventory(new InventorySpecification(capacity,ConveyorSimulationSystem.EligibleResources));
                    context.Entities.AddComponent(e,new ConveyorFlowState(buffer,capacity));
                    context.Entities.AddComponent(e,new PowerConsumer(part.Module is ConveyorModule.Merger or ConveyorModule.Splitter?4:2));
                    if(context.Entities.TryGetComponent(payer,out PowerNetworkMembership membership))
                        context.Entities.AddComponent(e,membership);
                }
                context.Entities.AddComponent(e,transform);context.Entities.AddComponent(e,presence);
                context.Entities.AddComponent(e,new ControllableEntity(owner,ControllableEntityCategory.Logistics));
                context.Entities.AddComponent(e,new VisualIdentity(1));
                context.Entities.AddComponent(e,ForgeLine.Combat.HealthState.Full(200));
                context.Entities.AddComponent(e,new IntelligenceSignature(new FactionId(checked((uint)owner.Value)),
                    0x9000_0000u | (uint)part.Module));
                _spatial.Upsert(presence.CreateEntry(e,transform));
            }
        }
        // Auto feet persist while any remaining carrier uses the attachment.
        var survivors=Capture(context.Entities,owner);
        foreach(var support in survivors.Where(p=>p.Piece.AutomaticSupport))
        {
            Vector3 foot=support.Piece.Placement.Pivot;
            if(survivors.Any(p=>!p.Piece.Placement.IsSupport && p.Piece.Placement.Sockets.Any(s=>
                Vector2.Distance(new(s.Position.X,s.Position.Z),new(foot.X,foot.Z))<.1f)))continue;
            _spatial.Remove(support.Entity);context.Entities.DestroyEntity(support.Entity);
        }
        void DestroyEmptyBuffer(EntityId target)
        {
            if (context.Entities.TryGetComponent(target,out ConveyorFlowState flow) && _stock.Contains(flow.Buffer))
                _stock.DestroyInventory(flow.Buffer);
        }
        NavigationChanged?.Invoke(context);
        return ConveyorFailure.None;
    }
}

public readonly record struct ConveyorBuildingPort(EntityId Building, PlayerId Owner,
    Vector3 Position, Vector3 Facing, bool Input, string Key = "",
    bool Adaptable = false, ConveyorPortState Status = ConveyorPortState.Free);

public sealed class ConveyorConstructionCommand : ISimulationCommand
{
    private readonly ConveyorConstructionService _service;
    public ConveyorConstructionCommand(ConveyorConstructionService service,PlayerId issuer,
        ConveyorBuildRequest request,EntityId payer,SimulationTick submittedAtTick)
    { _service=service;Issuer=issuer;Request=request;Payer=payer;SubmittedAtTick=submittedAtTick; }
    public PlayerId Issuer { get; }
    public ConveyorBuildRequest Request { get; }
    public EntityId Payer { get; }
    public SimulationTick SubmittedAtTick { get; }
    public SimulationTick ExecutedAtTick { get; private set; }
    public ConveyorFailure Result { get; private set; }
    public ConveyorCargoRecovery[] CargoRecovery { get; private set; } = [];
    public bool Accepted=>ExecutedAtTick!=SimulationTick.Zero && Result==ConveyorFailure.None;
    public void Execute(SimulationContext context)
    { ExecutedAtTick=context.Tick; Result=_service.Apply(context,Issuer,Request,Payer, out var cargo); CargoRecovery=cargo; }
}

