using System.Diagnostics;
using System.Numerics;
using ForgeLine.Core;
using ForgeLine.Game;
using ForgeLine.Presentation;
using ForgeLine.Simulation;

namespace ForgeLine.Client;

/// <summary>Selected-subset measurements for the existing standalone ring qualification lane.</summary>
internal static class GroupActionRingMeasurements
{
    internal static object[] Capture()
    {
        var results=new List<object>();
        var camera=new RtsCamera();var layout=GameplayHudLayout.Create(1920,1080,96);
        foreach(int count in new[]{0,1,2,10,50,100,1000})
        {
            var instances=Enumerable.Range(1,count).Select(i=>new RenderInstance(new EntityId((uint)i,1),
                RenderTransform.Identity with {Position=new(i*.01f,0,0)},default,default,RenderVisibilityMask.World,
                Selectable:new(new PlayerId(1),ControllableEntityCategory.Unit))).ToArray();
            var snapshot=new PresentationSnapshot(new SimulationTick(4),TimeSpan.FromMilliseconds(50),count,instances,sessionId:new(1),
                playerExperience:default(PlayerExperienceSnapshot) with {Player=new(1),Tick=new(4)});
            var buffer=new PresentationSnapshotBuffer();buffer.Publish(snapshot);var world=new RenderWorld();world.Update(buffer);
            var selection=new SelectionSet();selection.Replace(instances.Select(i=>i.Entity).ToArray());
            GroupActionRingContext? context=null;
            for(int i=0;i<128;i++) context=GroupActionRingContext.Capture(snapshot,world,selection);
            long before=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
            for(int i=0;i<128;i++) context=GroupActionRingContext.Capture(snapshot,world,selection);
            double captureUs=Stopwatch.GetElapsedTime(start).TotalMicroseconds/128;
            long updateBytes=(GC.GetAllocatedBytesForCurrentThread()-before)/128;
            if(context?.Count!=count) throw new InvalidDataException("Group aggregation lost an authorized identity.");
            for(int i=0;i<128;i++) context.TryAnchor(world,camera,1,new(1),layout,out _,out _);
            before=GC.GetAllocatedBytesForCurrentThread();start=Stopwatch.GetTimestamp();
            bool anchored=false;int visible=0;
            for(int i=0;i<128;i++) anchored=context.TryAnchor(world,camera,1,new(1),layout,out _,out visible);
            double anchorUs=Stopwatch.GetElapsedTime(start).TotalMicroseconds/128;
            long anchorBytes=(GC.GetAllocatedBytesForCurrentThread()-before)/128;
            if(anchorBytes!=0) throw new InvalidDataException("Warm group anchor allocated managed frame storage.");
            results.Add(new{Selected=count,ContextMembers=context.Count,Samples=128,CaptureMeanMicroseconds=captureUs,
                CaptureBytesPerUpdate=updateBytes,AnchorMeanMicroseconds=anchorUs,AnchorBytesPerFrame=anchorBytes,Anchored=anchored,Visible=visible});
        }
        return results.ToArray();
    }
}
