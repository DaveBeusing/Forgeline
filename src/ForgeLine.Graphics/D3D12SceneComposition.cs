using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private GraphicsSceneOutputSettings _sceneOutput = new(false, 1, true);
    private ID3D12Resource[]? _sceneTargets;
    private int[]? _sceneDescriptors;
    private D3D12GraphicsPipeline? _sceneComposite;
    private double? _compositeCpuMilliseconds;
    private double? _recordedCompositeCpuMilliseconds;

    public GraphicsSceneOutputSettings SceneOutput
    {
        get { ThrowIfDisposed(); return _sceneOutput; }
    }

    public void ConfigureSceneOutput(GraphicsSceneOutputSettings settings)
    {
        ThrowIfDisposed();
        ThrowIfRecording();
        settings.Validate();
        if (settings == _sceneOutput)
            return;
        WaitForIdle();
        ReleaseSceneTargets();
        _sceneComposite?.Dispose();
        _sceneComposite = null;
        _sceneOutput = settings;
        ResetFrameMeasurements();
        try
        {
            if (settings.Enabled)
            {
                CreateSceneTargets(_surfaceLifecycle.Width, _surfaceLifecycle.Height);
                _sceneComposite = CreateSceneComposite();
            }
        }
        catch
        {
            ReleaseSceneTargets();
            _sceneOutput = new(false, 1, true);
            throw;
        }
    }

    private CpuDescriptorHandle SceneRtv(int frame) => new(
        _rtvHeap.GetCPUDescriptorHandleForHeapStart(), frame + _configuration.BufferCount, _rtvDescriptorSize);

    private void CreateSceneTargets(int width, int height)
    {
        if (!_sceneOutput.Enabled)
            return;
        _sceneTargets = new ID3D12Resource[_configuration.BufferCount];
        _sceneDescriptors = new int[_configuration.BufferCount];
        Array.Fill(_sceneDescriptors, -1);
        try
        {
            for (int frame = 0; frame < _sceneTargets.Length; frame++)
            {
                ID3D12Resource target = _device.CreateCommittedResource(HeapType.Default,
                    ResourceDescription.Texture2D(Format.R16G16B16A16_Float, checked((uint)width), checked((uint)height),
                        mipLevels: 1, flags: ResourceFlags.AllowRenderTarget), ResourceStates.PixelShaderResource);
                _sceneTargets[frame] = target;
                target.Name = $"ForgeLine Linear Scene {frame}";
                _device.CreateRenderTargetView(target, null, SceneRtv(frame));
                int descriptor = _shaderResourceDescriptors.Allocate();
                _sceneDescriptors[frame] = descriptor;
                _device.CreateShaderResourceView(target, new ShaderResourceViewDescription
                {
                    Format = Format.R16G16B16A16_Float,
                    ViewDimension = Vortice.Direct3D12.ShaderResourceViewDimension.Texture2D,
                    Shader4ComponentMapping = ShaderComponentMapping.Default,
                    Texture2D = new Texture2DShaderResourceView { MipLevels = 1 }
                }, new CpuDescriptorHandle(_shaderResourceHeap.GetCPUDescriptorHandleForHeapStart(), descriptor,
                    _shaderResourceDescriptorSize));
            }
            _peakShaderResourceDescriptorsUsed = Math.Max(_peakShaderResourceDescriptorsUsed, _shaderResourceDescriptors.UsedCount);
        }
        catch
        {
            ReleaseSceneTargets();
            throw;
        }
    }

    // Surface-owned native targets are only released after idle or confirmed removal.
    private void ReleaseSceneTargets()
    {
        if (_sceneTargets is null)
            return;
        for (int frame = 0; frame < _sceneTargets.Length; frame++)
        {
            _sceneTargets[frame]?.Dispose();
            if (_sceneDescriptors![frame] >= 0)
                _shaderResourceDescriptors.Release(_sceneDescriptors[frame]);
        }
        _sceneTargets = null;
        _sceneDescriptors = null;
    }

    private D3D12GraphicsPipeline CreateSceneComposite()
    {
        var compiler = new DxcShaderCompiler();
        GraphicsShaderBytecode vertex = compiler.Compile("""
            cbuffer OutputSettings : register(b0) { float Exposure; float Aces; };
            struct Output { float4 Position : SV_Position; nointerpolation float2 Settings : TEXCOORD0; };
            Output VSMain(uint id : SV_VertexID) {
                Output o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                o.Settings = float2(Exposure, Aces);
                return o;
            }
            """, GraphicsShaderStage.Vertex, "VSMain", "SceneCompositeVertex.hlsl");
        GraphicsShaderBytecode pixel = compiler.Compile("""
            Texture2D<float4> Scene : register(t0);
            float4 PSMain(float4 position : SV_Position, nointerpolation float2 settings : TEXCOORD0) : SV_Target0 {
                float4 scene = Scene.Load(int3(int2(position.xy), 0));
                // A negative alpha identifies an authored output-space clear/debug pixel.
                if (scene.a < 0) return float4(scene.rgb, -scene.a - 1);
                float3 value = scene.rgb * settings.x;
                if (settings.y >= 0.5)
                    value = saturate((value * (2.51 * value + 0.03)) / (value * (2.43 * value + 0.59) + 0.14));
                else value = saturate(value);
                float3 lo = value * 12.92;
                float3 hi = 1.055 * pow(max(value, 0), 1.0 / 2.4) - 0.055;
                return float4(lerp(hi, lo, step(value, 0.0031308)), 1);
            }
            """, GraphicsShaderStage.Pixel, "PSMain", "SceneCompositePixel.hlsl");
        return (D3D12GraphicsPipeline)CreateGraphicsPipeline(new GraphicsPipelineDescription(vertex, pixel)
        {
            CullMode = GraphicsCullMode.None, PixelTextureCount = 1, VertexRootConstantCount = 2
        });
    }

    private void ComposeScene()
    {
        _recordedCompositeCpuMilliseconds = null;
        if (!_sceneOutput.Enabled)
            return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        _commandList.ResourceBarrierTransition(_sceneTargets![_frameIndex],
            ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        _commandList.OMSetRenderTargets(new CpuDescriptorHandle(_rtvHeap.GetCPUDescriptorHandleForHeapStart(),
            _frameIndex, _rtvDescriptorSize), _dsvHeap.GetCPUDescriptorHandleForHeapStart());
        _commandList.RSSetViewport(0, 0, _surfaceLifecycle.Width, _surfaceLifecycle.Height);
        _commandList.RSSetScissorRect(Vortice.Mathematics.RectI.FromLTRB(0, 0, _surfaceLifecycle.Width, _surfaceLifecycle.Height));
        UseResource(_sceneComposite!.Lifetime);
        _commandList.SetGraphicsRootSignature(_sceneComposite.RootSignature);
        _commandList.SetPipelineState(_sceneComposite.PipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.SetDescriptorHeaps(_shaderResourceHeap);
        _commandList.SetGraphicsRootDescriptorTable(1, new GpuDescriptorHandle(
            _shaderResourceHeap.GetGPUDescriptorHandleForHeapStart(), _sceneDescriptors![_frameIndex], _shaderResourceDescriptorSize));
        ReadOnlySpan<float> settings = stackalloc float[] { _sceneOutput.Exposure, _sceneOutput.AcesFitted ? 1 : 0 };
        _commandList.SetGraphicsRoot32BitConstants(0, settings);
        _commandList.DrawInstanced(3, 1, 0, 0);
        _recordedCompositeCpuMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}
