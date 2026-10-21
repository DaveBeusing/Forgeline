using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Derived scenery over existing geography. No graph, blocker or ECS transform is changed.
internal sealed class CrossingVisualLayout(TerrainWorld terrain)
{
    private static readonly (float Cargo,float Supply) TruckOffsets=CreateTruckOffsets();
    private readonly List<DrivingSurface> _surfaces=[];
    private readonly record struct DrivingSurface(Vector3 Center,Vector3 Direction,Vector3 Normal,bool Bridge);
    internal Dictionary<string,RenderTransform> Crossings { get; }=new(StringComparer.Ordinal);
    internal Dictionary<string,RenderTransform> Aprons { get; }=new(StringComparer.Ordinal);
    internal List<RenderInstance> Approaches { get; }=[];

    internal static CrossingVisualLayout Create(BattlefieldDefinition map,TerrainWorld terrain)
    {
        var result=new CrossingVisualLayout(terrain);
        ReadOnlySpan<float> lateralSamples=[-6,0,6];
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
            foreach(float lateral in lateralSamples)
            {
                Vector3 p=Vector3.Lerp(near,far,i/16f)+side*lateral;
                crown=Math.Max(crown,Ground(p).Y-PlaneHeight(anchor,normal,p));
            }
            anchor.Y+=crown+.021f;
            result._surfaces.Add(new(anchor,direction,normal,bridge));
            // Catalog lift plus the authored top: bridge (.25-.07)*4; road (.48-.42)*.35.
            float floorOffset=bridge?.72f:.021f;
            result.Crossings.Add(crossing.Key,new(anchor-normal*floorOffset,rotation,
                new(bridge?18:12,bridge?4:.35f,span.Length())));
            Vector3 nearTop=near with { Y=PlaneHeight(anchor,normal,near) };
            Vector3 farTop=far with { Y=PlaneHeight(anchor,normal,far) };
            Vector3 nearRoad=Ground(center-direction*29),farRoad=Ground(center+direction*29);
            if(bridge)
            {
                if(crossing.Key=="crossing.north_bridge")
                {
                    result.Aprons.Add("kit.bridge-apron.north",Apron(nearTop,nearRoad,-direction));
                    result.Aprons.Add("kit.bridge-apron.south",Apron(farTop,farRoad,direction));
                }
            }
            else
            {
                AddStrip(nearRoad+Vector3.UnitY*.021f,nearTop,true);
                AddStrip(farTop,farRoad+Vector3.UnitY*.021f,true);
            }
            AddApproach(start,nearRoad);
            AddApproach(farRoad,end);

            void AddApproach(Vector3 a,Vector3 b)
            {
                float length=Vector2.Distance(new(a.X,a.Z),new(b.X,b.Z));
                int segments=Math.Max(1,(int)MathF.Ceiling(length/8));
                for(int i=0;i<segments;i++)
                    AddStrip(Ground(Vector3.Lerp(a,b,(float)i/segments)),Ground(Vector3.Lerp(a,b,(float)(i+1)/segments)),false);
            }
            void AddStrip(Vector3 a,Vector3 b,bool floorEndpoints)
            {
                Vector3 delta=b-a;
                float horizontal=new Vector2(delta.X,delta.Z).Length();
                Quaternion pose=Quaternion.CreateFromYawPitchRoll(yaw,-MathF.Atan2(delta.Y,horizontal),0);
                Vector3 up=Vector3.Transform(Vector3.UnitY,pose);
                Vector3 origin=(a+b)*.5f-(floorEndpoints?up*.021f:Vector3.Zero);
                Add(origin,pose,new(12,.35f,delta.Length()),InfrastructurePresentationKind.RoadSegment);
                Add(origin-side*7.2f+up*.021f,pose,new(15,.30f,delta.Length()),InfrastructurePresentationKind.RoadShoulder);
                Add(origin+side*7.2f+up*.021f,pose,new(15,.30f,delta.Length()),InfrastructurePresentationKind.RoadShoulder);
            }
        }
        return result;

        Vector3 Ground(Vector3 p)
        {
            if(!terrain.TrySampleHeight(p.X,p.Z,out float h)) throw new InvalidOperationException("Crossing visual lies outside terrain.");
            return p with { Y=h };
        }
        static RenderTransform Apron(Vector3 high,Vector3 lowGround,Vector3 outward)
        {
            Vector3 low=lowGround+Vector3.UnitY*.021f;
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
        foreach(var surface in _surfaces)
        {
            if(surface.Bridge?!bridgeOperational:!fordOperational) continue;
            Vector3 delta=point-surface.Center;
            float along=Vector3.Dot(delta,surface.Direction);
            float lateral=Vector3.Dot(delta,Vector3.Cross(Vector3.UnitY,surface.Direction));
            if(MathF.Abs(along)>29||MathF.Abs(lateral)>6) continue;
            normal=surface.Normal;
            if(MathF.Abs(along)<=21)
            { height=PlaneHeight(surface.Center,normal,point);return true; }
            Vector3 outward=surface.Direction*MathF.Sign(along);
            Vector3 edge=surface.Center+outward*21,outside=surface.Center+outward*29;
            if(!terrain.TrySampleHeight(outside.X,outside.Z,out float ground)) throw new InvalidOperationException("Crossing apron lies outside terrain.");
            float high=PlaneHeight(surface.Center,normal,edge),low=ground+.021f;
            float slope=(low-high)/8;
            normal=Vector3.Normalize(Vector3.UnitY-outward*slope);
            height=high+(MathF.Abs(along)-21)*slope;
            return true;
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
