using ForgeLine.Platform;
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

    private ID3D12Resource? _depthTarget;
    private ulong _nextFenceValue = 1;
    private int _frameIndex;
    private int _width;
    private int _height;
    private bool _isSuspended;
    private bool _disposed;

    internal D3D12GraphicsDevice(IWindow window, GraphicsConfiguration configuration)
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

        _width = Math.Max(window.ClientSize.Width, 1);
        _height = Math.Max(window.ClientSize.Height, 1);
        _isSuspended = window.IsMinimized || window.ClientSize.IsEmpty;

        SwapChainDescription1 swapChainDescription = new()
        {
            Width = (uint)_width,
            Height = (uint)_height,
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
                   window.NativeHandle.Value,
                   swapChainDescription))
        {
            _factory.MakeWindowAssociation(
                window.NativeHandle.Value,
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

        CreateRenderTargets();

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
            $"[graphics:surface] size={_width}x{_height} " +
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
                    _width,
                    _height,
                    _configuration.BufferCount,
                    _frameIndex,
                    _isSuspended,
                    PresentMode));
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
                        0))
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
                        nameof(description.PrimitiveTopology))
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

        if (_isSuspended)
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

        var context = new D3D12GraphicsCommandContext(
            this,
            _commandList,
            _width,
            _height,
            _frameIndex);

        context.SetViewport(0, 0, _width, _height);
        context.SetScissor(0, 0, _width, _height);
        recordCommands?.Invoke(context);

        _commandList.ResourceBarrierTransition(
            backBuffer,
            ResourceStates.RenderTarget,
            ResourceStates.Present);
        _commandList.Close();

        _commandQueue.ExecuteCommandList(_commandList);

        var presentResult = _swapChain.Present(
            _configuration.EnableVSync ? 1u : 0u,
            PresentFlags.None);

        if (presentResult.Failure)
        {
            throw CreateDeviceFailure(
                "Direct3D 12 failed to present the current frame.",
                presentResult.Code);
        }

        ulong fenceValue = _nextFenceValue++;
        _commandQueue.Signal(_frameFence, fenceValue);
        _frameFenceValues[_frameIndex] = fenceValue;
        _frameIndex = checked((int)_swapChain.CurrentBackBufferIndex);
    }

    public void Resize(int width, int height)
    {
        ThrowIfDisposed();

        if (width <= 0 || height <= 0)
        {
            _isSuspended = true;
            return;
        }

        if (!_isSuspended && width == _width && height == _height)
        {
            return;
        }

        WaitForIdle();
        ReleaseRenderTargets();

        var resizeResult = _swapChain.ResizeBuffers(
            (uint)_configuration.BufferCount,
            (uint)width,
            (uint)height,
            BackBufferFormat);

        if (resizeResult.Failure)
        {
            throw CreateDeviceFailure(
                $"Direct3D 12 failed to resize the swap chain to {width}x{height}.",
                resizeResult.Code);
        }

        _width = width;
        _height = height;
        _frameIndex = checked((int)_swapChain.CurrentBackBufferIndex);
        Array.Clear(_frameFenceValues);
        CreateRenderTargets();
        _isSuspended = false;

        Console.WriteLine($"[graphics:resize] size={_width}x{_height}");
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

    private void CreateRenderTargets()
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
            checked((uint)_width),
            checked((uint)_height),
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
