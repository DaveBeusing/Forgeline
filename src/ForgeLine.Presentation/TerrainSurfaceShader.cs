using System.Globalization;

namespace ForgeLine.Presentation;

internal static class TerrainSurfaceShader
{
    internal static string Constants(TerrainSurfaceDetailSettings settings)
    {
        settings.Validate();
        return string.Create(CultureInfo.InvariantCulture, $"#define TERRAIN_DETAIL {(settings.Enabled ? 1 : 0)}\nstatic const float DetailTile = {settings.MicroTileMeters:R};\nstatic const float DetailColor = {settings.MicroColorStrength:R};\nstatic const float DetailNormal = {settings.MicroNormalStrength:R};\nstatic const float RockSlopeStart = {settings.RockSlopeStart:R};\nstatic const float RockSlopeEnd = {settings.RockSlopeEnd:R};\n");
    }

    internal const string Source = """
        struct TerrainSurface
        {
            float3 Color;
            float3 Normal;
            float3 Orm;
        };

        TerrainSurface SampleTerrainSurface(Texture2D baseMap, Texture2D normalMap, Texture2D ormMap,
            float3 p, float3 n, float2 scale, uint slot, float weight,
            float3 dx, float3 dy, float3 tangent, float3 bitangent)
        {
            float2 uv = p.xz * scale;
            float slopeBlend = 0;
            bool xFace = abs(n.x) >= abs(n.z);
        #if TERRAIN_DETAIL
            // The rock slot is uniform across the draw. Rotate its existing projection;
            // retain exactly three samples per layer, including on steep faces.
            if (slot == 3)
            {
                slopeBlend = smoothstep(RockSlopeStart, RockSlopeEnd, 1 - abs(n.y));
                float2 sideUv = (xFace ? p.zy : p.xy) * scale;
                uv = lerp(uv, sideUv, slopeBlend);
            }
        #endif
            // Derive the final projected coordinates before any per-pixel branch.
            float2 uvDx = ddx(uv);
            float2 uvDy = ddy(uv);
            TerrainSurface result;
            result.Color = baseMap.SampleGrad(WorldMaterialSampler, uv, uvDx, uvDy).rgb;
            float3 encoded = DecodeNormal(normalMap.SampleGrad(WorldMaterialSampler, uv, uvDx, uvDy).xyz);
            result.Normal = normalize(tangent * encoded.x + bitangent * encoded.y + n * encoded.z);
            result.Orm = ormMap.SampleGrad(WorldMaterialSampler, uv, uvDx, uvDy).rgb;
        #if TERRAIN_DETAIL
            float3 perturbation = xFace ? float3(0, encoded.y, encoded.x) : float3(encoded.x, encoded.y, 0);
            perturbation -= n * dot(perturbation, n);
            float3 sideNormal = normalize(n + perturbation * .35);
            result.Normal = normalize(lerp(result.Normal, sideNormal, slopeBlend));
        #endif
            return result;
        }

        void ApplyTerrainMicroDetail(float3 p, float3 dx, float3 dy, inout float3 color, inout float3 normal)
        {
        #if TERRAIN_DETAIL
            // Fade before the repeating detail approaches a pixel, independent of camera distance.
            float footprint = max(length(dx.xz), length(dy.xz)) / DetailTile;
            float fade = 1 - smoothstep(.08, .3, footprint);
            float2 phase = p.xz * (6.2831853 / DetailTile) * float2(1, .83);
            float2 waves = sin(phase);
            float variation = waves.x * waves.y;
            color *= 1 + DetailColor * fade * variation;
            float2 gradient = cos(phase) * waves.yx;
            float3 perturbation = float3(gradient.x, 0, gradient.y);
            perturbation -= normal * dot(perturbation, normal);
            normal = normalize(normal + DetailNormal * fade * perturbation);
        #endif
        }
        """;
}
