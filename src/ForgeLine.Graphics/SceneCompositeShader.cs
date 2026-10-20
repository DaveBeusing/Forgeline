namespace ForgeLine.Graphics;

internal static class SceneCompositeShader
{
    internal const int ConstantCount = 40;
    internal const string Shared = """
        struct Output {
            float4 Position : SV_Position;
            nointerpolation float4 Settings : TEXCOORD0;
            nointerpolation float4 R0 : TEXCOORD1;
            nointerpolation float4 R1 : TEXCOORD2;
            nointerpolation float4 R2 : TEXCOORD3;
            nointerpolation float4 R3 : TEXCOORD4;
            nointerpolation float4 Camera : TEXCOORD5;
            nointerpolation float4 ColorDensity : TEXCOORD6;
            nointerpolation float4 Fog : TEXCOORD7;
            nointerpolation float4 Grade : TEXCOORD8;
            nointerpolation float4 Post : TEXCOORD9;
        };
        """;
    internal const string Vertex = Shared + """
        cbuffer Constants : register(b0) {
            float4 Settings; float4 R0; float4 R1; float4 R2; float4 R3;
            float4 Camera; float4 ColorDensity; float4 Fog; float4 Grade; float4 Post;
        };
        Output VSMain(uint id : SV_VertexID) {
            Output o; float2 uv = float2((id << 1) & 2, id & 2);
            o.Position = float4(uv * float2(2,-2) + float2(-1,1), 0, 1);
            o.Settings=Settings; o.R0=R0; o.R1=R1; o.R2=R2; o.R3=R3;
            o.Camera=Camera; o.ColorDensity=ColorDensity; o.Fog=Fog; o.Grade=Grade; o.Post=Post; return o;
        }
        """;
    internal const string Pixel = Shared + """
        // Enable only in an isolated qualification build until paired hardware
        // timings and native image review accept the shared-linear variant.
        #ifndef COMPOSITION_SHARED_LINEAR
        #define COMPOSITION_SHARED_LINEAR 0
        #endif
        Texture2D<float4> Scene : register(t0);
        #if ATMOSPHERE || UPSAMPLE
        Texture2D<float> Depth : register(t1);
        #endif
        float3 Decode(float3 value) {
            return lerp(pow((value+0.055)/1.055,2.4),value/12.92,step(value,0.04045));
        }
        float3 Encode(float3 value) {
            return lerp(1.055*pow(max(value,0),1.0/2.4)-0.055,value*12.92,step(value,0.0031308));
        }
        float Luma(float3 value) { return dot(value,float3(0.2126,0.7152,0.0722)); }
        float ColorDistance(float3 a, float3 b) {
            float3 delta=abs(a-b); return max(delta.r,max(delta.g,delta.b));
        }
        float4 NormalizeWeights(float4 weights, uint nearestIndex) {
            float total=dot(weights,1);
            if(all(isfinite(weights)) && all(weights>=0) && isfinite(total) && total>=1.17549435e-38)
                return weights/total;
            return float4(nearestIndex==0,nearestIndex==1,nearestIndex==2,nearestIndex==3);
        }
        bool CanShareNeighborhood(int2 origin, uint2 dimensions) {
            // Clamping a center before adding its AA offsets is not equivalent
            // to clamping a shared tile when an edge collapses two centers.
            return all(origin>=0) && all(origin+1<int2(dimensions));
        }
        float DepthContinuity(float depth, float referenceDepth) {
            float tolerance=max(0.00001,(1-referenceDepth)*0.05);
            return saturate(1-abs(depth-referenceDepth)/tolerance);
        }
        float3 FilterDisplayLinear(float3 value, float3 north, float3 south,
            float3 west, float3 east, float edgeBlend) {
            float3 low=min(value,min(min(north,south),min(west,east)));
            float3 high=max(value,max(max(north,south),max(west,east)));
            float range=ColorDistance(high,low);
            // Chroma-aware detection also covers equal-luminance material edges.
            if(range>max(0.025,max(high.r,max(high.g,high.b))*0.125)) {
                bool vertical=ColorDistance(north,south)<ColorDistance(west,east);
                float3 first=vertical ? north : west, second=vertical ? south : east;
                bool detail=dot(value-first,value-second)>0 && ColorDistance(first,second)<range*0.25;
                float blend=edgeBlend*(detail ? 0.5 : 1);
                value=lerp(value,(first+second)*0.5,blend);
            }
            return value;
        }
        #if LOCAL_HAZE
        float HazeField(float3 p) {
            float3 delta=(p-HazeCenter)/HazeRadius;
            float density=saturate(1-dot(delta,delta));
            return density*density;
        }
        float HazeOpticalDepth(float3 camera,float3 position,float startDistance) {
            float3 ray=position-camera; float rayLength=length(ray);
            if(rayLength<=startDistance || rayLength<.0001) return 0;
            float3 start=camera+ray*(startDistance/rayLength);
            float integral=(HazeField(start)+4*HazeField((start+position)*.5)+HazeField(position))/6;
            return HazeDensity*(rayLength-startDistance)*integral;
        }
        #endif
        float3 DisplayLinear(float4 scene, int2 pixel, Output input, uint2 dimensions) {
            // Authored output-space clear/debug pixels bypass fog, grade and tone mapping.
            if(scene.a<0) return Decode(scene.rgb);
            float3 value=scene.rgb;
        #if ATMOSPHERE
            float depth=Depth.Load(int3(pixel,0));
            if(input.Settings.z>0.5 && depth<1) {
                float2 uv=(float2(pixel)+0.5)/float2(dimensions);
                float4 world=mul(float4(uv*float2(2,-2)+float2(-1,1),depth,1),
                    float4x4(input.R0,input.R1,input.R2,input.R3));
                if(abs(world.w)>0.000001) {
                    float3 position=world.xyz/world.w;
                    float length=max(0,distance(position,input.Camera.xyz)-input.Fog.x);
                    float heightDensity=1;
                    if(input.Settings.w>1.5) {
                        float heightSpan=abs(position.y-input.Camera.y)*input.Fog.z;
                        float integral=heightSpan<0.001 ? 1-heightSpan*0.5 : (1-exp(-heightSpan))/heightSpan;
                        heightDensity=exp(clamp(-(min(position.y,input.Camera.y)-input.Fog.y)*input.Fog.z,-40,5))*integral;
                    }
                    float opticalDepth=length*input.ColorDensity.w*heightDensity;
                #if LOCAL_HAZE
                    opticalDepth+=HazeOpticalDepth(input.Camera.xyz,position,input.Fog.x);
                #endif
                    float opacity=min(input.Fog.w,1-exp(-min(80,opticalDepth)));
                    value=lerp(value,input.ColorDensity.rgb,opacity);
                }
            }
        #endif
            value*=input.Settings.x*float3(1+input.Grade.x,1+input.Grade.y,1-input.Grade.x);
            if(input.Settings.y>=0.5)
                value=saturate((value*(2.51*value+0.03))/(value*(2.43*value+0.59)+0.14));
            else value=saturate(value);
            value=saturate(lerp(Luma(value).xxx,value,input.Grade.w));
            return saturate((value-0.18)*input.Grade.z+0.18);
        }
        float3 SampleDisplayLinear(int2 pixel, Output input, uint2 dimensions) {
            pixel=clamp(pixel,int2(0,0),int2(dimensions)-1);
            return DisplayLinear(Scene.Load(int3(pixel,0)),pixel,input,dimensions);
        }
        float3 ConstrainNeighbor(float3 center, float3 neighbor, float centerDepth, float neighborDepth) {
            float continuity=DepthContinuity(neighborDepth,centerDepth);
            if(continuity<=0) return center;
            return lerp(center,neighbor,continuity);
        }
        float3 SampleAaNeighbor(int2 pixel, float3 center, float centerDepth, Output input, uint2 dimensions) {
            pixel=clamp(pixel,int2(0,0),int2(dimensions)-1);
        #if UPSAMPLE
            float continuity=DepthContinuity(Depth.Load(int3(pixel,0)),centerDepth);
            // Outer reconstruction weights cannot undo an AA sample that has
            // already mixed foreground color/fog into another surface.
            if(continuity<=0) return center;
            return lerp(center,SampleDisplayLinear(pixel,input,dimensions),continuity);
        #else
            return SampleDisplayLinear(pixel,input,dimensions);
        #endif
        }
        float4 ShadeDisplayLinear(float4 scene, int2 pixel, Output input, uint2 dimensions) {
            if(scene.a<0) return float4(Decode(scene.rgb),-scene.a-1);
            float3 value=DisplayLinear(scene,pixel,input,dimensions);
            if(input.Post.x>0) {
                float centerDepth=1;
            #if UPSAMPLE
                centerDepth=Depth.Load(int3(pixel,0));
            #endif
                float3 north=SampleAaNeighbor(pixel+int2(0,-1),value,centerDepth,input,dimensions);
                float3 south=SampleAaNeighbor(pixel+int2(0,1),value,centerDepth,input,dimensions);
                float3 west=SampleAaNeighbor(pixel+int2(-1,0),value,centerDepth,input,dimensions);
                float3 east=SampleAaNeighbor(pixel+int2(1,0),value,centerDepth,input,dimensions);
                value=FilterDisplayLinear(value,north,south,west,east,input.Post.x);
            }
            return float4(value,scene.a);
        }
        float4 ShadePixel(int2 pixel, Output input, uint2 dimensions) {
            float4 scene=Scene.Load(int3(pixel,0));
            // Native authored output retains its exact original value and alpha.
            if(scene.a<0) return float4(scene.rgb,-scene.a-1);
            float4 value=ShadeDisplayLinear(scene,pixel,input,dimensions);
            return float4(Encode(value.rgb),value.a);
        }
        #if UPSAMPLE
        #if COMPOSITION_SHARED_LINEAR
        void ShadeSharedNeighborhood(int2 origin, Output input, uint2 dimensions, float4 depths,
            out float4 a, out float4 b, out float4 c, out float4 d) {
            float4 sa=Scene.Load(int3(origin,0));
            float4 sb=Scene.Load(int3(origin+int2(1,0),0));
            float4 sc=Scene.Load(int3(origin+int2(0,1),0));
            float4 sd=Scene.Load(int3(origin+int2(1,1),0));
            float3 ca=DisplayLinear(sa,origin,input,dimensions);
            float3 cb=DisplayLinear(sb,origin+int2(1,0),input,dimensions);
            float3 cc=DisplayLinear(sc,origin+int2(0,1),input,dimensions);
            float3 cd=DisplayLinear(sd,origin+int2(1,1),input,dimensions);
            a=float4(ca,sa.a<0 ? -sa.a-1 : sa.a);
            b=float4(cb,sb.a<0 ? -sb.a-1 : sb.a);
            c=float4(cc,sc.a<0 ? -sc.a-1 : sc.a);
            d=float4(cd,sd.a<0 ? -sd.a-1 : sd.a);
            if(input.Post.x<=0) return;
            // Four centers and eight exterior neighbors form the twelve-sample
            // union. Each center keeps its own fog, grade, alpha and AA decision.
            if(sa.a>=0) a.rgb=FilterDisplayLinear(ca,
                SampleAaNeighbor(origin+int2(0,-1),ca,depths.x,input,dimensions),
                ConstrainNeighbor(ca,cc,depths.x,depths.z),
                SampleAaNeighbor(origin+int2(-1,0),ca,depths.x,input,dimensions),
                ConstrainNeighbor(ca,cb,depths.x,depths.y),input.Post.x);
            if(sb.a>=0) b.rgb=FilterDisplayLinear(cb,
                SampleAaNeighbor(origin+int2(1,-1),cb,depths.y,input,dimensions),
                ConstrainNeighbor(cb,cd,depths.y,depths.w),
                ConstrainNeighbor(cb,ca,depths.y,depths.x),
                SampleAaNeighbor(origin+int2(2,0),cb,depths.y,input,dimensions),input.Post.x);
            if(sc.a>=0) c.rgb=FilterDisplayLinear(cc,
                ConstrainNeighbor(cc,ca,depths.z,depths.x),
                SampleAaNeighbor(origin+int2(0,2),cc,depths.z,input,dimensions),
                SampleAaNeighbor(origin+int2(-1,1),cc,depths.z,input,dimensions),
                ConstrainNeighbor(cc,cd,depths.z,depths.w),input.Post.x);
            if(sd.a>=0) d.rgb=FilterDisplayLinear(cd,
                ConstrainNeighbor(cd,cb,depths.w,depths.y),
                SampleAaNeighbor(origin+int2(1,2),cd,depths.w,input,dimensions),
                ConstrainNeighbor(cd,cc,depths.w,depths.z),
                SampleAaNeighbor(origin+int2(2,1),cd,depths.w,input,dimensions),input.Post.x);
        }
        #endif
        struct CompositeOutput { float4 Color:SV_Target0; float DepthValue:SV_Depth; };
        CompositeOutput PSMain(Output input) {
            uint width,height; Scene.GetDimensions(width,height); uint2 dimensions=uint2(width,height);
            float2 position=input.Position.xy*float2(dimensions)/input.Post.yz-0.5;
            int2 origin=int2(floor(position)); float2 weight=frac(position);
            int2 maximum=int2(dimensions)-1;
            int2 pa=clamp(origin,int2(0,0),maximum);
            int2 pb=clamp(origin+int2(1,0),int2(0,0),maximum);
            int2 pc=clamp(origin+int2(0,1),int2(0,0),maximum);
            int2 pd=clamp(origin+int2(1,1),int2(0,0),maximum);
            CompositeOutput output;
            // Interpolate display-linear colors after each sample's fog/tone/AA, including authored clear colors.
            int2 nearest=clamp(int2(floor(position+0.5)),int2(0,0),maximum);
            float referenceDepth=Depth.Load(int3(nearest,0));
            float4 depths=float4(Depth.Load(int3(pa,0)),Depth.Load(int3(pb,0)),
                Depth.Load(int3(pc,0)),Depth.Load(int3(pd,0)));
            float4 weights=float4((1-weight.x)*(1-weight.y),weight.x*(1-weight.y),
                (1-weight.x)*weight.y,weight.x*weight.y);
            // Reject cross-surface color bleeding in perspective depth. The tolerance
            // scales with remaining depth range and keeps sloping surfaces continuous.
            weights*=float4(DepthContinuity(depths.x,referenceDepth),DepthContinuity(depths.y,referenceDepth),
                DepthContinuity(depths.z,referenceDepth),DepthContinuity(depths.w,referenceDepth));
            uint nearestIndex=(weight.x>=0.5 ? 1u : 0u)+(weight.y>=0.5 ? 2u : 0u);
            weights=NormalizeWeights(weights,nearestIndex);
            float4 a,b,c,d;
        #if COMPOSITION_SHARED_LINEAR
            if(CanShareNeighborhood(origin,dimensions))
                ShadeSharedNeighborhood(origin,input,dimensions,depths,a,b,c,d);
            else {
                a=ShadeDisplayLinear(Scene.Load(int3(pa,0)),pa,input,dimensions);
                b=ShadeDisplayLinear(Scene.Load(int3(pb,0)),pb,input,dimensions);
                c=ShadeDisplayLinear(Scene.Load(int3(pc,0)),pc,input,dimensions);
                d=ShadeDisplayLinear(Scene.Load(int3(pd,0)),pd,input,dimensions);
            }
        #else
            a=ShadePixel(pa,input,dimensions); a.rgb=Decode(a.rgb);
            b=ShadePixel(pb,input,dimensions); b.rgb=Decode(b.rgb);
            c=ShadePixel(pc,input,dimensions); c.rgb=Decode(c.rgb);
            d=ShadePixel(pd,input,dimensions); d.rgb=Decode(d.rgb);
        #endif
            output.Color=float4(Encode(a.rgb*weights.x+b.rgb*weights.y+c.rgb*weights.z+d.rgb*weights.w),
                dot(float4(a.a,b.a,c.a,d.a),weights));
            output.DepthValue=referenceDepth;
            return output;
        }
        #else
        float4 PSMain(Output input):SV_Target0 {
            uint width,height; Scene.GetDimensions(width,height);
            return ShadePixel(int2(input.Position.xy),input,uint2(width,height));
        }
        #endif
        """;
}
