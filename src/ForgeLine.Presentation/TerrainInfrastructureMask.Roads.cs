using System.Numerics;

namespace ForgeLine.Presentation;

public sealed partial class TerrainInfrastructureMask
{
    // Road graphs contain navigation-sized pieces. Join contiguous collinear
    // centerlines before indexing visual regions; gameplay geometry is untouched.
    public static TerrainInfrastructureMask FromRoadSegments(
        IEnumerable<(Vector2 Start,Vector2 End)> segments,float widthMeters,float featherMeters=4)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if(!float.IsFinite(widthMeters)||widthMeters<=0||!float.IsFinite(featherMeters)||featherMeters is <0 or >1024)
            throw new ArgumentOutOfRangeException(nameof(widthMeters));
        var source=segments.Take(MaximumRegions+1).ToArray();
        if(source.Length>MaximumRegions)throw new ArgumentException("Road segment input exceeds its budget.",nameof(segments));
        var groups=new Dictionary<(long X,long Z,long Line),List<RoadInterval>>();
        foreach(var item in source)
        {
            var a=Millimeters(item.Start);var b=Millimeters(item.End);
            long dx=b.X-a.X,dz=b.Z-a.Z,divisor=Gcd(Math.Abs(dx),Math.Abs(dz));
            if(divisor==0)throw new ArgumentException("Road endpoints must differ at millimeter precision.",nameof(segments));
            dx/=divisor;dz/=divisor;
            if(dx<0||dx==0&&dz<0){dx=-dx;dz=-dz;}
            long line=checked(dx*a.Z-dz*a.X);
            long from=checked(dx*a.X+dz*a.Z),to=checked(dx*b.X+dz*b.Z);
            Vector2 start=Point(a),end=Point(b);
            if(from>to){(from,to)=(to,from);(start,end)=(end,start);}
            if(!groups.TryGetValue((dx,dz,line),out var group))groups.Add((dx,dz,line),group=[]);
            group.Add(new(from,to,start,end));
        }
        var output=new List<TerrainBlendRegion>();
        foreach(var group in groups.OrderBy(g=>g.Key.X).ThenBy(g=>g.Key.Z).ThenBy(g=>g.Key.Line))
        {
            var ordered=group.Value.OrderBy(i=>i.From).ThenBy(i=>i.To).ToArray();
            var current=ordered[0];
            for(int index=1;index<ordered.Length;index++)
            {
                var next=ordered[index];
                if(next.From>current.To){Add(current);current=next;}
                else if(next.To>current.To)current=current with {To=next.To,End=next.End};
            }
            Add(current);
        }
        return new(output);

        // Outward 1 mm per side covers endpoint quantization. The existing road
        // feather, control-cell inflation and four-layer admission remain intact.
        void Add(RoadInterval road)=>output.Add(Road(road.Start,road.End,widthMeters+.002f,featherMeters));
    }

    private readonly record struct RoadInterval(long From,long To,Vector2 Start,Vector2 End);
    private static (long X,long Z) Millimeters(Vector2 point)
    {
        if(!float.IsFinite(point.X)||!float.IsFinite(point.Y)||MathF.Abs(point.X)>1_000_000||MathF.Abs(point.Y)>1_000_000)
            throw new ArgumentException("Road coordinates exceed the finite infrastructure extent.");
        return ((long)Math.Round(point.X*1000.0,MidpointRounding.ToEven),(long)Math.Round(point.Y*1000.0,MidpointRounding.ToEven));
    }
    private static Vector2 Point((long X,long Z) point)=>new((float)(point.X/1000.0),(float)(point.Z/1000.0));
    private static long Gcd(long x,long z){while(z!=0){(x,z)=(z,x%z);}return x;}
}
