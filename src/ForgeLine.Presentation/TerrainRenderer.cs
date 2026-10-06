using System.Diagnostics;
using System.Numerics;
using ForgeLine.Assets;
using ForgeLine.Graphics;
using ForgeLine.World;

namespace ForgeLine.Presentation;

public enum TerrainDebugVisualizationMode : byte
{
    None = 0,
    ControlRed = 1,
    ControlGreen = 2,
    ControlBlue = 3,
    ControlAlpha = 4,
    MaterialPalette = 5
}

public sealed class TerrainRenderer : IDisposable
{
    private const int TerrainBaseRootConstantCount = 36;
    private const int TerrainRootConstantCount =
        TerrainBaseRootConstantCount +
        SceneLightingSettings.ShaderConstantCount;
    private const int TerrainTextureCount = 13;
    private const int LayerCount = TerrainChunkSplatData.LayerCount;

    private readonly IGraphicsPipeline _pipeline;
    private readonly TerrainChunkRenderResource[] _resources;
    private readonly RuntimeWorldAssetResources? _runtimeAssets;
    private readonly IGraphicsTexture? _fallbackBaseColor;
    private readonly IGraphicsTexture? _fallbackNormal;
    private readonly IGraphicsTexture? _fallbackOrm;
    private readonly SceneLightingSettings _lighting;

    private bool _disposed;

    public TerrainRenderer(
        IGraphicsDevice graphics,
        TerrainWorld world,
        TerrainMeshSettings? meshSettings = null,
        TerrainPresentationProfile? presentationProfile = null,
        RuntimeAssetCatalog? runtimeAssets = null,
        SceneLightingSettings? lighting = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(world);

        _lighting =
            lighting ??
            SceneLightingSettings.Default;
        _lighting.Validate();

        TerrainMeshSettings resolvedMeshSettings =
            meshSettings ??
            new TerrainMeshSettings();
        TerrainPresentationProfile resolvedProfile =
            presentationProfile ??
            TerrainPresentationProfile.CreateCentralDivide(
                runtimeAssets);
        resolvedMeshSettings.Validate();

        _pipeline =
            CreateTerrainPipeline(
                graphics);
        _runtimeAssets =
            runtimeAssets is null
                ? null
                : new RuntimeWorldAssetResources(
                    graphics,
                    runtimeAssets);

        if (_runtimeAssets is null)
        {
            _fallbackBaseColor =
                CreateSolidTexture(
                    graphics,
                    255,
                    255,
                    255,
                    255,
                    GraphicsTextureColorSpace.Srgb);
            _fallbackNormal =
                CreateSolidTexture(
                    graphics,
                    128,
                    128,
                    255,
                    255,
                    GraphicsTextureColorSpace.Linear);
            _fallbackOrm =
                CreateSolidTexture(
                    graphics,
                    255,
                    255,
                    0,
                    255,
                    GraphicsTextureColorSpace.Linear);
        }

        var resources =
            new List<TerrainChunkRenderResource>(
                world.Chunks.Count);

        try
        {
            foreach (TerrainChunk chunk in world.Chunks)
            {
                TerrainMeshData mesh =
                    TerrainMeshGenerator.Generate(
                        chunk,
                        resolvedMeshSettings);
                TerrainChunkSplatData splat =
                    TerrainSplatMapBuilder.Build(
                        chunk,
                        resolvedProfile);
                TerrainRenderVertex[] renderVertices =
                    CreateRenderVertices(
                        mesh.Vertices);
                IGraphicsTexture controlTexture =
                    graphics.CreateTexture(
                        splat.CreateTextureData());
                TerrainLayerResource[] layers =
                    CreateLayers(
                        splat.Palette,
                        resolvedProfile);

                ulong vertexBytes =
                    checked(
                        (ulong)renderVertices.Length *
                        TerrainRenderVertex.SizeInBytes);
                ulong indexBytes =
                    checked(
                        (ulong)mesh.Indices.Length *
                        sizeof(uint));

                IGraphicsBuffer vertexBuffer =
                    graphics.CreateBuffer(
                        new GraphicsBufferDescription(
                            vertexBytes,
                            GraphicsBufferMemory.Upload));
                IGraphicsBuffer indexBuffer =
                    graphics.CreateBuffer(
                        new GraphicsBufferDescription(
                            indexBytes,
                            GraphicsBufferMemory.Upload));

                try
                {
                    vertexBuffer.SetData<TerrainRenderVertex>(
                        renderVertices);
                    indexBuffer.SetData<uint>(
                        mesh.Indices);

                    resources.Add(
                        new TerrainChunkRenderResource(
                            chunk.Coordinate,
                            mesh.Bounds,
                            vertexBuffer,
                            indexBuffer,
                            controlTexture,
                            layers,
                            mesh.Indices.Length,
                            mesh.TriangleCount));
                }
                catch
                {
                    indexBuffer.Dispose();
                    vertexBuffer.Dispose();
                    controlTexture.Dispose();
                    throw;
                }
            }

            _resources =
                resources.ToArray();
        }
        catch
        {
            foreach (TerrainChunkRenderResource resource in resources)
            {
                resource.Dispose();
            }

            _fallbackOrm?.Dispose();
            _fallbackNormal?.Dispose();
            _fallbackBaseColor?.Dispose();
            _runtimeAssets?.Dispose();
            _pipeline.Dispose();
            throw;
        }

        LastDiagnostics =
            new TerrainRenderDiagnostics(
                _resources.Length,
                0,
                _resources.Length,
                0,
                0,
                checked(
                    _resources.Length *
                    2),
                _resources.Length,
                TerrainTextureCount,
                TerrainTextureCount,
                0.0);
    }

    public bool DebugChunksEnabled { get; set; } = true;

    public TerrainDebugVisualizationMode DebugVisualizationMode { get; set; }

    public SceneLightingSettings Lighting =>
        _lighting;

    public TerrainRenderDiagnostics LastDiagnostics { get; private set; }

    public RuntimeMaterialDiagnostics MaterialDiagnostics =>
        _runtimeAssets?.MaterialDiagnostics ??
        default;

    public void Render(
        IGraphicsCommandContext context,
        RtsCamera camera)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(camera);

        long startedAt =
            Stopwatch.GetTimestamp();
        CameraMatrices matrices =
            camera.GetMatrices(
                context.Width,
                context.Height);
        ViewFrustum frustum =
            ViewFrustum.FromViewProjection(
                matrices.ViewProjection);

        int visibleChunks = 0;
        int drawCalls = 0;
        long submittedTriangles = 0;

        context.SetPipeline(
            _pipeline);

        Span<float> constants =
            stackalloc float[
                TerrainRootConstantCount];
        WriteMatrix(
            matrices.ViewProjection,
            constants);
        _lighting.WriteShaderConstants(
            constants[
                TerrainBaseRootConstantCount..]);

        foreach (TerrainChunkRenderResource resource in _resources)
        {
            if (!frustum.Intersects(
                    resource.Bounds))
            {
                continue;
            }

            visibleChunks++;
            submittedTriangles +=
                resource.TriangleCount;

            constants[16] =
                DebugChunksEnabled
                    ? 1.0f
                    : 0.0f;
            constants[17] =
                (float)DebugVisualizationMode;
            constants[18] =
                0.0f;
            constants[19] =
                0.0f;
            WriteLayerConstants(
                resource.Layers,
                constants);

            context.SetVertexConstants(
                constants);
            context.SetVertexBuffer(
                resource.VertexBuffer,
                TerrainRenderVertex.SizeInBytes);
            context.SetIndexBuffer(
                resource.IndexBuffer,
                GraphicsIndexFormat.ThirtyTwoBit);

            BindTerrainTextures(
                context,
                resource);

            context.DrawIndexed(
                resource.IndexCount);
            drawCalls++;
        }

        double submissionMilliseconds =
            Stopwatch.GetElapsedTime(
                    startedAt)
                .TotalMilliseconds;

        LastDiagnostics =
            new TerrainRenderDiagnostics(
                _resources.Length,
                visibleChunks,
                _resources.Length -
                visibleChunks,
                submittedTriangles,
                drawCalls,
                checked(
                    _resources.Length *
                    2),
                _resources.Length,
                TerrainTextureCount,
                TerrainTextureCount,
                submissionMilliseconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (TerrainChunkRenderResource resource in _resources)
        {
            resource.Dispose();
        }

        _fallbackOrm?.Dispose();
        _fallbackNormal?.Dispose();
        _fallbackBaseColor?.Dispose();
        _runtimeAssets?.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }

    private TerrainLayerResource[] CreateLayers(
        IReadOnlyList<TerrainMaterialSlot> palette,
        TerrainPresentationProfile profile)
    {
        var layers =
            new TerrainLayerResource[
                LayerCount];

        for (int index = 0;
             index < layers.Length;
             index++)
        {
            TerrainMaterialSlot slot =
                palette[index];
            TerrainMaterialDefinition definition =
                profile.GetMaterial(
                    slot);
            RuntimeMaterialResources material =
                _runtimeAssets is null
                    ? CreateFallbackMaterial(
                        definition)
                    : _runtimeAssets.ResolveMaterial(
                        definition.AssetId);
            float tileMeters =
                float.IsFinite(
                    definition.TextureTileMeters) &&
                definition.TextureTileMeters >
                    0.0f
                    ? definition.TextureTileMeters
                    : 128.0f;
            Vector2 worldUvScale =
                material.UvScale /
                tileMeters;

            layers[index] =
                new TerrainLayerResource(
                    slot,
                    material,
                    worldUvScale);
        }

        return layers;
    }

    private RuntimeMaterialResources CreateFallbackMaterial(
        in TerrainMaterialDefinition definition) =>
        new(
            AssetId.Parse(
                definition.AssetId),
            _fallbackBaseColor!,
            _fallbackNormal!,
            _fallbackOrm!,
            _fallbackBaseColor!,
            new Vector4(
                definition.BaseColor,
                1.0f),
            definition.Roughness,
            definition.Metallic,
            0.0f,
            Vector2.One,
            false);

    private static void BindTerrainTextures(
        IGraphicsCommandContext context,
        TerrainChunkRenderResource resource)
    {
        context.SetPixelTexture(
            0,
            resource.ControlTexture);

        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            RuntimeMaterialResources material =
                resource.Layers[layer]
                    .Material;

            context.SetPixelTexture(
                1 +
                layer,
                material.BaseColorTexture);
            context.SetPixelTexture(
                5 +
                layer,
                material.NormalTexture);
            context.SetPixelTexture(
                9 +
                layer,
                material.OrmTexture);
        }
    }

    private static IGraphicsPipeline CreateTerrainPipeline(
        IGraphicsDevice graphics)
    {
        const string vertexShaderSource = """
            cbuffer TerrainFrame : register(b0)
            {
                row_major float4x4 ViewProjection;
                float DebugChunks;
                float DebugMode;
                uint PackedPaletteSlots;
                float TerrainPadding;
                float4 LayerUvScale01;
                float4 LayerUvScale23;
                uint4 LayerBaseColorPacked;
                uint4 LayerSurfacePacked;
                float4 SceneLightDirectionIntensity;
                float4 SceneDirectionalColorAmbientIntensity;
                float4 SceneAmbientColorExposure;
                float2 SceneGroundAmbientToneMapping;
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
                output.Normal =
                    input.Normal;
                output.LocalUv =
                    input.LocalUv;
                output.DebugChunks =
                    DebugChunks;
                output.MacroTint =
                    input.MacroTint;
                output.DebugMode =
                    DebugMode;
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

        const string pixelShaderSource = """
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

            float3 ToneMapAces(float3 value)
            {
                const float a = 2.51f;
                const float b = 0.03f;
                const float c = 2.43f;
                const float d = 0.59f;
                const float e = 0.14f;

                return saturate(
                    (value *
                        (a * value + b)) /
                    (value *
                        (c * value + d) +
                     e));
            }

            float3 LinearToSrgb(float3 value)
            {
                value =
                    max(
                        value,
                        0.0f);
                float3 low =
                    value *
                    12.92f;
                float3 high =
                    1.055f *
                    pow(
                        max(
                            value,
                            0.0031308f),
                        1.0f / 2.4f) -
                    0.055f;

                return lerp(
                    low,
                    high,
                    step(
                        float3(
                            0.0031308f,
                            0.0031308f,
                            0.0031308f),
                        value));
            }

            float3 ApplySceneLighting(
                float3 albedo,
                float3 worldNormal,
                float ambientOcclusion,
                float roughness,
                float metallic,
                float3 emissive,
                float4 sceneLight,
                float4 sceneDirectionalAmbient,
                float4 sceneAmbientExposure,
                float2 sceneGroundTone)
            {
                float3 normal =
                    normalize(
                        worldNormal);
                float lightFacing =
                    saturate(
                        dot(
                            normal,
                            normalize(
                                sceneLight.xyz)));
                float hemisphere =
                    saturate(
                        normal.y *
                            0.5f +
                        0.5f);
                float ambientShape =
                    lerp(
                        sceneGroundTone.x,
                        1.0f,
                        hemisphere);
                float aoResponse =
                    lerp(
                        0.55f,
                        1.0f,
                        saturate(
                            ambientOcclusion));
                float3 ambient =
                    sceneAmbientExposure.rgb *
                    sceneDirectionalAmbient.w *
                    ambientShape *
                    aoResponse;
                float3 direct =
                    sceneDirectionalAmbient.rgb *
                    sceneLight.w *
                    lightFacing;
                float diffuseEnergy =
                    lerp(
                        1.0f,
                        0.72f,
                        saturate(
                            metallic));
                float roughnessResponse =
                    lerp(
                        1.02f,
                        0.94f,
                        saturate(
                            roughness));
                float3 linearColor =
                    albedo *
                    (ambient + direct) *
                    diffuseEnergy *
                    roughnessResponse +
                    emissive;

                linearColor *=
                    sceneAmbientExposure.w;

                if (sceneGroundTone.y >= 0.5f)
                {
                    linearColor =
                        ToneMapAces(
                            linearColor);
                }
                else
                {
                    linearColor =
                        saturate(
                            linearColor);
                }

                return LinearToSrgb(
                    saturate(
                        linearColor));
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
                        1.0f);
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
                        1.0f);
                }

                float2 worldXZ =
                    input.WorldPosition.xz;
                float2 uv0 =
                    worldXZ *
                    input.LayerUvScale01.xy;
                float2 uv1 =
                    worldXZ *
                    input.LayerUvScale01.zw;
                float2 uv2 =
                    worldXZ *
                    input.LayerUvScale23.xy;
                float2 uv3 =
                    worldXZ *
                    input.LayerUvScale23.zw;

                float3 baseFactor0 =
                    UnpackRgb10(
                        input.LayerBaseColorPacked.x);
                float3 baseFactor1 =
                    UnpackRgb10(
                        input.LayerBaseColorPacked.y);
                float3 baseFactor2 =
                    UnpackRgb10(
                        input.LayerBaseColorPacked.z);
                float3 baseFactor3 =
                    UnpackRgb10(
                        input.LayerBaseColorPacked.w);

                float3 base0 =
                    LayerBase0.Sample(
                        WorldMaterialSampler,
                        uv0).rgb *
                    baseFactor0;
                float3 base1 =
                    LayerBase1.Sample(
                        WorldMaterialSampler,
                        uv1).rgb *
                    baseFactor1;
                float3 base2 =
                    LayerBase2.Sample(
                        WorldMaterialSampler,
                        uv2).rgb *
                    baseFactor2;
                float3 base3 =
                    LayerBase3.Sample(
                        WorldMaterialSampler,
                        uv3).rgb *
                    baseFactor3;

                float3 geometricNormal =
                    normalize(
                        input.Normal);
                float3 tangent =
                    float3(
                        1.0f,
                        0.0f,
                        0.0f) -
                    geometricNormal *
                    geometricNormal.x;

                if (dot(tangent, tangent) <
                    0.0001f)
                {
                    tangent =
                        float3(
                            0.0f,
                            0.0f,
                            1.0f) -
                        geometricNormal *
                        geometricNormal.z;
                }

                tangent =
                    normalize(
                        tangent);
                float3 bitangent =
                    normalize(
                        cross(
                            geometricNormal,
                            tangent)) *
                    -1.0f;

                float3 normal0 =
                    DecodeNormal(
                        LayerNormal0.Sample(
                            WorldMaterialSampler,
                            uv0).xyz);
                float3 normal1 =
                    DecodeNormal(
                        LayerNormal1.Sample(
                            WorldMaterialSampler,
                            uv1).xyz);
                float3 normal2 =
                    DecodeNormal(
                        LayerNormal2.Sample(
                            WorldMaterialSampler,
                            uv2).xyz);
                float3 normal3 =
                    DecodeNormal(
                        LayerNormal3.Sample(
                            WorldMaterialSampler,
                            uv3).xyz);

                normal0 =
                    normalize(
                        tangent * normal0.x +
                        bitangent * normal0.y +
                        geometricNormal * normal0.z);
                normal1 =
                    normalize(
                        tangent * normal1.x +
                        bitangent * normal1.y +
                        geometricNormal * normal1.z);
                normal2 =
                    normalize(
                        tangent * normal2.x +
                        bitangent * normal2.y +
                        geometricNormal * normal2.z);
                normal3 =
                    normalize(
                        tangent * normal3.x +
                        bitangent * normal3.y +
                        geometricNormal * normal3.z);

                float3 blendedNormal =
                    normalize(
                        normal0 *
                            weights.x +
                        normal1 *
                            weights.y +
                        normal2 *
                            weights.z +
                        normal3 *
                            weights.w);

                float3 orm0 =
                    LayerOrm0.Sample(
                        WorldMaterialSampler,
                        uv0).rgb;
                float3 orm1 =
                    LayerOrm1.Sample(
                        WorldMaterialSampler,
                        uv1).rgb;
                float3 orm2 =
                    LayerOrm2.Sample(
                        WorldMaterialSampler,
                        uv2).rgb;
                float3 orm3 =
                    LayerOrm3.Sample(
                        WorldMaterialSampler,
                        uv3).rgb;
                float3 orm =
                    orm0 *
                        weights.x +
                    orm1 *
                        weights.y +
                    orm2 *
                        weights.z +
                    orm3 *
                        weights.w;

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

                color =
                    ApplySceneLighting(
                        color,
                        blendedNormal,
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
                        input.SceneGroundTone);

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
                            1.0f);
                    }
                }

                return float4(
                    color,
                    1.0f);
            }
            """;

        var compiler =
            new DxcShaderCompiler();
        GraphicsShaderBytecode vertexShader =
            compiler.Compile(
                vertexShaderSource,
                GraphicsShaderStage.Vertex,
                "VSMain",
                "TerrainVertex.hlsl");
        GraphicsShaderBytecode pixelShader =
            compiler.Compile(
                pixelShaderSource,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "TerrainPixel.hlsl");

        return graphics.CreateGraphicsPipeline(
            new GraphicsPipelineDescription(
                vertexShader,
                pixelShader)
            {
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

    private static TerrainRenderVertex[] CreateRenderVertices(
        TerrainVertex[] vertices)
    {
        var result =
            new TerrainRenderVertex[
                vertices.Length];

        for (int index = 0;
             index < result.Length;
             index++)
        {
            TerrainVertex vertex =
                vertices[index];
            float macro =
                CalculateMacroVariation(
                    vertex.Position);

            result[index] =
                new TerrainRenderVertex(
                    vertex.Position,
                    vertex.Normal,
                    vertex.LocalUv,
                    new Vector4(
                        macro,
                        macro,
                        macro,
                        1.0f));
        }

        return result;
    }

    internal static float CalculateMacroVariation(
        Vector3 position)
    {
        float broad =
            MathF.Sin(
                position.X *
                    0.0019f +
                position.Z *
                    0.0013f);
        float secondary =
            MathF.Sin(
                position.X *
                    0.0047f -
                position.Z *
                    0.0039f +
                MathF.Sin(
                    position.Z *
                        0.0011f));

        return Math.Clamp(
            1.0f +
            broad *
                0.055f +
            secondary *
                0.025f,
            0.90f,
            1.08f);
    }

    private static void WriteLayerConstants(
        IReadOnlyList<TerrainLayerResource> layers,
        Span<float> constants)
    {
        if (layers.Count !=
            LayerCount)
        {
            throw new InvalidOperationException(
                $"Terrain rendering requires {LayerCount} active material layers.");
        }

        for (int layer = 0;
             layer < LayerCount;
             layer++)
        {
            TerrainLayerResource resource =
                layers[layer];
            RuntimeMaterialResources material =
                resource.Material;
            int uvOffset =
                20 +
                layer *
                2;
            constants[uvOffset] =
                resource.WorldUvScale.X;
            constants[uvOffset + 1] =
                resource.WorldUvScale.Y;

            constants[28 + layer] =
                BitConverter.UInt32BitsToSingle(
                    PackRgb10(
                        material.BaseColorFactor));
            constants[32 + layer] =
                BitConverter.UInt32BitsToSingle(
                    PackRg16(
                        material.RoughnessFactor,
                        material.MetallicFactor));
        }

        uint packedPalette =
            (uint)layers[0].Slot |
            ((uint)layers[1].Slot << 8) |
            ((uint)layers[2].Slot << 16) |
            ((uint)layers[3].Slot << 24);
        constants[18] =
            BitConverter.UInt32BitsToSingle(
                packedPalette);
    }

    private static uint PackRgb10(
        in Vector4 color)
    {
        uint red =
            PackUnorm(
                color.X,
                1_023);
        uint green =
            PackUnorm(
                color.Y,
                1_023);
        uint blue =
            PackUnorm(
                color.Z,
                1_023);

        return red |
            (green << 10) |
            (blue << 20);
    }

    private static uint PackRg16(
        float first,
        float second)
    {
        uint x =
            PackUnorm(
                first,
                65_535);
        uint y =
            PackUnorm(
                second,
                65_535);

        return x |
            (y << 16);
    }

    private static uint PackUnorm(
        float value,
        uint maximum)
    {
        float clamped =
            Math.Clamp(
                value,
                0.0f,
                1.0f);

        return checked(
            (uint)MathF.Round(
                clamped *
                maximum));
    }

    private readonly record struct TerrainRenderVertex(
        Vector3 Position,
        Vector3 Normal,
        Vector2 LocalUv,
        Vector4 MacroTint)
    {
        public const int SizeInBytes = 48;
    }

    private static IGraphicsTexture CreateSolidTexture(
        IGraphicsDevice graphics,
        byte red,
        byte green,
        byte blue,
        byte alpha,
        GraphicsTextureColorSpace colorSpace) =>
        graphics.CreateTexture(
            new GraphicsTextureData(
                new GraphicsTextureDescription(
                    1,
                    1,
                    GraphicsTextureFormat.Rgba8Unorm,
                    colorSpace,
                    1),
                [
                    new GraphicsTextureMipData(
                        1,
                        1,
                        4,
                        [
                            red,
                            green,
                            blue,
                            alpha
                        ])
                ]));

    private static void WriteMatrix(
        Matrix4x4 matrix,
        Span<float> destination)
    {
        destination[0] = matrix.M11;
        destination[1] = matrix.M12;
        destination[2] = matrix.M13;
        destination[3] = matrix.M14;
        destination[4] = matrix.M21;
        destination[5] = matrix.M22;
        destination[6] = matrix.M23;
        destination[7] = matrix.M24;
        destination[8] = matrix.M31;
        destination[9] = matrix.M32;
        destination[10] = matrix.M33;
        destination[11] = matrix.M34;
        destination[12] = matrix.M41;
        destination[13] = matrix.M42;
        destination[14] = matrix.M43;
        destination[15] = matrix.M44;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    private readonly record struct TerrainLayerResource(
        TerrainMaterialSlot Slot,
        RuntimeMaterialResources Material,
        Vector2 WorldUvScale);

    private sealed class TerrainChunkRenderResource : IDisposable
    {
        internal TerrainChunkRenderResource(
            ChunkCoordinate coordinate,
            AxisAlignedBounds bounds,
            IGraphicsBuffer vertexBuffer,
            IGraphicsBuffer indexBuffer,
            IGraphicsTexture controlTexture,
            TerrainLayerResource[] layers,
            int indexCount,
            int triangleCount)
        {
            Coordinate =
                coordinate;
            Bounds =
                bounds;
            VertexBuffer =
                vertexBuffer;
            IndexBuffer =
                indexBuffer;
            ControlTexture =
                controlTexture;
            Layers =
                layers ??
                throw new ArgumentNullException(
                    nameof(layers));
            IndexCount =
                indexCount;
            TriangleCount =
                triangleCount;
        }

        internal ChunkCoordinate Coordinate { get; }

        internal AxisAlignedBounds Bounds { get; }

        internal IGraphicsBuffer VertexBuffer { get; }

        internal IGraphicsBuffer IndexBuffer { get; }

        internal IGraphicsTexture ControlTexture { get; }

        internal TerrainLayerResource[] Layers { get; }

        internal int IndexCount { get; }

        internal int TriangleCount { get; }

        public void Dispose()
        {
            ControlTexture.Dispose();
            IndexBuffer.Dispose();
            VertexBuffer.Dispose();
        }
    }
}
