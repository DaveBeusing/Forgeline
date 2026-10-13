using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Feature = Vortice.Direct3D12.Feature;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private GraphicsPostProcessingSettings _postProcessing = GraphicsPostProcessingSettings.Neutral;
    public ulong? AvailableMemoryBudgetBytes
    {
        get
        {
            ThrowIfDisposed();
            GraphicsMemoryDiagnostics budget = CaptureMemoryBudget();
            return budget.Available && budget.LocalBudgetBytes > 0 ?
                budget.LocalBudgetBytes > budget.LocalUsageBytes ? budget.LocalBudgetBytes - budget.LocalUsageBytes : 0 : null;
        }
    }
    private bool? _supportsPostProcessing;
    public bool SupportsPostProcessing
    {
        get
        {
            ThrowIfDisposed();
            if (_supportsPostProcessing is { } supported) return supported;
            var color = new FeatureDataFormatSupport { Format = Format.R16G16B16A16_Float };
            var depth = new FeatureDataFormatSupport { Format = Format.R32_Float };
            const FormatSupport1 colorFlags = FormatSupport1.Texture2D | FormatSupport1.RenderTarget | FormatSupport1.ShaderLoad | FormatSupport1.Blendable;
            const FormatSupport1 depthFlags = FormatSupport1.Texture2D | FormatSupport1.ShaderLoad;
            bool available = _device.CheckFeatureSupport(Feature.FormatSupport, ref color) &&
                _device.CheckFeatureSupport(Feature.FormatSupport, ref depth) &&
                (color.Support1 & colorFlags) == colorFlags && (depth.Support1 & depthFlags) == depthFlags;
            _supportsPostProcessing = available;
            return available;
        }
    }
    public GraphicsPostProcessingSettings PostProcessing { get { ThrowIfDisposed(); return _postProcessing; } }
    public void ConfigurePostProcessing(GraphicsPostProcessingSettings settings)
    {
        ThrowIfDisposed(); ThrowIfRecording(); settings.Validate();
        if (!_sceneOutput.Enabled && settings != GraphicsPostProcessingSettings.Neutral)
            throw new InvalidOperationException("Postprocessing requires linear scene output.");
        _postProcessing = settings;
        ResetFrameMeasurements();
    }
    private GraphicsSceneOutputSettings _sceneOutput = new(false, 1, true);
    private ID3D12Resource[]? _sceneTargets;
    private ID3D12Resource? _overlayDepthTarget;
    private bool ScaledScene => _sceneOutput.Enabled && _sceneOutput.ResolutionScale < 1;
    internal bool IsWorldPass => _passState.Current == GraphicsFramePass.World;
    internal int WorldWidth => GraphicsFramePlan.ScaledDimension(_surfaceLifecycle.Width, _sceneOutput.ResolutionScale);
    internal int WorldHeight => GraphicsFramePlan.ScaledDimension(_surfaceLifecycle.Height, _sceneOutput.ResolutionScale);
    private CpuDescriptorHandle OverlayDsv => ScaledScene ? new(_dsvHeap.GetCPUDescriptorHandleForHeapStart(), 1,
        _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView)) : _dsvHeap.GetCPUDescriptorHandleForHeapStart();
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
        if (settings.Enabled && !SupportsPostProcessing) throw new NotSupportedException("Linear scene formats are unsupported.");
        if ((_atmosphere.Enabled || _postProcessing != GraphicsPostProcessingSettings.Neutral) && !settings.Enabled)
            throw new InvalidOperationException("Disable atmosphere and postprocessing before disabling linear scene composition.");
        if (settings == _sceneOutput)
            return;
        if (settings.Enabled == _sceneOutput.Enabled && settings.ResolutionScale == _sceneOutput.ResolutionScale)
        {
            _sceneOutput = settings;
            ResetFrameMeasurements();
            return;
        }
        WaitForIdle();
        GraphicsSceneOutputSettings previous = _sceneOutput;
        ReleaseRenderTargets();
        _sceneComposite?.Dispose();
        _sceneComposite = null;
        _sceneOutput = settings;
        ResetFrameMeasurements();
        try
        {
            if (settings.Enabled)
            {
                CreateRenderTargets(_surfaceLifecycle.Width, _surfaceLifecycle.Height);
                _sceneComposite = CreateSceneComposite();
            }
            else CreateRenderTargets(_surfaceLifecycle.Width, _surfaceLifecycle.Height);
        }
        catch
        {
            ReleaseRenderTargets();
            _sceneOutput = previous;
            try
            {
                CreateRenderTargets(_surfaceLifecycle.Width, _surfaceLifecycle.Height);
                if (previous.Enabled) _sceneComposite = CreateSceneComposite();
            }
            catch (Exception recovery)
            {
                ReleaseRenderTargets();
                _failure = new GraphicsDeviceException("Scene target rollback failed.", recovery);
                throw _failure;
            }
            throw;
        }
    }

    private CpuDescriptorHandle SceneRtv(int frame) => new(
        _rtvHeap.GetCPUDescriptorHandleForHeapStart(), frame + _configuration.BufferCount, _rtvDescriptorSize);

    private void CreateSceneTargets(int width, int height)
    {
        if (!_sceneOutput.Enabled)
            return;
        width = GraphicsFramePlan.ScaledDimension(width, _sceneOutput.ResolutionScale);
        height = GraphicsFramePlan.ScaledDimension(height, _sceneOutput.ResolutionScale);
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
        var vertex = compiler.Compile(SceneCompositeShader.Vertex, GraphicsShaderStage.Vertex, "VSMain", "SceneCompositeVertex.hlsl");
        var pixel = compiler.Compile((ScaledScene ? "#define UPSAMPLE 1\n" : "#define UPSAMPLE 0\n") +
            (_atmosphere.Enabled ? "#define ATMOSPHERE 1\n" : "#define ATMOSPHERE 0\n") +
            SceneCompositeShader.Pixel, GraphicsShaderStage.Pixel, "PSMain", "SceneCompositePixel.hlsl");
        return (D3D12GraphicsPipeline)CreateGraphicsPipeline(new(vertex, pixel)
        {
            CullMode = GraphicsCullMode.None, PixelTextureCount = _atmosphere.Enabled || ScaledScene ? 2 : 1,
            DepthEnabled = ScaledScene, DepthAlwaysPass = ScaledScene,
            VertexRootConstantCount = SceneCompositeShader.ConstantCount
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
        if (_atmosphere.Enabled || ScaledScene)
        {
            _commandList.OMSetRenderTargets(new CpuDescriptorHandle(_rtvHeap.GetCPUDescriptorHandleForHeapStart(),
                _frameIndex, _rtvDescriptorSize));
            _commandList.ResourceBarrierTransition(_depthTarget!, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
        }
        _commandList.OMSetRenderTargets(new CpuDescriptorHandle(_rtvHeap.GetCPUDescriptorHandleForHeapStart(),
            _frameIndex, _rtvDescriptorSize), ScaledScene ? OverlayDsv : _atmosphere.Enabled ? null : _dsvHeap.GetCPUDescriptorHandleForHeapStart());
        _commandList.RSSetViewport(0, 0, _surfaceLifecycle.Width, _surfaceLifecycle.Height);
        _commandList.RSSetScissorRect(Vortice.Mathematics.RectI.FromLTRB(0, 0, _surfaceLifecycle.Width, _surfaceLifecycle.Height));
        UseResource(_sceneComposite!.Lifetime);
        _commandList.SetGraphicsRootSignature(_sceneComposite.RootSignature);
        _commandList.SetPipelineState(_sceneComposite.PipelineState);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.SetDescriptorHeaps(_shaderResourceHeap);
        _commandList.SetGraphicsRootDescriptorTable(1, new GpuDescriptorHandle(
            _shaderResourceHeap.GetGPUDescriptorHandleForHeapStart(), _sceneDescriptors![_frameIndex], _shaderResourceDescriptorSize));
        if (_atmosphere.Enabled || ScaledScene)
        {
            _commandList.SetGraphicsRootDescriptorTable(2, new GpuDescriptorHandle(
                _shaderResourceHeap.GetGPUDescriptorHandleForHeapStart(), _atmosphereDepthDescriptor, _shaderResourceDescriptorSize));
        }
        Span<float> constants = stackalloc float[SceneCompositeShader.ConstantCount];
        WriteAtmosphereConstants(constants);
        constants[32] = _postProcessing.WhiteBalance; constants[33] = _postProcessing.Tint;
        constants[34] = _postProcessing.Contrast; constants[35] = _postProcessing.Saturation;
        constants[36] = _postProcessing.EdgeBlend;
        constants[37] = _surfaceLifecycle.Width; constants[38] = _surfaceLifecycle.Height;
        RecordCaptureInput(System.Runtime.InteropServices.MemoryMarshal.AsBytes(constants));
        _commandList.SetGraphicsRoot32BitConstants(0, constants);
        _commandList.DrawInstanced(3, 1, 0, 0);
        if (_atmosphere.Enabled || ScaledScene)
        {
            _commandList.ResourceBarrierTransition(_depthTarget!, ResourceStates.PixelShaderResource, ResourceStates.DepthWrite);
            _commandList.OMSetRenderTargets(new CpuDescriptorHandle(_rtvHeap.GetCPUDescriptorHandleForHeapStart(),
                _frameIndex, _rtvDescriptorSize), OverlayDsv);
        }
        _recordedCompositeCpuMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}
