namespace ForgeLine.Presentation;

internal static class DirectionalShadowShader
{
    internal const string Disabled = "float2 SampleDirectionalShadow(float3 position, float3 normal) { return float2(1, 1); }\n";
    internal const string Source = """
        Texture2D<float> DirectionalDepth : register(t15);
        cbuffer ShadowFrame : register(b1) {
            row_major float4x4 ShadowProjection;
            float4 ShadowCenterRadius;
            float4 ShadowParameters;
        };
        float2 SampleDirectionalShadow(float3 position, float3 normal) {
            float4 projected = mul(float4(position, 1), ShadowProjection);
            float3 p = projected.xyz / projected.w;
            float2 uv = p.xy * float2(0.5, -0.5) + 0.5;
            // Evaluate quad derivatives before coverage and determinant branches.
            float2 du = ddx_fine(uv), dv = ddy_fine(uv);
            float dzdx = ddx_fine(p.z), dzdy = ddy_fine(p.z);
            if (any(uv <= 0) || any(uv >= 1) || p.z <= 0 || p.z >= 1) return float2(1, 1);
            float edge = max(abs(p.x), abs(p.y));
            float fade = 1 - smoothstep(0.82, 0.98, edge);
            fade *= 1 - smoothstep(0.8, 1, length(position.xz - ShadowCenterRadius.xz) / ShadowCenterRadius.w);
            float bias = (0.03 + 0.1 * (1 - saturate(normal.y))) / ShadowParameters.y;
            int size = (int)ShadowParameters.x;
            float2 texel = uv * size - 0.5;
            int2 pixel = int2(floor(texel));
            float2 fraction = frac(texel);
            float determinant = du.x * dv.y - du.y * dv.x;
            float2 plane = abs(determinant) > 1e-10 ?
                float2(dzdx * dv.y - dzdy * du.y, dzdy * du.x - dzdx * dv.x) / determinant : 0;
            float visibility = 0;
            float contact = 0;
            [unroll] for (int y = 0; y <= 1; y++) {
                [unroll] for (int x = 0; x <= 1; x++) {
                    float weight = (x == 0 ? 1 - fraction.x : fraction.x) * (y == 0 ? 1 - fraction.y : fraction.y);
                    float blocker = DirectionalDepth.Load(int3(clamp(pixel + int2(x, y), 0, size - 1), 0));
                    float receiver = p.z + dot(plane, (float2(pixel + int2(x, y)) + 0.5) / size - uv);
                    float gap = (receiver - blocker) * ShadowParameters.y;
                    visibility += (receiver - bias <= blocker ? 1 : 0) * weight;
                    contact += (gap > bias * ShadowParameters.y ? 1 - smoothstep(0.1, 1.5, gap) : 0) * weight;
                }
            }
            // Contact is local to an actual depth blocker and changes ambient by at most 15%.
            return float2(lerp(1, visibility, fade), 1 - contact * fade * ShadowParameters.z);
        }

        """;
}
