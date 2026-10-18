using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

internal static class InstanceMaterialPipeline
{
    private const int MatrixRootConstantCount = 16;
    private const int RootConstantCount = MatrixRootConstantCount + SceneLightingSettings.ShaderConstantCount + 5;

    internal static IGraphicsPipeline CreatePipeline(
        IGraphicsDevice graphics, WorldEnvironmentSettings environment)
    {
        const string vertexShaderSource = """
            cbuffer InstanceFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                float4 SceneLightDirectionIntensity;
                float4 SceneDirectionalColorAmbientIntensity;
                float4 SceneAmbientColorExposure;
                float2 SceneGroundAmbientToneMapping;
                float2 FramePadding;
                float3 CameraPosition;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
                float3 Normal : NORMAL;
                float4 WorldRow0 : INSTANCEWORLD0;
                float4 WorldRow1 : INSTANCEWORLD1;
                float4 WorldRow2 : INSTANCEWORLD2;
                float4 WorldRow3 : INSTANCEWORLD3;
                float4 Color : INSTANCECOLOR0;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float3 WorldPosition : TEXCOORD14;
                float3 WorldView : TEXCOORD15;
                float3 WorldNormal : TEXCOORD0;
                nointerpolation float4 SceneLight : TEXCOORD1;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD2;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD3;
                nointerpolation float2 SceneGroundTone : TEXCOORD4;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                row_major float4x4 world =
                    float4x4(
                        input.WorldRow0,
                        input.WorldRow1,
                        input.WorldRow2,
                        input.WorldRow3);
                float4 worldPosition =
                    mul(
                        float4(
                            input.Position,
                            1.0f),
                        world);
                float3x3 worldBasis =
                    (float3x3)world;

                output.WorldPosition = worldPosition.xyz;
                output.WorldView = CameraPosition - worldPosition.xyz;
                output.Position =
                    mul(
                        worldPosition,
                        ViewProjection);
                output.Color =
                    input.Color;
                float orientation;
                output.WorldNormal = PbrTransformNormal(input.Normal, worldBasis, orientation);
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

        const string pixelShaderSource = """
            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float3 WorldPosition : TEXCOORD14;
                float3 WorldView : TEXCOORD15;
                float3 WorldNormal : TEXCOORD0;
                nointerpolation float4 SceneLight : TEXCOORD1;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD2;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD3;
                nointerpolation float2 SceneGroundTone : TEXCOORD4;
            };

            float4 PSMain(PixelInput input) : SV_Target0
            {
                float3 color =
                    ApplySceneLighting(
                        input.Color.rgb,
                        input.WorldNormal,
                        1.0f,
                        0.82f,
                        0.0f,
                        0.0f,
                        input.SceneLight,
                        input.SceneDirectionalAmbient,
                        input.SceneAmbientExposure,
                        input.SceneGroundTone,
                        input.WorldPosition, input.WorldView);

                return float4(
                    color,
                    input.Color.a);
            }
            """;

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                PbrMaterialShader.Transforms + vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "InstanceVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                environment.ShaderConstants() + (graphics.SceneOutput.Enabled ? "#define LINEAR_SCENE 1\n" : "#define LINEAR_SCENE 0\n") + (graphics.DirectionalShadows.Enabled ? DirectionalShadowShader.Source : DirectionalShadowShader.Disabled) + PbrMaterialShader.Lighting + pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "InstancePixel.hlsl");

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
                        "INSTANCEWORLD",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        0,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        1,
                        GraphicsVertexElementFormat.Float4,
                        16,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        2,
                        GraphicsVertexElementFormat.Float4,
                        32,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        3,
                        GraphicsVertexElementFormat.Float4,
                        48,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCECOLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        64,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1)
                ],
                VertexRootConstantCount =
                    RootConstantCount,
                DepthEnabled =
                    true
            });
    }

    internal static IGraphicsPipeline CreateTexturedPipeline(
        IGraphicsDevice graphics, WorldEnvironmentSettings environment)
    {
        const string vertexShaderSource = """
            cbuffer InstanceFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                float4 SceneLightDirectionIntensity;
                float4 SceneDirectionalColorAmbientIntensity;
                float4 SceneAmbientColorExposure;
                float2 SceneGroundAmbientToneMapping;
                float2 FramePadding;
                float3 CameraPosition;
            };

            struct VertexInput
            {
                float3 Position : POSITION;
                float3 Normal : NORMAL;
                float2 Uv : TEXCOORD0;
                float4 Tangent : TANGENT;
                float4 WorldRow0 : INSTANCEWORLD0;
                float4 WorldRow1 : INSTANCEWORLD1;
                float4 WorldRow2 : INSTANCEWORLD2;
                float4 WorldRow3 : INSTANCEWORLD3;
                float4 Color : INSTANCECOLOR0;
                float4 Material0 : INSTANCEMATERIAL0;
                float4 Material1 : INSTANCEMATERIAL1;
            };

            struct VertexOutput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float3 WorldPosition : TEXCOORD14;
                float3 WorldView : TEXCOORD15;
                float2 Uv : TEXCOORD0;
                float3 WorldNormal : TEXCOORD1;
                float4 WorldTangent : TEXCOORD2;
                float4 Material0 : TEXCOORD3;
                float4 Material1 : TEXCOORD4;
                nointerpolation float4 SceneLight : TEXCOORD5;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD6;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD7;
                nointerpolation float2 SceneGroundTone : TEXCOORD8;
            };

            VertexOutput VSMain(VertexInput input)
            {
                VertexOutput output;
                row_major float4x4 world =
                    float4x4(
                        input.WorldRow0,
                        input.WorldRow1,
                        input.WorldRow2,
                        input.WorldRow3);
                float4 worldPosition =
                    mul(
                        float4(
                            input.Position,
                            1.0f),
                        world);
                float3x3 worldBasis =
                    (float3x3)world;

                output.WorldPosition = worldPosition.xyz;
                output.WorldView = CameraPosition - worldPosition.xyz;
                output.Position =
                    mul(
                        worldPosition,
                        ViewProjection);
                output.Color =
                    input.Color;
                output.Uv =
                    input.Uv;
                float orientation;
                output.WorldNormal = PbrTransformNormal(input.Normal, worldBasis, orientation);
                output.WorldTangent =
                    float4(
                        mul(
                            input.Tangent.xyz,
                            worldBasis),
                        input.Tangent.w * orientation);
                output.Material0 =
                    input.Material0;
                output.Material1 =
                    input.Material1;
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

        const string pixelShaderSource = """
            Texture2D BaseColorTexture : register(t0);
            Texture2D NormalTexture : register(t1);
            Texture2D OrmTexture : register(t2);
            Texture2D EmissiveTexture : register(t3);
            SamplerState WorldMaterialSampler : register(s0);

            struct PixelInput
            {
                float4 Position : SV_Position;
                float4 Color : COLOR0;
                float3 WorldPosition : TEXCOORD14;
                float3 WorldView : TEXCOORD15;
                float2 Uv : TEXCOORD0;
                float3 WorldNormal : TEXCOORD1;
                float4 WorldTangent : TEXCOORD2;
                float4 Material0 : TEXCOORD3;
                float4 Material1 : TEXCOORD4;
                nointerpolation float4 SceneLight : TEXCOORD5;
                nointerpolation float4 SceneDirectionalAmbient : TEXCOORD6;
                nointerpolation float4 SceneAmbientExposure : TEXCOORD7;
                nointerpolation float2 SceneGroundTone : TEXCOORD8;
            };

            float3 BuildFallbackTangent(float3 normal)
            {
                float3 axis =
                    abs(normal.y) < 0.999f
                        ? float3(0.0f, 1.0f, 0.0f)
                        : float3(1.0f, 0.0f, 0.0f);
                return normalize(
                    cross(
                        axis,
                        normal));
            }

            float4 PSMain(PixelInput input) : SV_Target0
            {
                float2 uv =
                    input.Uv *
                    input.Material0.xy + input.Material1.yz;
                float4 baseColor =
                    BaseColorTexture.Sample(
                        WorldMaterialSampler,
                        uv);
                // Ordered coverage preserves transparent sprite/decal edges in
                // the existing depth-tested instanced pass without sorting.
                static const float coverageThresholds[16] = {
                    0.03125f, 0.53125f, 0.15625f, 0.65625f,
                    0.78125f, 0.28125f, 0.90625f, 0.40625f,
                    0.21875f, 0.71875f, 0.09375f, 0.59375f,
                    0.96875f, 0.46875f, 0.84375f, 0.34375f
                };
                uint2 coveragePixel = uint2(input.Position.xy) & 3;
                clip(baseColor.a * input.Color.a -
                    coverageThresholds[coveragePixel.y * 4 + coveragePixel.x]);
                float3 tangentNormal =
                    NormalTexture.Sample(
                        WorldMaterialSampler,
                        uv).xyz *
                    2.0f -
                    1.0f;
                float3 orm =
                    OrmTexture.Sample(
                        WorldMaterialSampler,
                        uv).rgb;
                float3 emissive =
                    EmissiveTexture.Sample(
                        WorldMaterialSampler,
                        uv).rgb;

                float3 normal =
                    normalize(
                        input.WorldNormal);
                float3 tangent =
                    input.WorldTangent.xyz -
                    normal *
                    dot(
                        normal,
                        input.WorldTangent.xyz);
                tangent =
                    dot(
                        tangent,
                        tangent) >
                    1e-8f
                        ? normalize(tangent)
                        : BuildFallbackTangent(
                            normal);
                float handedness =
                    input.WorldTangent.w < 0.0f
                        ? -1.0f
                        : 1.0f;
                float3 bitangent =
                    normalize(
                        cross(
                            normal,
                            tangent)) *
                    handedness;
                float3 worldNormal =
                    normalize(
                        tangent *
                        tangentNormal.x +
                        bitangent *
                        tangentNormal.y +
                        normal *
                        tangentNormal.z);

                float ambientOcclusion =
                    saturate(
                        orm.r);
                float roughness =
                    saturate(
                        orm.g *
                        input.Material0.z);
                float metallic =
                    saturate(
                        orm.b *
                        input.Material0.w);
                float3 color =
                    ApplySceneLighting(
                        baseColor.rgb *
                        input.Color.rgb,
                        worldNormal,
                        ambientOcclusion,
                        roughness,
                        metallic,
                        emissive *
                        input.Material1.x,
                        input.SceneLight,
                        input.SceneDirectionalAmbient,
                        input.SceneAmbientExposure,
                        input.SceneGroundTone,
                        input.WorldPosition, input.WorldView);

                return float4(
                    color,
                    baseColor.a *
                    input.Color.a);
            }
            """;

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                PbrMaterialShader.Transforms + vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "InstanceMaterialVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                environment.ShaderConstants() + (graphics.SceneOutput.Enabled ? "#define LINEAR_SCENE 1\n" : "#define LINEAR_SCENE 0\n") + (graphics.DirectionalShadows.Enabled ? DirectionalShadowShader.Source : DirectionalShadowShader.Disabled) + PbrMaterialShader.Lighting + pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "InstanceMaterialPixel.hlsl");

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
                        "TANGENT",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        32),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        0,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        1,
                        GraphicsVertexElementFormat.Float4,
                        16,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        2,
                        GraphicsVertexElementFormat.Float4,
                        32,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEWORLD",
                        3,
                        GraphicsVertexElementFormat.Float4,
                        48,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCECOLOR",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        64,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEMATERIAL",
                        0,
                        GraphicsVertexElementFormat.Float4,
                        80,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1),
                    new GraphicsVertexElement(
                        "INSTANCEMATERIAL",
                        1,
                        GraphicsVertexElementFormat.Float4,
                        96,
                        1,
                        GraphicsVertexInputRate.PerInstance,
                        1)
                ],
                VertexRootConstantCount =
                    RootConstantCount,
                PixelTextureCount =
                    4,
                // Imported glTF surfaces use counterclockwise outward winding.
                CullMode = GraphicsCullMode.Clockwise,
                DepthEnabled =
                    true
            });
    }

}
