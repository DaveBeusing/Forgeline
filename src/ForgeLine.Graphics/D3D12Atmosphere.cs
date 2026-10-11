using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private GraphicsAtmosphereSettings _atmosphere = GraphicsAtmosphereSettings.Default;
    private int _atmosphereDepthDescriptor = -1;
    private GraphicsAtmosphereView? _atmosphereView;
    private bool _recordedAtmosphereApplied, _submittedAtmosphereApplied;
    private float _recordedAtmosphereDensity, _submittedAtmosphereDensity;

    public GraphicsAtmosphereSettings Atmosphere { get { ThrowIfDisposed(); return _atmosphere; } }

    public void ConfigureAtmosphere(GraphicsAtmosphereSettings settings)
    {
        ThrowIfDisposed(); ThrowIfRecording(); settings.Validate();
        if (settings == _atmosphere) return;
        if (settings.Enabled && !_sceneOutput.Enabled)
            throw new InvalidOperationException("Atmosphere requires linear scene composition.");
        WaitForIdle();
        ReleaseRenderTargets();
        _sceneComposite?.Dispose(); _sceneComposite = null;
        _atmosphere = settings;
        ResetFrameMeasurements();
        try
        {
            CreateRenderTargets(_surfaceLifecycle.Width, _surfaceLifecycle.Height);
            if (_sceneOutput.Enabled) _sceneComposite = CreateSceneComposite();
        }
        catch (Exception exception)
        {
            ReleaseRenderTargets();
            _failure = new GraphicsDeviceException("Atmospheric render-target configuration failed.", exception);
            throw;
        }
    }

    internal void SetAtmosphereView(GraphicsAtmosphereView view)
    {
        ValidateRecording(); view.Validate();
        if (!_atmosphere.Enabled || _passState.Current != GraphicsFramePass.World || _worldDrawRecorded || _shadowRecording)
            throw new InvalidOperationException("Set the atmospheric camera before world submission.");
        _atmosphereView = view;
    }

    private GraphicsAtmosphereDiagnostics AtmosphereDiagnostics => new(_atmosphere.Quality,
        _submittedAtmosphereApplied, _atmosphereDepthDescriptor >= 0 ? 1 : 0, 0, _submittedAtmosphereDensity,
        _cpuTimingSubmission, _gpuTimingFence, _atmosphere.Enabled ? _compositeGpuMilliseconds : null,
        !_atmosphere.Enabled ? "Atmospheric fog is disabled." : !_submittedAtmosphereApplied ? "No atmospheric camera was submitted." : null)
        { RequiredScenePayloadBytes = _atmosphere.Enabled ? checked((long)_surfaceLifecycle.Width * _surfaceLifecycle.Height * 8 * _configuration.BufferCount) : 0 };

    private void CreateAtmosphereDepthView()
    {
        if (!_atmosphere.Enabled && !_particleDepth) return;
        _atmosphereDepthDescriptor = _shaderResourceDescriptors.Allocate();
        _device.CreateShaderResourceView(_depthTarget!, new ShaderResourceViewDescription
        {
            Format = Format.R32_Float, ViewDimension = ShaderResourceViewDimension.Texture2D,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Texture2D = new Texture2DShaderResourceView { MipLevels = 1 }
        }, new CpuDescriptorHandle(_shaderResourceHeap.GetCPUDescriptorHandleForHeapStart(),
            _atmosphereDepthDescriptor, _shaderResourceDescriptorSize));
        _peakShaderResourceDescriptorsUsed = Math.Max(_peakShaderResourceDescriptorsUsed, _shaderResourceDescriptors.UsedCount);
    }

    private void ReleaseAtmosphereDepthView()
    {
        if (_atmosphereDepthDescriptor < 0) return;
        _shaderResourceDescriptors.Release(_atmosphereDepthDescriptor);
        _atmosphereDepthDescriptor = -1;
    }

    private void WriteAtmosphereConstants(Span<float> values)
    {
        values.Clear();
        values[0] = _sceneOutput.Exposure; values[1] = _sceneOutput.AcesFitted ? 1 : 0;
        _recordedAtmosphereApplied = _atmosphereView.HasValue;
        if (!_atmosphereView.HasValue) return;
        var view = _atmosphereView.Value;
        values[2] = 1; values[3] = (float)_atmosphere.Quality;
        Matrix4x4 inverse = view.InverseViewProjection;
        MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(ref inverse, 1)).CopyTo(values[4..20]);
        values[20] = view.CameraPosition.X; values[21] = view.CameraPosition.Y; values[22] = view.CameraPosition.Z;
        float compensation = Math.Clamp(420 / view.CameraDistance, 0.35f, 2);
        _recordedAtmosphereDensity = _atmosphere.Density * float.Lerp(1, compensation, _atmosphere.ZoomCompensation);
        values[24] = _atmosphere.Color.X; values[25] = _atmosphere.Color.Y; values[26] = _atmosphere.Color.Z;
        values[27] = _recordedAtmosphereDensity;
        values[28] = _atmosphere.StartDistance; values[29] = _atmosphere.BaseHeight;
        values[30] = _atmosphere.HeightFalloff; values[31] = _atmosphere.MaximumOpacity;
    }

    private D3D12GraphicsPipeline CreateAtmosphereComposite()
    {
        const string shared = """
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
            };
            """;
        var compiler = new DxcShaderCompiler();
        var vertex = compiler.Compile(shared + """
            cbuffer Constants : register(b0) {
                float4 Settings; float4 R0; float4 R1; float4 R2; float4 R3;
                float4 Camera; float4 ColorDensity; float4 Fog;
            };
            Output VSMain(uint id : SV_VertexID) {
                Output o; float2 uv = float2((id << 1) & 2, id & 2);
                o.Position = float4(uv * float2(2,-2) + float2(-1,1), 0, 1);
                o.Settings = Settings; o.R0=R0; o.R1=R1; o.R2=R2; o.R3=R3;
                o.Camera=Camera; o.ColorDensity=ColorDensity; o.Fog=Fog; return o;
            }
            """, GraphicsShaderStage.Vertex, "VSMain", "AtmosphereCompositeVertex.hlsl");
        var pixel = compiler.Compile(shared + """
            Texture2D<float4> Scene : register(t0);
            Texture2D<float> Depth : register(t1);
            float4 PSMain(Output input) : SV_Target0 {
                int2 pixel = int2(input.Position.xy);
                float4 scene = Scene.Load(int3(pixel,0));
                if (scene.a < 0) return float4(scene.rgb, -scene.a - 1);
                float3 value = scene.rgb;
                float depth = Depth.Load(int3(pixel,0));
                if (input.Settings.z > 0.5 && depth < 1) {
                    uint width, height; Scene.GetDimensions(width,height);
                    float2 uv = input.Position.xy / float2(width,height);
                    float4 world = mul(float4(uv * float2(2,-2) + float2(-1,1), depth, 1),
                        float4x4(input.R0,input.R1,input.R2,input.R3));
                    if (abs(world.w) > 0.000001) {
                        float3 position = world.xyz / world.w;
                        float length = max(0, distance(position,input.Camera.xyz)-input.Fog.x);
                        float heightDensity = 1;
                        if (input.Settings.w > 1.5) {
                            float heightSpan = abs(position.y-input.Camera.y) * input.Fog.z;
                            float integral = heightSpan < 0.001 ? 1-heightSpan*0.5 : (1-exp(-heightSpan))/heightSpan;
                            heightDensity = exp(clamp(-(min(position.y,input.Camera.y)-input.Fog.y)*input.Fog.z,-40,5))*integral;
                        }
                        float opacity = min(input.Fog.w, 1-exp(-min(80,length*input.ColorDensity.w*heightDensity)));
                        value = lerp(value,input.ColorDensity.rgb,opacity);
                    }
                }
                value *= input.Settings.x;
                if (input.Settings.y >= 0.5)
                    value = saturate((value*(2.51*value+0.03))/(value*(2.43*value+0.59)+0.14));
                else value=saturate(value);
                float3 lo=value*12.92;
                float3 hi=1.055*pow(max(value,0),1.0/2.4)-0.055;
                return float4(lerp(hi,lo,step(value,0.0031308)),scene.a);
            }
            """, GraphicsShaderStage.Pixel, "PSMain", "AtmosphereCompositePixel.hlsl");
        return (D3D12GraphicsPipeline)CreateGraphicsPipeline(new(vertex,pixel)
        { CullMode=GraphicsCullMode.None, PixelTextureCount=2, VertexRootConstantCount=32 });
    }
}
