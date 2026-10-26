using System.Numerics;
using ForgeLine.World;

namespace ForgeLine.Game;

/// <summary>Fits paid carrier geometry between fixed sockets, preserving real ground clearance.</summary>
internal static class ConveyorTerrainRouter
{
    internal const float MaximumGrade = .25f;
    internal const float MaximumSupportHeight = 32;

    internal static bool TryFit(IReadOnlyList<ConveyorPlacement> source, float startHeight, float endHeight,
        ITerrainQuery terrain, out List<ConveyorPlacement> result)
    {
        result=[];
        if(source.Count==0)return false;
        var distance=new float[source.Count+1];
        var samples=new List<(int Piece,float X,float Z,float Ground)>();
        for(int i=0;i<source.Count;i++)
        {
            var part=source[i];
            bool straight=part.Module is ConveyorModule.Standard or ConveyorModule.ShortSegment or
                ConveyorModule.InputAdapter or ConveyorModule.OutputAdapter;
            distance[i+1]=distance[i]+(straight?part.HorizontalSpan:0);
            var bounds=part.Bounds;
            int nx=Math.Max(1,(int)MathF.Ceiling((bounds.Maximum.X-bounds.Minimum.X)/.5f));
            int nz=Math.Max(1,(int)MathF.Ceiling((bounds.Maximum.Z-bounds.Minimum.Z)/.5f));
            for(int x=0;x<=nx;x++)for(int z=0;z<=nz;z++)
            {
                float wx=float.Lerp(bounds.Minimum.X,bounds.Maximum.X,(float)x/nx);
                float wz=float.Lerp(bounds.Minimum.Z,bounds.Maximum.Z,(float)z/nz);
                if(!terrain.TrySampleHeight(wx,wz,out float ground))return false;
                samples.Add((i,wx,wz,ground));
            }
        }
        float length=distance[^1];
        if(MathF.Abs(endHeight-startHeight)>length*MaximumGrade+.001f)return false;
        var profile=new ConveyorPlacement[source.Count];
        float minimum=MathF.Max(startHeight,endHeight);
        if(!Fits(minimum))
        {
            float maximum=minimum+MaximumSupportHeight;
            if(!Fits(maximum))return false;
            for(int step=0;step<12;step++)
            {
                float middle=(minimum+maximum)*.5f;
                if(Fits(middle))maximum=middle;else minimum=middle;
            }
            _=Fits(maximum);
        }
        result=profile.ToList();
        return true;

        bool Fits(float ceiling)
        {
            for(int i=0;i<source.Count;i++)
            {
                float a=Height(i),b=Height(i+1);
                float rise=b-a;
                profile[i]=source[i] with {Pivot=source[i].Pivot with {Y=(a+b)*.5f},RiseMeters=MathF.Abs(rise)<.00001f?0:rise};
            }
            foreach(var sample in samples)
                if(profile[sample.Piece].UndersideAt(sample.X,sample.Z)<sample.Ground+5.05f)return false;
            return true;

            float Height(int index)=>index==0?startHeight:index==source.Count?endHeight:
                MathF.Min(ceiling,MathF.Min(startHeight+MaximumGrade*distance[index],
                    endHeight+MaximumGrade*(length-distance[index])));
        }
    }
}
