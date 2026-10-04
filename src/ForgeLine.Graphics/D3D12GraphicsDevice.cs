using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsDevice : IGraphicsDevice
{
    private const Format BackBufferFormat = Format.R8G8B8A8_UNorm;
    private const Format DepthBufferFormat = Format.D32_Float;
    private const int DxgiStatusOccluded = 0x087A0001;

    private readonly GraphicsConfiguration _configuration;
    private readonly IDXGIFactory4 _factory;
    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly IDXGISwapChain3 _swapChain;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly ID3D12DescriptorHeap _dsvHeap;
    private readonly uint _rtvDescriptorSize;
    private readonly ID3D12CommandAllocator[] _commandAllocators;
    private readonly ID3D12Resource[] _renderTargets;
    private readonly ulong[] _frameFenceValues;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12Fence _frameFence;
    private readonly AutoResetEvent _frameFenceEvent;
    private readonly GraphicsDeviceInfo _deviceInfo;
    private readonly GraphicsSurfaceLifecycleState _surfaceLifecycle;

    private ID3D12Resource? _depthTarget;
    private ulong _nextFenceValue = 1;
    private ulong _submittedFrameCount;
    private ulong _presentedFrameCount;
    private int _frameIndex;
    private bool _disposed;

    internal D3D12GraphicsDevice(in GraphicsWindowTarget target, GraphicsConfiguration configuration)
    {
        _configuration = configuration;
        configuration.Validate();

        bool debugLayerEnabled = TryEnableDebugLayer(configuration.EnableDebugLayer);
        _factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(debugLayerEnabled);

        (_device, _deviceInfo) = CreateDevice(
            _factory,
            configuration.AllowSoftwareAdapterFallback,
            debugLayerEnabled);

        _commandQueue = _device.CreateCommandQueue(CommandListType.Direct);
        _commandQueue.Name = "ForgeLine Graphics Queue";

        int initialWidth =
            Math.Max(
                target.Width,
                1);
        int initialHeight =
            Math.Max(
                target.Height,
                1);
        _surfaceLifecycle =
            new GraphicsSurfaceLifecycleState(
                initialWidth,
                initialHeight,
                target.Suspended ||
                target.Width <= 0 ||
                target.Height <= 0);

        SwapChainDescription1 swapChainDescription = new()
        {
            Width = (uint)initialWidth,
            Height = (uint)initialHeight,
            Format = BackBufferFormat,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = (uint)configuration.BufferCount,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard
        };

        using (IDXGISwapChain1 swapChain = _factory.CreateSwapChainForHwnd(
                   _commandQueue,
                   target.NativeHandle,
                   swapChainDescription))
        {
            _factory.MakeWindowAssociation(
                target.NativeHandle,
                WindowAssociationFlags.IgnoreAltEnter);

            _swapChain = swapChain.QueryInterface<IDXGISwapChain3>();
        }

        _frameIndex = checked((int)_swapChain.CurrentBackBufferIndex);

        _rtvHeap = _device.CreateDescriptorHeap(
            new DescriptorHeapDescription(
                DescriptorHeapType.RenderTargetView,
                (uint)configuration.BufferCount));
        _rtvDescriptorSize =
            _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _dsvHeap = _device.CreateDescriptorHeap(
            new DescriptorHeapDescription(
                DescriptorHeapType.DepthStencilView,
                1));

        _renderTargets = new ID3D12Resource[configuration.BufferCount];
        _commandAllocators = new ID3D12CommandAllocator[configuration.BufferCount];
        _frameFenceValues = new ulong[configuration.BufferCount];

        CreateRenderTargets(
            initialWidth,
            initialHeight);

        for (int index = 0; index < configuration.BufferCount; index++)
        {
            _commandAllocators[index] =
                _device.CreateCommandAllocator(CommandListType.Direct);
            _commandAllocators[index].Name = $"ForgeLine Frame Allocator {index}";
        }

        _commandList = _device.CreateCommandList<ID3D12GraphicsCommandList>(
            CommandListType.Direct,
            _commandAllocators[0]);
        _commandList.Name = "ForgeLine Graphics Command List";
        _commandList.Close();

        _frameFence = _device.CreateFence(0);
        _frameFence.Name = "ForgeLine Frame Fence";
        _frameFenceEvent = new AutoResetEvent(false);

        Console.WriteLine(
            $"[graphics:device] adapter=\"{_deviceInfo.AdapterName}\" " +
            $"featureLevel={_deviceInfo.FeatureLevel} " +
            $"vramBytes={_deviceInfo.DedicatedVideoMemoryBytes} " +
            $"software={_deviceInfo.IsSoftwareAdapter} " +
            $"debugLayer={_deviceInfo.DebugLayerEnabled}");
        Console.WriteLine(
            $"[graphics:surface] size={_surfaceLifecycle.Width}x{_surfaceLifecycle.Height} " +
            $"buffers={configuration.BufferCount} present={PresentMode}");
    }

    public GraphicsDiagnostics Diagnostics
    {
        get
        {
            ThrowIfDisposed();

            return new GraphicsDiagnostics(
                _deviceInfo,
                new GraphicsSurfaceInfo(
                    _surfaceLifecycle.Width,
                    _surfaceLifecycle.Height,
                    _configuration.BufferCount,
                    _frameIndex,
                    _surfaceLifecycle.IsSuspended,
                    PresentMode,
                    _surfaceLifecycle.IsOccluded,
                    _surfaceLifecycle.HasPendingResize,
                    _surfaceLifecycle.ResizeGeneration,
                    _surfaceLifecycle.AppliedResizeGeneration,
                    _submittedFrameCount,
                    _presentedFrameCount));
        }
    }

    private string PresentMode => _configuration.EnableVSync ? "VSync" : "Immediate";

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        description.Validate();

        RootSignatureFlags rootSignatureFlags =
            RootSignatureFlags.AllowInputAssemblerInputLayout |
            RootSignatureFlags.DenyHullShaderRootAccess |
            RootSignatureFlags.DenyDomainShaderRootAccess |
            RootSignatureFlags.DenyGeometryShaderRootAccess |
            RootSignatureFlags.DenyAmplificationShaderRootAccess |
            RootSignatureFlags.DenyMeshShaderRootAccess;

        RootParameter1[]? rootParameters =
            description.VertexRootConstantCount > 0
                ? [
                    new RootParameter1(
                        new RootConstants(
                            0,
                            0,
                            checked((uint)description.VertexRootConstantCount)),
                        ShaderVisibility.Vertex)
                ]
                : null;

        ID3D12RootSignature rootSignature =
            _device.CreateRootSignature(
                new RootSignatureDescription1(
                    rootSignatureFlags,
                    rootParameters));
        rootSignature.Name = "ForgeLine Graphics Root Signature";

        InputElementDescription[] inputElements = description.VertexElements
            .Select(
                static element =>
                    new InputElementDescription(
                        element.SemanticName,
                        checked((uint)element.SemanticIndex),
                        ToNativeFormat(element.Format),
                        checked((uint)element.OffsetInBytes),
                        checked((uint)element.InputSlot),
                        element.InputRate switch
                        {
                            GraphicsVertexInputRate.PerVertex =>
                                InputClassification.PerVertexData,
                            GraphicsVertexInputRate.PerInstance =>
                                InputClassification.PerInstanceData,
                            _ =>
                                throw new ArgumentOutOfRangeException(
                                    nameof(element),
                                    element.InputRate,
                                    "Unsupported graphics vertex input rate.")
                        },
                        checked((uint)element.InstanceStepRate)))
            .ToArray();

        try
        {
            GraphicsPipelineStateDescription pipelineStateDescription = new()
            {
                RootSignature = rootSignature,
                VertexShader = description.VertexShader.Data,
                PixelShader = description.PixelShader.Data,
                InputLayout = new InputLayoutDescription(inputElements),
                SampleMask = uint.MaxValue,
                PrimitiveTopologyType = description.PrimitiveTopology switch
                {
                    GraphicsPrimitiveTopology.TriangleList =>
                        PrimitiveTopologyType.Triangle,
                    GraphicsPrimitiveTopology.LineList =>
                        PrimitiveTopologyType.Line,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(description),
                        description.PrimitiveTopology,
                        "Unsupported graphics primitive topology.")
                },
                RasterizerState = RasterizerDescription.CullCounterClockwise,
                BlendState = BlendDescription.Opaque,
                DepthStencilState = description.DepthEnabled
                    ? DepthStencilDescription.Default
                    : DepthStencilDescription.None,
                RenderTargetFormats = [BackBufferFormat],
                DepthStencilFormat = DepthBufferFormat,
                SampleDescription = SampleDescription.Default
            };

            ID3D12PipelineState pipelineState =
                _device.CreateGraphicsPipelineState(pipelineStateDescription);
            pipelineState.Name = "ForgeLine Graphics Pipeline";

            return new D3D12GraphicsPipeline(
                this,
                description,
                rootSignature,
                pipelineState);
        }
        catch
        {
            rootSignature.Dispose();
            throw;
        }
    }

    public IGraphicsBuffer CreateBuffer(GraphicsBufferDescription description)
    {
        ThrowIfDisposed();
        description.Validate();

        HeapType heapType;
        ResourceStates initialState;

        switch (description.Memory)
        {
            case GraphicsBufferMemory.GpuLocal:
                heapType = HeapType.Default;
                initialState = ResourceStates.Common;
                break;

            case GraphicsBufferMemory.Upload:
                heapType = HeapType.Upload;
                initialState = ResourceStates.GenericRead;
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(description),
                    description.Memory,
                    "Unsupported graphics buffer memory type.");
        }

        ID3D12Resource resource = _device.CreateCommittedResource(
            heapType,
            ResourceDescription.Buffer(description.SizeInBytes),
            initialState);

        return new D3D12GraphicsBuffer(this, description, resource);
    }

    public void RenderFrame(
        GraphicsColor clearColor,
        Action<IGraphicsCommandContext>? recordCommands = null)
    {
        ThrowIfDisposed();

        ApplyPendingResize();

        if (_surfaceLifecycle.IsSuspended)
        {
            return;
        }

        if (_surfaceLifecycle.IsOccluded &&
            !TryRecoverOcclusion())
        {
            return;
        }

        WaitForFrame(_frameIndex);

        ID3D12CommandAllocator allocator = _commandAllocators[_frameIndex];
        allocator.Reset();
        _commandList.Reset(allocator);

        ID3D12Resource backBuffer = _renderTargets[_frameIndex];
        _commandList.ResourceBarrierTransition(
            backBuffer,
            ResourceStates.Present,
            ResourceStates.RenderTarget);

        CpuDescriptorHandle rtv = new(
            _rtvHeap.GetCPUDescriptorHandleForHeapStart(),
            _frameIndex,
            _rtvDescriptorSize);

        CpuDescriptorHandle dsv =
            _dsvHeap.GetCPUDescriptorHandleForHeapStart();

        _commandList.OMSetRenderTargets(rtv, dsv);
        _commandList.ClearRenderTargetView(
            rtv,
            new Color4(
                clearColor.Red,
                clearColor.Green,
                clearColor.Blue,
                clearColor.Alpha));
        _commandList.ClearDepthStencilView(
            dsv,
            ClearFlags.Depth,
            1.0f,
            0);

        int width =
            _surfaceLifecycle.Width;
        int height =
            _surfaceLifecycle.Height;
        var context = new D3D12GraphicsCommandContext(
            this,
            _commandList,
            width,
            height,
            _frameIndex);

        context.SetViewport(
            0,
            0,
            width,
            height);
        context.SetScissor(
            0,
            0,
            width,
            height);
        recordCommands?.Invoke(context);

        _commandList.ResourceBarrierTransition(
            backBuffer,
            ResourceStates.RenderTarget,
            ResourceStates.Present);
        _commandList.Close();

        _commandQueue.ExecuteCommandList(_commandList);
        _submittedFrameCount++;

        var presentResult = _swapChain.Present(
            _configuration.EnableVSync ? 1u : 0u,
            PresentFlags.None);

        if (presentResult.Failure)
        {
            Console.Error.WriteLine(
                $"[graphics:present-failed] frameIndex={_frameIndex} " +
                $"size={width}x{height} hresult=0x{presentResult.Code:X8}");
            throw CreateDeviceFailure(
                "Direct3D 12 failed to present the current frame.",
                presentResult.Code);
        }

        if (presentResult.Code ==
            DxgiStatusOccluded)
        {
            if (_surfaceLifecycle.MarkOccluded())
            {
                Console.WriteLine(
                    $"[graphics:present-occluded] frameIndex={_frameIndex} " +
                    $"size={width}x{height}");
            }
        }
        else
        {
            _ = _surfaceLifecycle.MarkPresentable();
            _presentedFrameCount++;
        }

        SignalSubmittedFrame();
    }

    public void Resize(int width, int height)
    {
        ThrowIfDisposed();

        if (!_surfaceLifecycle.RequestResize(
                width,
                height))
        {
            return;
        }

        if (width <= 0 ||
            height <= 0)
        {
            Console.WriteLine(
                $"[graphics:resize-request] requested={width}x{height} " +
                "action=suspend");
            return;
        }

        Console.WriteLine(
            $"[graphics:resize-request] requested={width}x{height} " +
            $"generation={_surfaceLifecycle.ResizeGeneration} action=queued");
    }

    public void WaitForIdle()
    {
        ThrowIfDisposed();

        ulong fenceValue = _nextFenceValue++;
        _commandQueue.Signal(_frameFence, fenceValue);

        if (_frameFence.CompletedValue < fenceValue)
        {
            _frameFence.SetEventOnCompletion(fenceValue, _frameFenceEvent);
            _frameFenceEvent.WaitOne();
        }

        Array.Clear(_frameFenceValues);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            WaitForIdle();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[graphics:shutdown-warning] type={exception.GetType().Name} " +
                $"message={exception.Message}");
        }

        ReleaseRenderTargets();

        foreach (ID3D12CommandAllocator allocator in _commandAllocators)
        {
            allocator.Dispose();
        }

        _commandList.Dispose();
        _dsvHeap.Dispose();
        _rtvHeap.Dispose();
        _swapChain.Dispose();
        _frameFence.Dispose();
        _frameFenceEvent.Dispose();
        _commandQueue.Dispose();

#if DEBUG
        if (_deviceInfo.DebugLayerEnabled)
        {
            using ID3D12DebugDevice? debugDevice =
                _device.QueryInterfaceOrNull<ID3D12DebugDevice>();
            debugDevice?.ReportLiveDeviceObjects(
                ReportLiveDeviceObjectFlags.Detail |
                ReportLiveDeviceObjectFlags.IgnoreInternal);
        }
#endif

        _device.Dispose();
        _factory.Dispose();
        _disposed = true;
    }

    private void CreateRenderTargets(
        int width,
        int height)
    {
        CpuDescriptorHandle rtv =
            _rtvHeap.GetCPUDescriptorHandleForHeapStart();

        for (int index = 0; index < _renderTargets.Length; index++)
        {
            ID3D12Resource renderTarget =
                _swapChain.GetBuffer<ID3D12Resource>((uint)index);
            renderTarget.Name = $"ForgeLine Back Buffer {index}";
            _renderTargets[index] = renderTarget;
            _device.CreateRenderTargetView(renderTarget, null, rtv);
            rtv += (int)_rtvDescriptorSize;
        }

        ResourceDescription depthDescription = ResourceDescription.Texture2D(
            DepthBufferFormat,
            checked((uint)width),
            checked((uint)height),
            flags: ResourceFlags.AllowDepthStencil);
        var clearValue = new ClearValue(DepthBufferFormat, 1.0f, 0);

        _depthTarget = _device.CreateCommittedResource(
            HeapType.Default,
            depthDescription,
            ResourceStates.DepthWrite,
            clearValue);
        _depthTarget.Name = "ForgeLine Depth Buffer";

        DepthStencilViewDescription depthViewDescription = new()
        {
            Format = DepthBufferFormat,
            ViewDimension = DepthStencilViewDimension.Texture2D
        };

        _device.CreateDepthStencilView(
            _depthTarget,
            depthViewDescription,
            _dsvHeap.GetCPUDescriptorHandleForHeapStart());
    }

    private void ReleaseRenderTargets()
    {
        _depthTarget?.Dispose();
        _depthTarget = null;

        for (int index = 0; index < _renderTargets.Length; index++)
        {
            _renderTargets[index]?.Dispose();
            _renderTargets[index] = null!;
        }
    }

    private void ApplyPendingResize()
    {
        if (!_surfaceLifecycle.TryGetPendingResize(
                out GraphicsResizeRequest request))
        {
            return;
        }

        Console.WriteLine(
            $"[graphics:resize-apply] requested={request.Width}x{request.Height} " +
            $"generation={request.Generation} phase=synchronize");

        WaitForIdle();
        Console.WriteLine(
            $"[graphics:resize-synchronized] generation={request.Generation} " +
            $"completedFence={_frameFence.CompletedValue}");
        ReleaseRenderTargets();

        var resizeResult =
            _swapChain.ResizeBuffers(
                (uint)_configuration.BufferCount,
                (uint)request.Width,
                (uint)request.Height,
                BackBufferFormat);

        if (resizeResult.Failure)
        {
            Console.Error.WriteLine(
                $"[graphics:resize-failed] requested={request.Width}x{request.Height} " +
                $"generation={request.Generation} hresult=0x{resizeResult.Code:X8}");
            throw CreateDeviceFailure(
                $"Direct3D 12 failed to resize the swap chain to {request.Width}x{request.Height}.",
                resizeResult.Code);
        }

        int frameIndex =
            GetCurrentBackBufferIndex();
        CreateRenderTargets(
            request.Width,
            request.Height);
        Array.Clear(
            _frameFenceValues);
        _frameIndex = frameIndex;
        _surfaceLifecycle.CompleteResize(
            request);

        Console.WriteLine(
            $"[graphics:resize-applied] size={request.Width}x{request.Height} " +
            $"generation={request.Generation} frameIndex={_frameIndex} " +
            $"buffers={_configuration.BufferCount}");
    }

    private bool TryRecoverOcclusion()
    {
        var testResult =
            _swapChain.Present(
                0,
                PresentFlags.Test);

        if (testResult.Code ==
            DxgiStatusOccluded)
        {
            return false;
        }

        if (testResult.Failure)
        {
            Console.Error.WriteLine(
                $"[graphics:present-recovery-failed] frameIndex={_frameIndex} " +
                $"hresult=0x{testResult.Code:X8}");
            throw CreateDeviceFailure(
                "Direct3D 12 failed while probing presentation recovery.",
                testResult.Code);
        }

        if (_surfaceLifecycle.MarkPresentable())
        {
            Console.WriteLine(
                $"[graphics:present-recovered] frameIndex={_frameIndex} " +
                $"size={_surfaceLifecycle.Width}x{_surfaceLifecycle.Height}");
        }

        return true;
    }

    private void SignalSubmittedFrame()
    {
        ulong fenceValue =
            _nextFenceValue++;
        _commandQueue.Signal(
            _frameFence,
            fenceValue);
        _frameFenceValues[_frameIndex] =
            fenceValue;
        _frameIndex =
            GetCurrentBackBufferIndex();
    }

    private int GetCurrentBackBufferIndex()
    {
        int frameIndex =
            checked(
                (int)_swapChain.CurrentBackBufferIndex);

        if ((uint)frameIndex >=
            (uint)_configuration.BufferCount)
        {
            throw new GraphicsDeviceException(
                $"DXGI returned invalid back-buffer index {frameIndex} " +
                $"for {_configuration.BufferCount} buffers.");
        }

        return frameIndex;
    }

    private void WaitForFrame(int frameIndex)
    {
        ulong fenceValue = _frameFenceValues[frameIndex];
        if (fenceValue == 0 || _frameFence.CompletedValue >= fenceValue)
        {
            return;
        }

        _frameFence.SetEventOnCompletion(fenceValue, _frameFenceEvent);
        _frameFenceEvent.WaitOne();
    }

    private static Format ToNativeFormat(GraphicsVertexElementFormat format) =>
        format switch
        {
            GraphicsVertexElementFormat.Float2 => Format.R32G32_Float,
            GraphicsVertexElementFormat.Float3 => Format.R32G32B32_Float,
            GraphicsVertexElementFormat.Float4 => Format.R32G32B32A32_Float,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private GraphicsDeviceException CreateDeviceFailure(
        string message,
        int resultCode)
    {
        int removedReason = _device.DeviceRemovedReason.Code;

        return new GraphicsDeviceException(
            $"{message} HRESULT=0x{resultCode:X8}; " +
            $"deviceRemovedReason=0x{removedReason:X8}; " +
            $"adapter=\"{_deviceInfo.AdapterName}\".");
    }

    private static bool TryEnableDebugLayer(bool requested)
    {
        if (!requested)
        {
            return false;
        }

        if (D3D12.D3D12GetDebugInterface(out ID3D12Debug? debug).Failure ||
            debug is null)
        {
            Console.Error.WriteLine(
                "[graphics:debug] Direct3D 12 debug layer requested but unavailable.");
            return false;
        }

        using (debug)
        {
            debug.EnableDebugLayer();
        }

        return true;
    }

    private static (ID3D12Device Device, GraphicsDeviceInfo Info) CreateDevice(
        IDXGIFactory4 factory,
        bool allowSoftwareAdapterFallback,
        bool debugLayerEnabled)
    {
        for (uint adapterIndex = 0;
             factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success;
             adapterIndex++)
        {
            using (adapter)
            {
                AdapterDescription1 description = adapter!.Description1;
                if ((description.Flags & AdapterFlags.Software) != AdapterFlags.None)
                {
                    continue;
                }

                if (D3D12.D3D12CreateDevice(
                        adapter,
                        FeatureLevel.Level_11_0,
                        out ID3D12Device? device).Failure ||
                    device is null)
                {
                    continue;
                }

                FeatureLevel featureLevel =
                    device.CheckMaxSupportedFeatureLevel(D3D12.FeatureLevels);

                return (
                    device,
                    new GraphicsDeviceInfo(
                        description.Description,
                        (ulong)description.DedicatedVideoMemory,
                        false,
                        featureLevel.ToString(),
                        debugLayerEnabled));
            }
        }

        if (allowSoftwareAdapterFallback)
        {
            using IDXGIAdapter1 warpAdapter =
                factory.EnumWarpAdapter<IDXGIAdapter1>();
            AdapterDescription1 description = warpAdapter.Description1;

            if (D3D12.D3D12CreateDevice(
                    warpAdapter,
                    FeatureLevel.Level_11_0,
                    out ID3D12Device? device).Success &&
                device is not null)
            {
                FeatureLevel featureLevel =
                    device.CheckMaxSupportedFeatureLevel(D3D12.FeatureLevels);

                return (
                    device,
                    new GraphicsDeviceInfo(
                        description.Description,
                        (ulong)description.DedicatedVideoMemory,
                        true,
                        featureLevel.ToString(),
                        debugLayerEnabled));
            }
        }

        throw new PlatformNotSupportedException(
            "No Direct3D 12 adapter supporting feature level 11_0 or newer is available.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
