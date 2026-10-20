namespace ForgeLine.Presentation;

internal static class PbrMaterialShader
{
    internal const string Transforms = """
        float3 PbrNormalize(float3 value, float3 fallback) {
            float lengthSquared=dot(value,value);
            return isfinite(lengthSquared) && lengthSquared>1e-12 ? value*rsqrt(lengthSquared) : fallback;
        }
        float3 PbrTransformNormal(float3 normal, float3x3 basis, out float orientation) {
            float3 a=cross(basis[1],basis[2]);
            float3 b=cross(basis[2],basis[0]);
            float3 c=cross(basis[0],basis[1]);
            orientation=dot(basis[0],a)<0 ? -1 : 1;
            return PbrNormalize(mul(normal,float3x3(a,b,c))*orientation,float3(0,1,0));
        }
        """;

    internal const string Lighting = Transforms + MaterialFilteringShader.Source + """
        static const float PbrPi=3.14159265359;
        float PbrPow5(float value) { float square=value*value; return square*square*value; }
        float3 PbrFresnel(float3 f0,float cosine) { return f0+(1-f0)*PbrPow5(1-saturate(cosine)); }
        float3 PbrDirect(float3 albedo,float metallic,float roughness,float3 n,float3 v,float3 l) {
            float noL=saturate(dot(n,l)), noV=saturate(dot(n,v));
            if(noL<=0 || noV<=0) return 0;
            float3 h=PbrNormalize(v+l,n);
            float noH=saturate(dot(n,h)), voH=saturate(dot(v,h));
            float alpha=max(0.045,roughness); alpha*=alpha;
            float a2=alpha*alpha;
            float noH2=noH*noH;
            float denominator=(1-noH2)+a2*noH2;
            float distribution=a2/(PbrPi*denominator*denominator);
            float visibility=0.5/max(noL*sqrt(noV*noV*(1-a2)+a2)+noV*sqrt(noL*noL*(1-a2)+a2),1e-6);
            float3 fresnel=PbrFresnel(lerp(0.04.xxx,albedo,metallic),voH);
            float3 diffuse=(1-fresnel)*(1-metallic)*albedo/PbrPi;
            return (diffuse+distribution*visibility*fresnel)*noL;
        }
        float3 PbrEncode(float3 value) {
            value=max(value,0);
            return lerp(value*12.92,1.055*pow(max(value,0.0031308),1.0/2.4)-0.055,step(0.0031308.xxx,value));
        }
        float3 ApplySceneLighting(float3 albedo,float3 worldNormal,float3 geometricNormal,float ao,float roughness,float metallic,
            float3 emissive,float4 light,float4 directionalAmbient,float4 ambientExposure,float2 groundTone,
            float3 worldPosition,float3 viewVector) {
            float3 n=PbrNormalize(worldNormal,float3(0,1,0));
            float3 v=PbrNormalize(viewVector,n), l=PbrNormalize(light.xyz,float3(0,1,0));
            albedo=saturate(albedo); metallic=saturate(metallic); roughness=saturate(roughness);
        #if WORLD_ENVIRONMENT
            float wet=WorldWetness*saturate(n.y)*(1-metallic);
            albedo*=1-.15*wet;
            roughness=lerp(roughness,max(.28,roughness*.65),wet);
        #endif
            // Screen-space normal variance broadens subpixel specular lobes without history or extra samples.
            float3 dx=ddx(n), dy=ddy(n);
            roughness=PbrSpecularRoughness(roughness,dx,dy);
            float2 shadow=SampleDirectionalShadow(worldPosition,PbrNormalize(geometricNormal,n),l);
        #if WORLD_ENVIRONMENT
            // A fixed world-space cloud field avoids camera-relative shimmer and adds no texture reads.
            float cloud=.5+.5*sin(worldPosition.x*.0041+worldPosition.z*.0027)*sin(worldPosition.z*.0033-worldPosition.x*.0019);
            shadow.x*=1-.18*WorldCloudCover*cloud;
        #endif
            float3 direct=PbrDirect(albedo,metallic,roughness,n,v,l)*directionalAmbient.rgb*light.w*shadow.x;
            float3 f0=lerp(0.04.xxx,albedo,metallic);
            float3 ambientF=f0+(max((1-roughness).xxx,f0)-f0)*PbrPow5(1-saturate(dot(n,v)));
            float hemisphere=lerp(groundTone.x,1,saturate(n.y*0.5+0.5));
            // Bounded hemispheric sky approximation, not an environment-map/IBL integration.
            float3 ambient=((1-ambientF)*(1-metallic)*albedo+ambientF*(1-0.5*roughness))*
                ambientExposure.rgb*directionalAmbient.w*hemisphere*saturate(ao)*shadow.y;
            float3 color=max(0,direct+ambient+emissive);
        #if LINEAR_SCENE
            return color;
        #else
            color*=ambientExposure.w;
            if(groundTone.y>=0.5) color=saturate((color*(2.51*color+0.03))/(color*(2.43*color+0.59)+0.14));
            else color=saturate(color);
            return PbrEncode(color);
        #endif
        }
        float3 ApplySceneLighting(float3 albedo,float3 worldNormal,float ao,float roughness,float metallic,
            float3 emissive,float4 light,float4 directionalAmbient,float4 ambientExposure,float2 groundTone,
            float3 worldPosition,float3 viewVector) {
            return ApplySceneLighting(albedo,worldNormal,worldNormal,ao,roughness,metallic,emissive,
                light,directionalAmbient,ambientExposure,groundTone,worldPosition,viewVector);
        }
        """;
}
