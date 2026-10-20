using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class TerrainMaterialPipeline
{
    private const int TerrainBaseRootConstantCount = 36;
    private const int TerrainRootConstantCount = TerrainBaseRootConstantCount + SceneLightingSettings.ShaderConstantCount + 1;
    private const int TerrainTextureCount = 13;

    internal static IGraphicsPipeline CreateTerrainPipeline(
        IGraphicsDevice graphics, TerrainSurfaceDetailSettings surfaceDetail, WorldEnvironmentSettings environment)
    {
        const string vertexShaderSource = """
            cbuffer TerrainFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                uint DebugFlags;
                float CameraX;
                uint PackedPaletteSlots;
                float CameraY;
                float4 LayerUvScale01;
                float4 LayerUvScale23;
                uint4 LayerBaseColorPacked;
                uint4 LayerSurfacePacked;
                float4 SceneLightDirectionIntensity;
                float4 SceneDirectionalColorAmbientIntensity;
                float4 SceneAmbientColorExposure;
                float2 SceneGroundAmbientToneMapping;
                float CameraZ;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
                float3 Normal : NORMAL;
                float2 LocalUv : TEXCOORD0;
                float4 MacroTint : COLOR0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float3 WorldPosition : TEXCOORD0;
                float3 WorldView : TEXCOORD14;
                float3 Normal : TEXCOORD1;
                float2 LocalUv : TEXCOORD2;
                float DebugChunks : TEXCOORD3;
                float4 MacroTint : COLOR0;
                nointerpolation float DebugMode : TEXCOORD4;
                nointerpolation float4 LayerUvScale01 : TEXCOORD5;
                nointerpolation float4 LayerUvScale23 : TEXCOORD6;
                nointerpolation uint4 LayerBaseColorPacked : TEXCOORD7;
                nointerpolation uint4 LayerSurfacePacked : TEXCOORD8;
                nointerpolation uint PackedPaletteSlots : TEXCOORD9;
                nointerpolation float4 SceneLight : TEXCOORD10;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD11;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD12;
                nointerpolation float2 SceneGroundTone : TEXCOORD13;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                output.Position =
                    mul(
                        float4(
                            input.Position,
                            1.0f),
                        ViewProjection);
                output.WorldPosition =
                    input.Position;
                output.WorldView = float3(CameraX, CameraY, CameraZ) - input.Position;
                output.Normal =
                    input.Normal;
                output.LocalUv =
                    input.LocalUv;
                output.DebugChunks =
                    (DebugFlags & 1) != 0 ? 1.0f : 0.0f;
                output.MacroTint =
                    input.MacroTint;
                output.DebugMode =
                    float(DebugFlags >> 1);
                output.LayerUvScale01 =
                    LayerUvScale01;
                output.LayerUvScale23 =
                    LayerUvScale23;
                output.LayerBaseColorPacked =
                    LayerBaseColorPacked;
                output.LayerSurfacePacked =
                    LayerSurfacePacked;
                output.PackedPaletteSlots =
                    PackedPaletteSlots;
                output.SceneLight =
                    SceneLightDirectionIntensity;
                output.SceneDirectionalAmbient =
                    SceneDirectionalColorAmbientIntensity;
                output.SceneAmbientExposure =
                    SceneAmbientColorExposure;
                output.SceneGroundTone =
                    SceneGroundAmbientToneMapping;
                return output;
            }
            """;

        string pixelShaderSource = """
            Texture2D ControlTexture : register(t0);
            Texture2D LayerBase0 : register(t1);
            Texture2D LayerBase1 : register(t2);
            Texture2D LayerBase2 : register(t3);
            Texture2D LayerBase3 : register(t4);
            Texture2D LayerNormal0 : register(t5);
            Texture2D LayerNormal1 : register(t6);
            Texture2D LayerNormal2 : register(t7);
            Texture2D LayerNormal3 : register(t8);
            Texture2D LayerOrm0 : register(t9);
            Texture2D LayerOrm1 : register(t10);
            Texture2D LayerOrm2 : register(t11);
            Texture2D LayerOrm3 : register(t12);

            SamplerState WorldMaterialSampler : register(s0);
            SamplerState ClampSampler : register(s1);

            struct PixelInput
            {
                float4 Position : SV_Position;
                float3 WorldPosition : TEXCOORD0;
                float3 WorldView : TEXCOORD14;
                float3 Normal : TEXCOORD1;
                float2 LocalUv : TEXCOORD2;
                float DebugChunks : TEXCOORD3;
                float4 MacroTint : COLOR0;
                nointerpolation float DebugMode : TEXCOORD4;
                nointerpolation float4 LayerUvScale01 : TEXCOORD5;
                nointerpolation float4 LayerUvScale23 : TEXCOORD6;
                nointerpolation uint4 LayerBaseColorPacked : TEXCOORD7;
                nointerpolation uint4 LayerSurfacePacked : TEXCOORD8;
                nointerpolation uint PackedPaletteSlots : TEXCOORD9;
                nointerpolation float4 SceneLight : TEXCOORD10;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD11;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD12;
                nointerpolation float2 SceneGroundTone : TEXCOORD13;
            };

            float4 NormalizeWeights(float4 weights)
            {
                weights =
                    max(
                        weights,
                        0.0f);
                float total =
                    dot(
                        weights,
                        1.0f);

                return total > 0.00001f
                    ? weights /
                      total
                    : float4(
                        1.0f,
                        0.0f,
                        0.0f,
                        0.0f);
            }

            float3 DecodeNormal(float3 encoded)
            {
                return normalize(
                    encoded *
                    2.0f -
                    1.0f);
            }

            float3 PaletteColor(float slot)
            {
                float3 seed =
                    float3(
                        0.19f,
                        0.47f,
                        0.73f) *
                    (slot +
                     1.0f);

                return 0.28f +
                    0.72f *
                    frac(
                        seed);
            }

            float3 UnpackRgb10(uint packed)
            {
                return float3(
                    packed & 0x3ffu,
                    (packed >> 10) & 0x3ffu,
                    (packed >> 20) & 0x3ffu) /
                    1023.0f;
            }

            float2 UnpackRg16(uint packed)
            {
                return float2(
                    packed & 0xffffu,
                    (packed >> 16) & 0xffffu) /
                    65535.0f;
            }

            float4 PSMain(PixelInput input) : SV_Target0
            {
                float4 weights =
                    NormalizeWeights(
                        ControlTexture.Sample(
                            ClampSampler,
                            saturate(
                                input.LocalUv)));

                if (input.DebugMode >= 0.5f &&
                    input.DebugMode < 4.5f)
                {
                    int channel =
                        (int)round(
                            input.DebugMode -
                            1.0f);
                    float value =
                        channel == 0
                            ? weights.x
                            : channel == 1
                                ? weights.y
                                : channel == 2
                                    ? weights.z
                                    : weights.w;
                    return float4(
                        value,
                        value,
                        value,
                        DEBUG_ALPHA);
                }

                if (input.DebugMode >= 4.5f)
                {
                    float3 palette =
                        PaletteColor(
                            input.PackedPaletteSlots &
                            0xffu) *
                            weights.x +
                        PaletteColor(
                            (input.PackedPaletteSlots >>
                             8) &
                            0xffu) *
                            weights.y +
                        PaletteColor(
                            (input.PackedPaletteSlots >>
                             16) &
                            0xffu) *
                            weights.z +
                        PaletteColor(
                            (input.PackedPaletteSlots >>
                             24) &
                            0xffu) *
                            weights.w;
                    return float4(
                        palette,
                        DEBUG_ALPHA);
                }

                float2 worldXZ =
                    input.WorldPosition.xz;
                float3 geometricNormal = PbrNormalize(input.Normal, float3(0, 1, 0));
                float3 tangent = float3(1, 0, 0) - geometricNormal * geometricNormal.x;
                if (dot(tangent, tangent) < .0001) tangent = float3(0, 0, 1) - geometricNormal * geometricNormal.z;
                tangent = normalize(tangent);
                float3 bitangent = -normalize(cross(geometricNormal, tangent));
                float3 dx = ddx(input.WorldPosition);
                float3 dy = ddy(input.WorldPosition);
                TerrainSurface layer0 = SampleTerrainSurface(LayerBase0, LayerNormal0, LayerOrm0,
                    input.WorldPosition, geometricNormal, input.LayerUvScale01.xy, input.PackedPaletteSlots & 255,
                    weights.x, dx, dy, tangent, bitangent);
                TerrainSurface layer1 = SampleTerrainSurface(LayerBase1, LayerNormal1, LayerOrm1,
                    input.WorldPosition, geometricNormal, input.LayerUvScale01.zw, (input.PackedPaletteSlots >> 8) & 255,
                    weights.y, dx, dy, tangent, bitangent);
                TerrainSurface layer2 = SampleTerrainSurface(LayerBase2, LayerNormal2, LayerOrm2,
                    input.WorldPosition, geometricNormal, input.LayerUvScale23.xy, (input.PackedPaletteSlots >> 16) & 255,
                    weights.z, dx, dy, tangent, bitangent);
                TerrainSurface layer3 = SampleTerrainSurface(LayerBase3, LayerNormal3, LayerOrm3,
                    input.WorldPosition, geometricNormal, input.LayerUvScale23.zw, (input.PackedPaletteSlots >> 24) & 255,
                    weights.w, dx, dy, tangent, bitangent);
                float3 base0 = layer0.Color * UnpackRgb10(input.LayerBaseColorPacked.x);
                float3 base1 = layer1.Color * UnpackRgb10(input.LayerBaseColorPacked.y);
                float3 base2 = layer2.Color * UnpackRgb10(input.LayerBaseColorPacked.z);
                float3 base3 = layer3.Color * UnpackRgb10(input.LayerBaseColorPacked.w);
                float3 blendedNormal = normalize(layer0.Normal * weights.x + layer1.Normal * weights.y
                    + layer2.Normal * weights.z + layer3.Normal * weights.w);
                float3 orm = layer0.Orm * weights.x + layer1.Orm * weights.y + layer2.Orm * weights.z + layer3.Orm * weights.w;

                float2 surface0 =
                    UnpackRg16(
                        input.LayerSurfacePacked.x);
                float2 surface1 =
                    UnpackRg16(
                        input.LayerSurfacePacked.y);
                float2 surface2 =
                    UnpackRg16(
                        input.LayerSurfacePacked.z);
                float2 surface3 =
                    UnpackRg16(
                        input.LayerSurfacePacked.w);
                float roughnessFactor =
                    surface0.x *
                        weights.x +
                    surface1.x *
                        weights.y +
                    surface2.x *
                        weights.z +
                    surface3.x *
                        weights.w;
                float metallicFactor =
                    surface0.y *
                        weights.x +
                    surface1.y *
                        weights.y +
                    surface2.y *
                        weights.z +
                    surface3.y *
                        weights.w;
                float roughness =
                    saturate(
                        orm.g *
                        roughnessFactor);
                float metallic =
                    saturate(
                        orm.b *
                        metallicFactor);
                float ao =
                    saturate(
                        orm.r);

                float3 color =
                    (base0 *
                        weights.x +
                     base1 *
                        weights.y +
                     base2 *
                        weights.z +
                     base3 *
                        weights.w) *
                    input.MacroTint.rgb;

                ApplyTerrainMicroDetail(input.WorldPosition, dx, dy, color, blendedNormal);

                color =
                    ApplySceneLighting(
                        color,
                        blendedNormal,
                        geometricNormal,
                        ao,
                        roughness,
                        metallic,
                        float3(
                            0.0f,
                            0.0f,
                            0.0f),
                        input.SceneLight,
                        input.SceneDirectionalAmbient,
                        input.SceneAmbientExposure,
                        input.SceneGroundTone,
                        input.WorldPosition, input.WorldView);

                if (input.DebugChunks > 0.5f)
                {
                    float edgeDistance =
                        min(
                            min(
                                input.LocalUv.x,
                                1.0f -
                                input.LocalUv.x),
                            min(
                                input.LocalUv.y,
                                1.0f -
                                input.LocalUv.y));

                    if (edgeDistance < 0.0125f)
                    {
                        return float4(
                            1.0f,
                            0.55f,
                            0.08f,
                            DEBUG_ALPHA);
                    }
                }

                return float4(
                    color,
                    1.0f);
            }
            """;

        pixelShaderSource = pixelShaderSource.Replace("float4 PSMain(PixelInput input)",
            TerrainSurfaceShader.Source + "\nfloat4 PSMain(PixelInput input)", StringComparison.Ordinal);

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                PbrMaterialShader.Transforms + vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "TerrainVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                environment.ShaderConstants() + (graphics.SceneOutput.Enabled ? "#define LINEAR_SCENE 1\n#define DEBUG_ALPHA -2.0f\n" : "#define LINEAR_SCENE 0\n#define DEBUG_ALPHA 1.0f\n") + (graphics.DirectionalShadows.Enabled ? DirectionalShadowShader.Source : DirectionalShadowShader.Disabled) + TerrainSurfaceShader.Constants(surfaceDetail) + PbrMaterialShader.Lighting + pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "TerrainPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                vertexShader,
                pixelShader)
            {
                VertexConstantsInBuffer = graphics.DirectionalShadows.Enabled,
                DirectionalShadowReceiver = graphics.DirectionalShadows.Enabled,
                TargetFormat = graphics.SceneOutput.Enabled ? GraphicsFrameTargetFormat.Rgba16Float : GraphicsFrameTargetFormat.Rgba8Unorm,
                VertexElements =
                [
                    new GraphicsVertexElement(
                        "POSITION",
                        0,
                        GraphicsVertexElementFormat.Float3,
                        0),
                    new GraphicsVertexElement(
                        "NORMAL",
                        0,
                        GraphicsVertexElementFormat.Float3,
                        12),
                    new GraphicsVertexElement(
                        "TEXCOORD",
                        0,
                        GraphicsVertexElementFormat.Float2,
                        24),
                    new GraphicsVertexElement(
                        "COLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        32)
                ],
                VertexRootConstantCount =
                    TerrainRootConstantCount,
                PixelTextureCount =
                    TerrainTextureCount,
                CullMode =
                    GraphicsCullMode.None,
                DepthEnabled =
                    true
            });
    }

}
