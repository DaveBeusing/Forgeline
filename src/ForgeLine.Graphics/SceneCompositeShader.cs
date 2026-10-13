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
                    float opacity=min(input.Fog.w,1-exp(-min(80,length*input.ColorDensity.w*heightDensity)));
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
        float4 ShadePixel(int2 pixel, Output input, uint2 dimensions) {
            float4 scene=Scene.Load(int3(pixel,0));
            if(scene.a<0) return float4(scene.rgb,-scene.a-1);
            float3 value=DisplayLinear(scene,pixel,input,dimensions);
            if(input.Post.x>0) {
                float3 north=SampleDisplayLinear(pixel+int2(0,-1),input,dimensions);
                float3 south=SampleDisplayLinear(pixel+int2(0,1),input,dimensions);
                float3 west=SampleDisplayLinear(pixel+int2(-1,0),input,dimensions);
                float3 east=SampleDisplayLinear(pixel+int2(1,0),input,dimensions);
                float3 low=min(value,min(min(north,south),min(west,east)));
                float3 high=max(value,max(max(north,south),max(west,east)));
                float range=ColorDistance(high,low);
                // Chroma-aware detection also covers equal-luminance material edges.
                if(range>max(0.025,max(high.r,max(high.g,high.b))*0.125)) {
                    bool vertical=ColorDistance(north,south)<ColorDistance(west,east);
                    float3 first=vertical ? north : west, second=vertical ? south : east;
                    // Preserve isolated bright/dark or colored one-pixel details more strongly.
                    bool detail=dot(value-first,value-second)>0 && ColorDistance(first,second)<range*0.25;
                    float blend=input.Post.x*(detail ? 0.5 : 1);
                    value=lerp(value,(first+second)*0.5,blend);
                }
            }
            return float4(Encode(value),scene.a);
        }
        #if UPSAMPLE
        struct CompositeOutput { float4 Color:SV_Target0; float DepthValue:SV_Depth; };
        CompositeOutput PSMain(Output input) {
            uint width,height; Scene.GetDimensions(width,height); uint2 dimensions=uint2(width,height);
            float2 position=input.Position.xy*float2(dimensions)/input.Post.yz-0.5;
            int2 origin=int2(floor(position)); float2 weight=frac(position);
            int2 maximum=int2(dimensions)-1;
            float4 a=ShadePixel(clamp(origin,int2(0,0),maximum),input,dimensions);
            float4 b=ShadePixel(clamp(origin+int2(1,0),int2(0,0),maximum),input,dimensions);
            float4 c=ShadePixel(clamp(origin+int2(0,1),int2(0,0),maximum),input,dimensions);
            float4 d=ShadePixel(clamp(origin+int2(1,1),int2(0,0),maximum),input,dimensions);
            CompositeOutput output;
            // Interpolate display-linear colors after each sample's fog/tone/AA, including authored clear colors.
            output.Color=float4(Encode(lerp(lerp(Decode(a.rgb),Decode(b.rgb),weight.x),
                lerp(Decode(c.rgb),Decode(d.rgb),weight.x),weight.y)),lerp(lerp(a.a,b.a,weight.x),lerp(c.a,d.a,weight.x),weight.y));
            int2 nearest=clamp(int2(floor(position+0.5)),int2(0,0),maximum);
            output.DepthValue=Depth.Load(int3(nearest,0));
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
