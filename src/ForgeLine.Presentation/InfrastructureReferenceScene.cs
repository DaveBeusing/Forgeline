using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using ForgeLine.Assets;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Economy;
using ForgeLine.Logistics;
using ForgeLine.Navigation;
using ForgeLine.Simulation;
using ForgeLine.World;

namespace ForgeLine.Presentation;

// Bounded visual fixtures. Vehicle X/Z and wheel travel come from actual fixed-tick movement.
internal static class InfrastructureReferenceScene
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };
    internal sealed record TrafficSample(PresentationSnapshot Snapshot, RenderInstance Truck, WorldTransform[] Trace, int AcceptedCommands);
    private sealed record Chart(InfrastructurePresentationKind Kind, Vector2[] Path);
    private static readonly Chart[] Charts =
    [
        new(InfrastructurePresentationKind.RoadCurveShort, [new(-.559627f,-.213554f),new(-.210647f,-.213554f),new(.393537f,.390630f)]),
        new(InfrastructurePresentationKind.RoadCurveLong, [new(-.647507f,-.247372f),new(-.28f,-.247372f),new(.03f,.02f),new(.30f,.433774f),new(.639486f,.433774f)]),
        new(InfrastructurePresentationKind.RoadJunctionT, [new(0,.42f),new(0,.03f),new(.42f,.03f)]),
        new(InfrastructurePresentationKind.RoadJunctionCross, [new(-.42f,0),Vector2.Zero,new(.42f,0)])
    ];

    internal static VisualReferenceScene Create(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        if (kind is >= VisualReferenceSceneKind.InfrastructureBridgeApproach and <= VisualReferenceSceneKind.InfrastructureFordExit)
        {
            bool ford = kind >= VisualReferenceSceneKind.InfrastructureFordApproach;
            int phase = (int)kind - (int)(ford ? VisualReferenceSceneKind.InfrastructureFordApproach : VisualReferenceSceneKind.InfrastructureBridgeApproach);
            Vector3 target = new(1536, 0, ford ? 2200 : 920);
            Vector3[] path = [target - Vector3.UnitX * 50, target + Vector3.UnitX * 50];
            var sample = SimulatePath(terrain, assets, path, .2f + .3f * phase);
            target = Ground(terrain, target);
            var basis=VisualReferenceScene.Create(VisualReferenceSceneKind.EnvironmentKitBridge,terrain,false,assets);
            var backgroundInstances=basis.Snapshot.Instances.ToArray().ToList();
            backgroundInstances.Add(sample.Truck with { Entity=new(0xE7400000,1),DebugIdentity=0xE7400000 });
            var snapshot=new PresentationSnapshot(basis.Snapshot.Tick,basis.Snapshot.TickDuration,
                basis.Snapshot.SimulationEntityCount+1,backgroundInstances.ToArray(),basis.Snapshot.Intelligence,new(1),basis.Snapshot.PlayerExperience);
            return Scene(kind, target, snapshot, sample.Trace);
        }
        if (kind is VisualReferenceSceneKind.InfrastructureRoadJunctions or >= VisualReferenceSceneKind.InfrastructureCurveShort and <= VisualReferenceSceneKind.InfrastructureCrossJunction)
            return RoadReview(kind, terrain, assets);
        Vector3 center = Ground(terrain, new(1536, 0, 1536));
        var instances = new List<RenderInstance>();
        int count = kind switch
        {
            VisualReferenceSceneKind.InfrastructureSingle => 1,
            VisualReferenceSceneKind.InfrastructureHundred => 100,
            VisualReferenceSceneKind.InfrastructureThousand => 1000,
            VisualReferenceSceneKind.InfrastructureBridgeStates => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (kind == VisualReferenceSceneKind.InfrastructureBridgeStates) center = Ground(terrain, new(650, 0, 520));
        int columns = count == 1000 ? 40 : count == 100 ? 10 : count;
        for (int i = 0; i < count; i++)
        {
            Vector3 position = Ground(terrain, center + new Vector3((i % columns - (columns - 1) * .5f) * 25, 0,
                (i / columns - (count / columns - 1) * .5f) * 48));
            var state = count == 1 ? InfrastructurePresentationState.Operational : (InfrastructurePresentationState)(i % 3 + 1);
            instances.Add(new(new(0xE6000000u + (uint)i, 1), new(position, Quaternion.Identity, new(18,4,42)),
                new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                InfrastructureFeature: new(InfrastructurePresentationKind.RoadBridge, state)));
        }
        return Scene(kind, center, Snapshot(instances,terrain), Array.Empty<WorldTransform>());
    }

    private static VisualReferenceScene RoadReview(VisualReferenceSceneKind kind, TerrainWorld terrain, RuntimeAssetCatalog? assets)
    {
        Vector3 center = Ground(terrain, new(650, 0, 520));
        var instances = new List<RenderInstance>();
        var traces = new List<WorldTransform[]>();
        bool gallery = kind == VisualReferenceSceneKind.InfrastructureRoadJunctions;
        int selected = gallery ? -1 : (int)kind - (int)VisualReferenceSceneKind.InfrastructureCurveShort;
        for (int i = 0; i < Charts.Length; i++)
        {
            if (!gallery && i != selected) continue;
            var chart = Charts[i];
            Vector3 point = center + (gallery ? new Vector3(i % 2 == 0 ? -32 : 32, 0, i < 2 ? -32 : 32) : Vector3.Zero);
            var (transform, anchor, normal) = ReviewPlane(terrain, point);
            uint id = 0xE7000000u + (uint)(i * 3);
            instances.Add(new(new(id,1), transform, new(1), RenderMaterialHandle.Default, RenderVisibilityMask.World,
                InfrastructureFeature: new(chart.Kind, InfrastructurePresentationState.Operational)));
            instances.Add(GravelStand(id+1,anchor,normal,transform.Rotation,58));
            Matrix4x4 world = InfrastructurePresentationCatalog.AdjustWorldTransform(
                new(chart.Kind,InfrastructurePresentationState.Operational),transform.ToMatrix());
            Vector3[] path = chart.Path.Select(p => Vector3.Transform(new Vector3(p.X,-.42f,p.Y),world)).ToArray();
            var sample = SimulatePath(terrain,assets,path,.5f);
            var truck = sample.Truck;
            float height = anchor.Y - (normal.X*(truck.Transform.Position.X-anchor.X)+normal.Z*(truck.Transform.Position.Z-anchor.Z))/normal.Y;
            float offset = DirectorateContent.CreateUnitCatalog()[UnitIds.CargoTruck].Movement.HeightOffset;
            var pose = truck.Transform with { Position=truck.Transform.Position with { Y=height+offset/normal.Y },
                Rotation=OnPlane(truck.Transform.Rotation,normal) };
            instances.Add(truck with { Entity=new(id+2,1),DebugIdentity=id+2,Transform=pose,Selectable=SelectablePresentationMetadata.None });
            traces.Add(sample.Trace);
        }
        if (gallery)
        {
            var (plane,anchor,normal) = ReviewPlane(terrain,center);
            instances.Add(new(new(0xE7001000,1),plane with { Scale=new(12,.35f,12) },new(1),
                RenderMaterialHandle.Default,RenderVisibilityMask.World,
                InfrastructureFeature:new(InfrastructurePresentationKind.RoadYardTransition,InfrastructurePresentationState.Operational)));
            instances.Add(GravelStand(0xE7001001,anchor,normal,plane.Rotation,16));
        }
        return Scene(kind,center,Snapshot(instances,terrain),traces);
    }

    internal static TrafficSample SimulatePath(TerrainWorld terrain, RuntimeAssetCatalog? assets, Vector3[] path, float phase)
    {
        // Isolate locomotion from economic dispatch; keep current map obstacles and the real wheeled pathfinder.
        var map=CentralDivideBattlefield.Create();
        var simulation=new SimulationCoordinator(seed:VisualReferenceScene.Seed);
        var inventories=new InventoryStore();
        var network=new LogisticsNetwork();
        var navigation=NavigationWorld.Build(terrain,map.CreateNavigationObstacles(),
            new NavigationGridSettings { CellSizeMeters=4,StaticObstacleClearanceMeters=.5f },
            new NavigationSectorSettings { SectorSizeCells=8 });
        var routing=new HierarchicalNavigationSystem(new HierarchicalPathfinder(navigation));
        var spatial=new SpatialGridIndex();
        var transport=new CargoTransportSystem(network,inventories,navigation:routing);
        simulation.RegisterSystem(routing);
        simulation.RegisterSystem(new SpatialIndexSystem(new SpatialIndexSynchronizer(spatial)));
        simulation.RegisterSystem(new GroundMovementSystem(terrain,spatial));
        simulation.RegisterSystem(transport);
        var entities = simulation.Entities;
        var definition = DirectorateContent.CreateUnitCatalog()[UnitIds.CargoTruck];
        Vector3 start = Ground(terrain,path[0])+Vector3.UnitY*definition.Movement.HeightOffset;
        EntityId truck = CargoTruckFactory.Create(entities,inventories,start,new(1),transport,
            new CargoTruckDefinition(definition.Key,definition.CargoCapacity,definition.Movement,definition.VisualScale,definition.VisualId));
        entities.AddComponent(truck,new UnitIdentity(UnitIds.CargoTruck,new(1)));
        var buffer = new PresentationSnapshotBuffer();
        simulation.RegisterTickObserver(new PresentationExtractor(buffer,runtimeAssets:assets));
        float targetDistance = 0;
        for(int i=1;i<path.Length;i++) targetDistance += Vector2.Distance(new(path[i-1].X,path[i-1].Z),new(path[i].X,path[i].Z));
        targetDistance *= phase;
        var trace = new List<WorldTransform> { entities.GetComponent<WorldTransform>(truck) };
        PresentationSnapshot? best = null; RenderInstance selected = default;
        float distance = 0, bestError = float.MaxValue;
        int accepted = 0;
        for(int waypoint=1;waypoint<path.Length;waypoint++)
        {
            var command = new MoveEntitiesCommand(new(1),[truck],path[waypoint],simulation.Clock.CurrentTick);
            simulation.SubmitCommand(command,new SimulationTick(simulation.Clock.CurrentTick.Value+1));
            bool reached = false;
            for(int tick=0;tick<1200;tick++)
            {
                simulation.AdvanceOneTick();
                var current = entities.GetComponent<WorldTransform>(truck);
                var previous = trace[^1];
                distance += Vector2.Distance(new(previous.Position.X,previous.Position.Z),new(current.Position.X,current.Position.Z));
                trace.Add(current);
                var movement = entities.GetComponent<GroundMovementState>(truck);
                if(movement.Velocity.LengthSquared() > .01f && MathF.Abs(distance-targetDistance)<bestError)
                {
                    if(!buffer.TryReadLatest(out var snapshot)) throw new InvalidOperationException("Traffic snapshot was not published.");
                    best=snapshot;selected=snapshot.Instances.ToArray().Single(x=>x.Entity==truck);bestError=MathF.Abs(distance-targetDistance);
                }
                float remaining = Vector2.Distance(new(current.Position.X,current.Position.Z),new(path[waypoint].X,path[waypoint].Z));
                if(remaining <= definition.Movement.StopRadius+.1f && movement.Velocity.LengthSquared()<.04f)
                { reached=true;break; }
            }
            if(!reached || command.AcceptedTargetCount!=1)
            {
                var current=entities.GetComponent<WorldTransform>(truck);
                var movement=entities.GetComponent<GroundMovementState>(truck);
                var cargo=entities.GetComponent<CargoTransportRuntimeState>(truck);
                entities.TryGetComponent(truck,out MovementOrder order);
                throw new InvalidOperationException($"Cargo review failed waypoint {waypoint}: accepted={command.AcceptedTargetCount}, position={current.Position}, goal={path[waypoint]}, velocity={movement.Velocity}, cargo={cargo.Lifecycle}/{cargo.WaitReason}/{cargo.FailureReason}, order={JsonSerializer.Serialize(order,JsonOptions)}.");
            }
            accepted += command.AcceptedTargetCount;
        }
        if(best is null) throw new InvalidOperationException("No real moving cargo pose was observed.");
        return new(best,selected,trace.ToArray(),accepted);
    }

    private static (RenderTransform Transform,Vector3 Anchor,Vector3 Normal) ReviewPlane(TerrainWorld terrain,Vector3 point)
    {
        Vector3 center=Ground(terrain,point);
        float dx=(Ground(terrain,point+Vector3.UnitX).Y-Ground(terrain,point-Vector3.UnitX).Y)*.5f;
        float dz=(Ground(terrain,point+Vector3.UnitZ).Y-Ground(terrain,point-Vector3.UnitZ).Y)*.5f;
        Vector3 normal=Vector3.Normalize(new(-dx,1,-dz));
        float crown=0;
        for(int z=-30;z<=30;z+=5)
        for(int x=-30;x<=30;x+=5)
        {
            Vector3 p=point+new Vector3(x,0,z);
            float plane=center.Y+dx*x+dz*z;
            crown=Math.Max(crown,Ground(terrain,p).Y-plane);
        }
        Vector3 anchor=center+Vector3.UnitY*(crown+.05f);
        Quaternion rotation=OnPlane(Quaternion.Identity,normal);
        return (new(anchor-normal*.0245f,rotation,new(12/.34f,.35f,12/.34f)),anchor,normal);
    }

    private static Quaternion OnPlane(Quaternion rotation,Vector3 normal)
    {
        Vector3 forward=Vector3.Transform(Vector3.UnitZ,rotation);
        forward=Vector3.Normalize(forward-normal*Vector3.Dot(forward,normal));
        Vector3 right=Vector3.Normalize(Vector3.Cross(normal,forward));
        return Quaternion.CreateFromRotationMatrix(new Matrix4x4(right.X,right.Y,right.Z,0,normal.X,normal.Y,normal.Z,0,
            forward.X,forward.Y,forward.Z,0,0,0,0,1));
    }
    private static Vector3 Ground(TerrainWorld terrain,Vector3 point)
    {
        if(!terrain.TrySampleHeight(point.X,point.Z,out float height)) throw new InvalidOperationException("Infrastructure review lies outside terrain.");
        return point with { Y=height };
    }
    private static RenderInstance GravelStand(uint id,Vector3 anchor,Vector3 normal,Quaternion rotation,float width)
    {
        const float HeightScale=2/.06f;
        // Expanded existing gravel shoulder, with its top touching the concrete module's underside.
        return new(new(id,1),new(anchor+normal*(.01f*HeightScale-.028f),rotation,new(width/.16f,HeightScale,width)),
            new(1),RenderMaterialHandle.Default,RenderVisibilityMask.World,
            InfrastructureFeature:new(InfrastructurePresentationKind.RoadShoulder,InfrastructurePresentationState.Operational));
    }
    private static PresentationSnapshot Snapshot(List<RenderInstance> instances,TerrainWorld terrain)
    {
        var basis=VisualReferenceScene.Create(VisualReferenceSceneKind.DirectorateUnits,terrain,false);
        PlayerExperienceSnapshot? experience=basis.Snapshot.PlayerExperience is {} source
            ? source with { Selection=PlayerSelectionSummary.Empty } : null;
        return new(new SimulationTick(VisualReferenceScene.FixedTick),TimeSpan.FromMilliseconds(50),instances.Count,
            instances.ToArray(),sessionId:new(1),playerExperience:experience);
    }
    private static VisualReferenceScene Scene(VisualReferenceSceneKind kind,Vector3 target,PresentationSnapshot snapshot,object trace)
    {
        string fingerprint=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { kind,version=1,target,instances=snapshot.Instances.ToArray(),trace },JsonOptions)));
        return new(kind,target,snapshot,default,fingerprint);
    }
}
