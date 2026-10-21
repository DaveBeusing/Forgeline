using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Derived scenery over existing geography. No graph, blocker or ECS transform is changed.
internal sealed class CrossingVisualLayout
{
    private static readonly (float Cargo,float Supply) TruckOffsets=CreateTruckOffsets();
    private const float Clearance=.03f;
    private static readonly float[] CrossSamples=[-9,-8.4f,-6,0,6,8.4f,9];
    private readonly List<DrivingSurface> _surfaces=[];
    private readonly List<DrivingCorridor> _corridors=[];
    private readonly record struct DrivingSurface(Vector3 Center,Vector3 Direction,Vector3 Normal,float HalfLength,bool Bridge);
    private readonly record struct DrivingCorridor(Vector3 Center,Vector3 Direction,float HalfLength,bool Bridge);
    internal Dictionary<string,RenderTransform> Crossings { get; }=new(StringComparer.Ordinal);
    internal Dictionary<string,RenderTransform> Aprons { get; }=new(StringComparer.Ordinal);
    internal List<RenderInstance> Approaches { get; }=[];
    private readonly List<BridgeOverlay> _bridgeOverlays=[];
    private readonly record struct BridgeOverlay(WorldVisualId Visual,Vector2 Original,RenderTransform Deck,RenderTransform Ground);

    internal static CrossingVisualLayout Create(BattlefieldDefinition map,TerrainWorld terrain)
    {
        var result=new CrossingVisualLayout();
        foreach(var crossing in map.Crossings)
        {
            var edge=map.RoadEdges.Single(x=>x.Key==crossing.LogisticsEdgeKey);
            Vector3 start=map.RoadNodes.Single(x=>x.Key==edge.SourceNodeKey).Position;
            Vector3 end=map.RoadNodes.Single(x=>x.Key==edge.DestinationNodeKey).Position;
            Vector3 direction=Vector3.Normalize(new(end.X-start.X,0,end.Z-start.Z));
            Vector3 side=Vector3.Cross(Vector3.UnitY,direction);
            float yaw=MathF.Atan2(direction.X,direction.Z);
            bool bridge=crossing.LogisticsEdgeKey.Contains("bridge",StringComparison.OrdinalIgnoreCase);
            Vector3 center=Ground(crossing.Position);
            if(MathF.Abs(Vector3.Dot(center-start,side))>.001f)
                throw new InvalidOperationException("Crossing center is not on its authoritative road edge.");
            Vector3 near=Ground(center-direction*21),far=Ground(center+direction*21);
            Vector3 span=far-near;
            Quaternion rotation=Quaternion.CreateFromYawPitchRoll(yaw,-MathF.Atan2(span.Y,42),0);
            Vector3 normal=Vector3.Transform(Vector3.UnitY,rotation);
            Vector3 anchor=(near+far)*.5f;
            float crown=0;
            for(int i=0;i<=16;i++)
            foreach(float lateral in CrossSamples)
            {
                Vector3 p=Vector3.Lerp(near,far,i/16f)+side*lateral;
                crown=Math.Max(crown,Ground(p).Y-PlaneHeight(anchor,normal,p));
            }
            anchor.Y+=crown+Clearance;
            Vector3 nearTop=near with { Y=PlaneHeight(anchor,normal,near) };
            Vector3 farTop=far with { Y=PlaneHeight(anchor,normal,far) };
            Vector3[] nearPath=ClearPath(start,Ground(center-direction*29));
            Vector3[] farPath=ClearPath(Ground(center+direction*29),end);
            // Raising both ramp endpoints clears its interior without breaking a shared road edge.
            float nearLift=RequiredLift(nearPath[^1],nearTop),farLift=RequiredLift(farTop,farPath[0]);
            nearPath[^1]+=Vector3.UnitY*nearLift;nearTop+=Vector3.UnitY*nearLift;
            farPath[0]+=Vector3.UnitY*farLift;farTop+=Vector3.UnitY*farLift;
            span=farTop-nearTop;
            rotation=Pose(nearTop,farTop);
            normal=Vector3.Transform(Vector3.UnitY,rotation);
            anchor=(nearTop+farTop)*.5f;
            AddSurface(nearTop,farTop);
            result._corridors.Add(new(center,direction,Math.Max(MathF.Abs(Vector3.Dot(start-center,direction)),MathF.Abs(Vector3.Dot(end-center,direction))),bridge));
            // Catalog lift plus the authored top: bridge (.25-.07)*4; road (.48-.42)*.35.
            float floorOffset=bridge?.72f:.021f;
            result.Crossings.Add(crossing.Key,new(anchor-normal*floorOffset,rotation,
                new(bridge?18:12,bridge?4:.35f,span.Length())));
            if(bridge)
            {
                if(crossing.Key=="crossing.north_bridge")
                {
                    result.Aprons.Add("kit.bridge-apron.north",Apron(nearTop,nearPath[^1],-direction));
                    result.Aprons.Add("kit.bridge-apron.south",Apron(farTop,farPath[0],direction));
                }
                AddSurface(nearPath[^1],nearTop);AddSurface(farTop,farPath[0]);
            }
            else
            {
                AddStrip(nearPath[^1],nearTop);
                AddStrip(farTop,farPath[0]);
            }
            EmitPath(nearPath);EmitPath(farPath);
            if(bridge)
            foreach(var item in map.WorldObjects)
            {
                if(item.Key is not ("decal.crack.north" or "decal.roadwear.center"))continue;
                Vector3 offset=Vector3.Transform(new Vector3(item.Position.X-center.X,0,item.Position.Z-center.Z),rotation);
                Vector3 scale=item.Scale with { X=Math.Min(item.Scale.X,42) };
                float lift=item.Visual==WorldVisualId.DecalConcreteCrack?.004f:.003f;
                var deckOverlay=new RenderTransform(anchor+offset+normal*lift,
                    Quaternion.Normalize(Quaternion.Concatenate(Quaternion.CreateFromAxisAngle(Vector3.UnitY,item.RotationDegrees*MathF.PI/180),rotation)),scale);
                var groundOverlay=new RenderTransform(Ground(anchor+offset)+Vector3.UnitY*.04f,
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY,item.RotationDegrees*MathF.PI/180+yaw),scale);
                result._bridgeOverlays.Add(new(item.Visual,new(item.Position.X,item.Position.Z),deckOverlay,groundOverlay));
            }


            Vector3[] ClearPath(Vector3 a,Vector3 b)
            {
                float length=Vector2.Distance(new(a.X,a.Z),new(b.X,b.Z));
                int segments=Math.Max(1,(int)MathF.Ceiling(length/8));
                var points=new Vector3[segments+1];
                for(int i=0;i<=segments;i++)
                {
                    Vector3 point=Vector3.Lerp(a,b,(float)i/segments);
                    float height=Ground(point).Y;
                    foreach(float lateral in CrossSamples)height=Math.Max(height,Ground(point+side*lateral).Y);
                    points[i]=point with { Y=height+Clearance };
                }
                for(int i=0;i<segments;i++)
                {
                    float lift=RequiredLift(points[i],points[i+1]);
                    points[i]+=Vector3.UnitY*lift;points[i+1]+=Vector3.UnitY*lift;
                }
                return points;
            }
            float RequiredLift(Vector3 a,Vector3 b)
            {
                float lift=0;
                for(int i=0;i<=16;i++)
                foreach(float lateral in CrossSamples)
                {
                    Vector3 point=Vector3.Lerp(a,b,i/16f)+side*lateral;
                    lift=Math.Max(lift,Ground(point).Y+Clearance-point.Y);
                }
                return lift>0?lift+.0001f:0;
            }
            void EmitPath(Vector3[] path)
            {
                for(int i=1;i<path.Length;i++)AddStrip(path[i-1],path[i]);
            }
            Quaternion Pose(Vector3 a,Vector3 b)
            {
                Vector3 delta=b-a;
                return Quaternion.CreateFromYawPitchRoll(yaw,-MathF.Atan2(delta.Y,new Vector2(delta.X,delta.Z).Length()),0);
            }
            void AddSurface(Vector3 a,Vector3 b)
            {
                result._surfaces.Add(new((a+b)*.5f,direction,Vector3.Transform(Vector3.UnitY,Pose(a,b)),
                    Vector2.Distance(new(a.X,a.Z),new(b.X,b.Z))*.5f,bridge));
            }
            void AddStrip(Vector3 a,Vector3 b)
            {
                Vector3 delta=b-a;
                Quaternion pose=Pose(a,b);
                Vector3 up=Vector3.Transform(Vector3.UnitY,pose);
                float gap=0;
                for(int i=0;i<=16;i++)
                foreach(float lateral in CrossSamples)
                {
                    Vector3 point=Vector3.Lerp(a,b,i/16f)+side*lateral;
                    gap=Math.Max(gap,point.Y-Ground(point).Y);
                }
                // Extend the existing solid slab and gravel support below the terrain, never float a thin tile.
                float roadHeight=Math.Max(.35f,(gap+Clearance)*up.Y/.08f);
                float shoulderHeight=Math.Max(.30f,(gap+Clearance)*up.Y/.06f);
                Vector3 floor=(a+b)*.5f;
                Add(floor-up*(.06f*roadHeight),pose,new(12,roadHeight,delta.Length()),InfrastructurePresentationKind.RoadSegment);
                Vector3 shoulderRoot=floor+up*(.01f*shoulderHeight-.003f);
                Add(shoulderRoot-side*7.2f,pose,new(15,shoulderHeight,delta.Length()),InfrastructurePresentationKind.RoadShoulder);
                Add(shoulderRoot+side*7.2f,pose,new(15,shoulderHeight,delta.Length()),InfrastructurePresentationKind.RoadShoulder);
                AddSurface(a,b);
            }
        }
        return result;

        Vector3 Ground(Vector3 p)
        {
            if(!terrain.TrySampleHeight(p.X,p.Z,out float h)) throw new InvalidOperationException("Crossing visual lies outside terrain.");
            return p with { Y=h };
        }
        static RenderTransform Apron(Vector3 high,Vector3 low,Vector3 outward)
        {
            Vector3 delta=low-high;
            float depth=MathF.Sqrt(delta.LengthSquared()-.058f*.058f);
            float pitch=MathF.Atan2(-.058f,depth)-MathF.Atan2(delta.Y,8);
            Quaternion rotation=Quaternion.CreateFromYawPitchRoll(MathF.Atan2(outward.X,outward.Z),pitch,0);
            // Authored ramp endpoints: (0,.23,-.5) and (0,-.35,.5), Y scale .1.
            return new((high+low)*.5f-Vector3.Transform(new Vector3(0,-.006f,0),rotation),rotation,new(18,.1f,depth));
        }
        void Add(Vector3 position,Quaternion rotation,Vector3 scale,InfrastructurePresentationKind kind)
        {
            uint id=checked(0xE1000000u+(uint)result.Approaches.Count);
            result.Approaches.Add(new(new EntityId(id,1),new(position,rotation,scale),new(1),RenderMaterialHandle.Default,
                RenderVisibilityMask.World,id,InfrastructureFeature:new(kind,InfrastructurePresentationState.Operational)));
        }
    }

    internal RenderTransform AlignBridgeOverlay(in RenderTransform original,WorldVisualId visual,bool bridgeOperational=true)
    {
        foreach(var overlay in _bridgeOverlays)
        {
            if(overlay.Visual!=visual || Vector2.Distance(new(original.Position.X,original.Position.Z),overlay.Original)>.001f)continue;
            return bridgeOperational?overlay.Deck:overlay.Ground;
        }
        return original;
    }

    internal RenderTransform GroundTruck(in RenderTransform transform,bool cargo,bool bridgeOperational,bool fordOperational)
    {
        if(!TryDrivingSurface(transform.Position,out float height,out Vector3 normal,bridgeOperational,fordOperational)) return transform;
        Vector3 forward=Vector3.Transform(Vector3.UnitZ,transform.Rotation);
        forward=Vector3.Normalize(forward-normal*Vector3.Dot(forward,normal));
        Vector3 right=Vector3.Normalize(Vector3.Cross(normal,forward));
        Matrix4x4 basis=new(right.X,right.Y,right.Z,0,normal.X,normal.Y,normal.Z,0,forward.X,forward.Y,forward.Z,0,0,0,0,1);
        float offset=cargo?TruckOffsets.Cargo:TruckOffsets.Supply;
        return transform with { Position=transform.Position with { Y=height+offset/normal.Y },Rotation=Quaternion.CreateFromRotationMatrix(basis) };
    }

    internal bool TryDrivingSurface(Vector3 point,out float height,out Vector3 normal,bool bridgeOperational=true,bool fordOperational=true)
    {
        foreach(var corridor in _corridors)
        {
            if(corridor.Bridge?!bridgeOperational:!fordOperational)continue;
            Vector3 delta=point-corridor.Center;
            if(MathF.Abs(Vector3.Dot(delta,corridor.Direction))>corridor.HalfLength+.001f ||
                MathF.Abs(Vector3.Dot(delta,Vector3.Cross(Vector3.UnitY,corridor.Direction)))>6)continue;
            foreach(var surface in _surfaces)
            {
                if(surface.Bridge!=corridor.Bridge)continue;
                if(MathF.Abs(Vector3.Dot(point-surface.Center,surface.Direction))>surface.HalfLength+.001f)continue;
                normal=surface.Normal;height=PlaneHeight(surface.Center,normal,point);return true;
            }
        }
        height=0;normal=Vector3.UnitY;return false;
    }
    private static float PlaneHeight(Vector3 center,Vector3 normal,Vector3 point)=>
        center.Y-(normal.X*(point.X-center.X)+normal.Z*(point.Z-center.Z))/normal.Y;
    private static (float Cargo,float Supply) CreateTruckOffsets()
    {
        var units=DirectorateContent.CreateUnitCatalog();
        return (units[UnitIds.CargoTruck].Movement.HeightOffset,units[UnitIds.SupplyTruck].Movement.HeightOffset);
    }
}
