using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice : IGraphicsDevice
{
    public bool SupportsUiText => true;
    private const Format BackBufferFormat = Format.R8G8B8A8_UNorm;
    private const Format DepthBufferFormat = Format.D32_Float;
    private const int DxgiStatusOccluded = 0x087A0001;
    private const int ShaderResourceDescriptorCapacity = 4_096;
    // A live GPU timeout is fatal for this process. Keep ownership rooted until process exit.
    private static D3D12GraphicsDevice? s_unretiredDevice;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly GraphicsResourceRetirement _resources = new();
    private bool _recording;
    public bool IsRecording { get { ThrowIfDisposed(); return _recording; } }
    private GraphicsDeviceException? _failure;
    private long _fenceWaitCount;
    private long _submissionFaultCount;
    private ulong _lastSubmittedFrameFence;
    private double? _frameWaitMilliseconds, _presentMilliseconds;

    private readonly GraphicsConfiguration _configuration;
    private readonly IDXGIFactory4 _factory;
    private readonly ID3D12Device _device;
    private readonly ID3D12CommandQueue _commandQueue;
    private readonly IDXGISwapChain3 _swapChain;
    private readonly ID3D12DescriptorHeap _rtvHeap;
    private readonly ID3D12DescriptorHeap _dsvHeap;
    private readonly ID3D12DescriptorHeap _shaderResourceHeap;
    private readonly D3D12DescriptorAllocator _shaderResourceDescriptors;
    private readonly uint _rtvDescriptorSize;
    private readonly uint _shaderResourceDescriptorSize;
    private readonly ID3D12CommandAllocator[] _commandAllocators;
    private readonly ID3D12Resource[] _renderTargets;
    private readonly ulong[] _frameFenceValues;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly ID3D12Fence _frameFence;
    private readonly AutoResetEvent _frameFenceEvent;
    private readonly GraphicsDeviceInfo _deviceInfo;
    private readonly GraphicsSurfaceLifecycleState _surfaceLifecycle;
    private readonly bool[] _frameTimestampReady;
    private readonly bool[] _frameHasOverlay;
    private GraphicsFramePassState _passState;
    private long _passStartedAt;
    private double _worldCpuMilliseconds;
    private double? _overlayCpuMilliseconds;
    private double? _worldGpuMilliseconds;
    private double? _overlayGpuMilliseconds;
    private double? _compositeGpuMilliseconds;
    private ulong _gpuTimingFence;
    private ulong _cpuTimingSubmission;
    private double _recordedWorldCpuMilliseconds;
    private double? _recordedOverlayCpuMilliseconds;
    private readonly ID3D12InfoQueue? _debugInfoQueue;

    private ID3D12Resource? _depthTarget;
    private ID3D12QueryHeap? _timestampQueryHeap;
    private ID3D12Resource? _timestampReadback;
    private ulong _nextFenceValue = 1;
    private ulong _submittedFrameCount;
    private ulong _presentedFrameCount;
    private int _loadedTextureCount;
    private long _residentTextureBytes;
    private long _textureBindingFailureCount;
    private int _peakLoadedTextureCount;
    private long _peakResidentTextureBytes;
    private int _peakShaderResourceDescriptorsUsed;
    private long _textureUploadCount;
    private long _textureReleaseCount;
    private ulong _timestampFrequency;
    private string? _gpuTimingUnavailableReason;
    private double? _lastGpuFrameMilliseconds;
    private long _debugWarningCount;
    private long _debugErrorCount;
    private int _frameIndex;
    private bool _disposed;

    internal D3D12GraphicsDevice(in GraphicsWindowTarget target, GraphicsConfiguration configuration)
    {
        _configuration = configuration;
        configuration.Validate();
        if (Volatile.Read(ref s_unretiredDevice) is not null)
        {
            throw new GraphicsDeviceException("GPU shutdown is unresolved; restart the process.", "gpu-shutdown-pending");
        }

        try
        {
            AttachBoundInputProvenance();
            ConfigureDred();
            bool debugLayerEnabled = TryEnableDebugLayer(configuration.EnableDebugLayer);
            _factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(debugLayerEnabled);

            (_device, _deviceInfo) = CreateDevice(
                _factory,
                configuration.AllowSoftwareAdapterFallback,
                debugLayerEnabled,
                configuration.ForceSoftwareAdapter);
            var meshOptions = new FeatureDataD3D12Options7();
            var rayOptions = new FeatureDataD3D12Options5();
            bool meshQuery = _device.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options7, ref meshOptions);
            bool rayQuery = _device.CheckFeatureSupport(Vortice.Direct3D12.Feature.Options5, ref rayOptions);
            _deviceInfo = _deviceInfo with { Advanced = new(meshQuery, meshQuery && meshOptions.MeshShaderTier != MeshShaderTier.NotSupported,
                rayQuery, rayQuery && rayOptions.RaytracingTier != RaytracingTier.NotSupported) };
            _debugInfoQueue =
                debugLayerEnabled
                    ? _device.QueryInterfaceOrNull<ID3D12InfoQueue>()
                    : null;

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
                    (uint)configuration.BufferCount * 2));
            _rtvDescriptorSize =
                _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            _dsvHeap = _device.CreateDescriptorHeap(
                new DescriptorHeapDescription(
                    DescriptorHeapType.DepthStencilView,
                    2));
            _shaderResourceHeap =
                _device.CreateDescriptorHeap(
                    new DescriptorHeapDescription(
                        DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                        ShaderResourceDescriptorCapacity,
                        DescriptorHeapFlags.ShaderVisible));
            _shaderResourceDescriptorSize =
                _device.GetDescriptorHandleIncrementSize(
                    DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            _shaderResourceDescriptors =
                new D3D12DescriptorAllocator(
                    ShaderResourceDescriptorCapacity);

            _renderTargets = new ID3D12Resource[configuration.BufferCount];
            _commandAllocators = new ID3D12CommandAllocator[configuration.BufferCount];
            _frameFenceValues = new ulong[configuration.BufferCount];
            _frameTimestampReady = new bool[configuration.BufferCount];
            _frameHasOverlay = new bool[configuration.BufferCount];

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

            TryInitializeGpuTiming(
                configuration.BufferCount);

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
        catch
        {
            DisposeNativeObjects();
            throw;
        }
    }

    public GraphicsDiagnostics Diagnostics
    {
        get
        {
            VerifyOwner();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failure is null)
            {
                CaptureDebugLayerMessages();
                CaptureCompletedGpuTimings();
            }

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
                    _surfaceLifecycle.AppliedResizeGeneration)
                {
                    SubmittedFrameCount =
                        _submittedFrameCount,
                    PresentedFrameCount =
                        _presentedFrameCount
                })
            {
                DirectionalShadows = ShadowDiagnostics,
                Atmosphere = AtmosphereDiagnostics,
                PostProcessing = new(true, _sceneOutput.Enabled ? _postProcessing.Antialiasing : SpatialAntialiasing.Disabled,
                    _sceneOutput.ResolutionScale, _sceneTargets?.Length ?? 0, _sceneTargets is null ? 0 : checked((long)WorldWidth * WorldHeight * 8 * _configuration.BufferCount),
                    0, 0, _sceneOutput.Enabled ? (_postProcessing.EdgeBlend > 0 ? 5 : 1) * (ScaledScene ? 4 : 1) : 0, _gpuTimingFence, _compositeGpuMilliseconds),
                Particles = new(_particleDepth, _particleGpuFence, _particleGpu, _particleDepth ? 1 : 0),
                Frame = new GraphicsFrameDiagnostics(
                    new GraphicsFramePlan(_surfaceLifecycle.Width, _surfaceLifecycle.Height, _configuration.BufferCount, _sceneOutput.Enabled, _directionalShadows.Resolution, _atmosphere.Enabled || _particleDepth, _sceneOutput.ResolutionScale),
                    _cpuTimingSubmission, _worldCpuMilliseconds, _overlayCpuMilliseconds,
                    _gpuTimingFence, _worldGpuMilliseconds, _overlayGpuMilliseconds)
                {
                    FrameWaitMilliseconds = _frameWaitMilliseconds,
                    PresentMilliseconds = _presentMilliseconds,
                    CompositeCpuMilliseconds = _compositeCpuMilliseconds,
                    CompositeGpuMilliseconds = _compositeGpuMilliseconds,
                    GpuTimingUnavailableReason = _gpuTimingUnavailableReason,
                    IntermediateUnavailableReason = _sceneOutput.Enabled ? null : "Linear scene composition is disabled."
                },
                Memory = _configuration.EnableMemoryDiagnostics && _failure is null ? CaptureMemoryBudget() : null,
                Resources =
                    new GraphicsResourceDiagnostics(
                        _loadedTextureCount,
                        _residentTextureBytes,
                        _shaderResourceDescriptors.UsedCount,
                        _shaderResourceDescriptors.Capacity,
                        _textureBindingFailureCount)
                    {
                        PeakLoadedTextureCount =
                            _peakLoadedTextureCount,
                        PeakResidentTextureBytes =
                            _peakResidentTextureBytes,
                        PeakShaderResourceDescriptorsUsed =
                            _peakShaderResourceDescriptorsUsed,
                        TextureUploadCount =
                            _textureUploadCount,
                        TextureReleaseCount =
                            _textureReleaseCount
                    },
                GpuTimingAvailable =
                    _timestampQueryHeap is not null &&
                    _timestampReadback is not null &&
                    _timestampFrequency > 0,
                TimestampFrequencyHz = _timestampFrequency,
                GpuFrameMilliseconds =
                    _lastGpuFrameMilliseconds,
                Health = new GraphicsHealthDiagnostics(
                    _resources.LiveCount,
                    _resources.PendingCount,
                    _resources.PeakPendingCount,
                    _resources.ReleasedCount,
                    _fenceWaitCount,
                    _submissionFaultCount,
                    _failure?.ReasonCode),
                Debug =
                    new GraphicsDebugDiagnostics(
                        _debugInfoQueue is not null,
                        _debugWarningCount,
                        _debugErrorCount)
            };
        }
    }

    private string PresentMode => _configuration.EnableVSync ? "VSync" : "Immediate";

    private GraphicsMemoryDiagnostics? _memoryBudgetSample;
    private long _memoryBudgetSampleTimestamp;
    private bool _memoryBudgetSampled;

    private GraphicsMemoryDiagnostics CaptureMemoryBudget()
    {
        if (_memoryBudgetSampled && System.Diagnostics.Stopwatch.GetElapsedTime(_memoryBudgetSampleTimestamp).TotalMilliseconds < 250)
            return _memoryBudgetSample!;
        _memoryBudgetSampleTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _memoryBudgetSampled = true;
        _memoryBudgetSample = QueryMemoryBudget();
        return _memoryBudgetSample;
    }

    private GraphicsMemoryDiagnostics QueryMemoryBudget()
    {
        try
        {
            using var adapter = _factory.EnumAdapterByLuid<IDXGIAdapter3>((Vortice.Luid)_device.AdapterLuid);
            QueryVideoMemoryInfo local = adapter.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
            QueryVideoMemoryInfo nonLocal = adapter.QueryVideoMemoryInfo(0, MemorySegmentGroup.NonLocal);
            return new GraphicsMemoryDiagnostics(true, local.Budget, local.CurrentUsage, nonLocal.Budget, nonLocal.CurrentUsage, null);
        }
        catch (SharpGen.Runtime.SharpGenException exception)
        {
            return new GraphicsMemoryDiagnostics(false, 0, 0, 0, 0, exception.ResultCode.ToString());
        }
    }

    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDescription description)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(description);
        description.Validate();
        if (description.VertexConstantsInBuffer && !_directionalShadows.Enabled)
            throw new NotSupportedException("Buffered frame constants require the directional shadow frame arena.");

        RootSignatureFlags rootSignatureFlags =
            RootSignatureFlags.AllowInputAssemblerInputLayout |
            RootSignatureFlags.DenyHullShaderRootAccess |
            RootSignatureFlags.DenyDomainShaderRootAccess |
            RootSignatureFlags.DenyGeometryShaderRootAccess |
            RootSignatureFlags.DenyAmplificationShaderRootAccess |
            RootSignatureFlags.DenyMeshShaderRootAccess;

        var rootParameters =
            new List<RootParameter1>();

        if (description.VertexRootConstantCount > 0)
        {
            rootParameters.Add(
                new RootParameter1(
                    new RootConstants(
                        0,
                        0,
                        checked((uint)description.VertexRootConstantCount)),
                    ShaderVisibility.Vertex));
            if (description.VertexConstantsInBuffer)
                rootParameters[0] = new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.Vertex);
        }

        for (int slot = 0;
             slot < description.PixelTextureCount;
             slot++)
        {
            rootParameters.Add(
                new RootParameter1(
                    new RootDescriptorTable1(
                        new DescriptorRange1(
                            DescriptorRangeType.ShaderResourceView,
                            1,
                            checked((uint)slot))),
                    ShaderVisibility.Pixel));
        }

        if (description.DirectionalShadowReceiver)
        {
            if (!_directionalShadows.Enabled) throw new NotSupportedException("Shadow receiver pipelines require enabled directional shadows.");
            rootParameters.Add(new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(1, 0), ShaderVisibility.Pixel));
            rootParameters.Add(new RootParameter1(new RootDescriptorTable1(new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 15)), ShaderVisibility.Pixel));
        }

        if (description.SceneDepthReceiver)
        {
            if (!_particleDepth) throw new NotSupportedException("Particle depth must be configured before pipeline creation.");
            rootParameters.Add(new RootParameter1(new RootDescriptorTable1(new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 15)), ShaderVisibility.Pixel));
        }

        StaticSamplerDescription[]? samplers =
            description.PixelTextureCount > 0
                ? [
                    new StaticSamplerDescription(
                        0,
                        Filter.Anisotropic,
                        TextureAddressMode.Wrap,
                        TextureAddressMode.Wrap,
                        TextureAddressMode.Wrap,
                        maxAnisotropy: 8,
                        comparisonFunction: ComparisonFunction.Never,
                        shaderVisibility: ShaderVisibility.Pixel),
                    new StaticSamplerDescription(
                        1,
                        Filter.MinMagMipLinear,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp,
                        maxAnisotropy: 1,
                        comparisonFunction: ComparisonFunction.Never,
                        shaderVisibility: ShaderVisibility.Pixel)
                ]
                : null;

        ID3D12RootSignature rootSignature =
            _device.CreateRootSignature(
                new RootSignatureDescription1(
                    rootSignatureFlags,
                    rootParameters.Count == 0
                        ? null
                        : rootParameters.ToArray(),
                    samplers));
        ID3D12PipelineState? pipelineState = null;
        try
        {
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
                RasterizerState = description.CullMode switch
                {
                    GraphicsCullMode.CounterClockwise =>
                        RasterizerDescription.CullCounterClockwise,
                    GraphicsCullMode.None =>
                        RasterizerDescription.CullNone,
                    GraphicsCullMode.Clockwise =>
                        RasterizerDescription.CullClockwise,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(description),
                        description.CullMode,
                        "Unsupported graphics cull mode.")
                },
                BlendState = description.AlphaBlendEnabled
                    ? BlendDescription.NonPremultiplied
                    : BlendDescription.Opaque,
                DepthStencilState = description.DepthEnabled
                    ? DepthStencilDescription.Default
                    : DepthStencilDescription.None,
                RenderTargetFormats = description.TargetFormat == GraphicsFrameTargetFormat.Depth32Float ? [] :
                    [description.TargetFormat == GraphicsFrameTargetFormat.Rgba16Float ? Format.R16G16B16A16_Float : BackBufferFormat],
                DepthStencilFormat = DepthBufferFormat,
                SampleDescription = SampleDescription.Default
            };

            if (description.DepthAlwaysPass)
            {
                var depth = DepthStencilDescription.Default;
                depth.DepthFunc = ComparisonFunction.Always;
                pipelineStateDescription.DepthStencilState = depth;
            }
            if (description.SceneDepthReceiver)
            {
                // A blended scene pixel must not retain the negative clear/debug
                // alpha sentinel consumed by the final output transform.
                var blend = BlendDescription.NonPremultiplied;
                blend.RenderTarget[0].SourceBlendAlpha = Blend.One;
                blend.RenderTarget[0].DestinationBlendAlpha = Blend.Zero;
                pipelineStateDescription.BlendState = blend;
            }

            pipelineState =
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
            pipelineState?.Dispose();
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

    public IGraphicsTexture CreateTexture(
        GraphicsTextureData texture)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(texture);

        GraphicsTextureDescription description =
            texture.Description;
        description.Validate();
        using var frozenSource = FreezeBoundTexture(texture);
        long residentByteCount = texture.ResidentByteCount;

        Format nativeFormat =
            description.Format == GraphicsTextureFormat.R8Unorm ? Format.R8_UNorm :
            description.Format == GraphicsTextureFormat.Bc7Unorm
                ? (description.ColorSpace == GraphicsTextureColorSpace.Srgb ? Format.BC7_UNorm_SRgb : Format.BC7_UNorm)
                : description.ColorSpace == GraphicsTextureColorSpace.Srgb
                ? Format.R8G8B8A8_UNorm_SRgb
                : Format.R8G8B8A8_UNorm;

        ResourceDescription resourceDescription =
            ResourceDescription.Texture2D(
                nativeFormat,
                checked((uint)description.Width),
                checked((uint)description.Height),
                mipLevels: checked((ushort)description.MipCount));

        _device.GetCopyableFootprints(resourceDescription, 0, checked((uint)description.MipCount), 0,
            _uploadLayouts, _uploadRows, _uploadRowSizes, out ulong uploadSize);
        _ = UploadArenaLimits.Capacity(uploadSize);

        ID3D12Resource? resource = null;
        int descriptorIndex = -1;
        GraphicsResourceRetirement.Resource? lifetime = null;
        bool committed = false;

        try
        {
            resource =
                _device.CreateCommittedResource(
                    HeapType.Default,
                    resourceDescription,
                    ResourceStates.CopyDest);
            resource.Name =
                $"ForgeLine Texture {description.Width}x{description.Height} Mips={description.MipCount}";
            ID3D12Resource ownedResource = resource;
            lifetime = RegisterResource(() =>
            {
                if (committed)
                {
                        ReleaseTexture(ownedResource, descriptorIndex, residentByteCount);
                }
                else
                {
                    ownedResource.Dispose();
                }
            }, residentByteCount);

            _boundInputTracker?.Register(lifetime, "texture", texture: description, committed: false);
            UploadTexture(
                resource,
                resourceDescription,
                texture,
                lifetime,
                uploadSize, frozenSource);

            InjectSchedulingFailure(SchedulingOperation.AllocateDescriptor);
            descriptorIndex =
                _shaderResourceDescriptors.Allocate();

            CpuDescriptorHandle cpuHandle =
                new(
                    _shaderResourceHeap.GetCPUDescriptorHandleForHeapStart(),
                    descriptorIndex,
                    _shaderResourceDescriptorSize);
            GpuDescriptorHandle gpuHandle =
                new(
                    _shaderResourceHeap.GetGPUDescriptorHandleForHeapStart(),
                    descriptorIndex,
                    _shaderResourceDescriptorSize);

            _device.CreateShaderResourceView(
                resource,
                null,
                cpuHandle);

            _boundInputTracker?.Commit(lifetime, descriptorIndex, "immutable-texture");
            _loadedTextureCount++;
            _residentTextureBytes =
                checked(
                    _residentTextureBytes +
                    texture.ResidentByteCount);
            _textureUploadCount++;
            _peakLoadedTextureCount =
                Math.Max(
                    _peakLoadedTextureCount,
                    _loadedTextureCount);
            _peakResidentTextureBytes =
                Math.Max(
                    _peakResidentTextureBytes,
                    _residentTextureBytes);
            _peakShaderResourceDescriptorsUsed =
                Math.Max(
                    _peakShaderResourceDescriptorsUsed,
                    _shaderResourceDescriptors.UsedCount);

            committed = true;
            return new D3D12GraphicsTexture(
                this,
                description,
                resource,
                descriptorIndex,
                gpuHandle,
                texture.ResidentByteCount,
                lifetime);
        }
        catch
        {
            if (descriptorIndex >= 0)
            {
                _shaderResourceDescriptors.Release(
                    descriptorIndex);
            }

            if (lifetime is not null)
            {
                RetireResource(lifetime);
            }
            else
            {
                resource?.Dispose();
            }
            throw;
        }
    }

    public ulong PresentedFrameCount => _presentedFrameCount;

    public void RenderFrame(
        GraphicsColor clearColor,
        Action<IGraphicsCommandContext>? recordCommands = null)
    {
        ThrowIfDisposed();
        ThrowIfRecording();
        InvalidatePostFrameStamp();
        try
        {
            long started = !_telemetryActive ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
            _cpuIntervals = new(_submittedFrameCount, new(started, started), new(started, started), new(started, started), false);
            RenderFrameCore(clearColor, recordCommands);
            if (_telemetryActive) _cpuIntervals = _cpuIntervals with { RenderFrame = new(started, System.Diagnostics.Stopwatch.GetTimestamp()),
                Submission = _submittedFrameCount, Submitted = _cpuIntervals.Submitted, SubmittedFence = _lastSubmittedFrameFence,
                Generation = _telemetryGeneration, ResourceGeneration = checked((ulong)_surfaceLifecycle.AppliedResizeGeneration + 1) };
        }
        catch (Exception exception)
        {
            _submissionFaultCount++;
            _failure = exception as GraphicsDeviceException ??
                new GraphicsDeviceException("Graphics frame failed; restart the client session.", exception);
            Console.Error.WriteLine($"[graphics:frame-failed] reason={_failure.ReasonCode} message={exception.Message}");
            throw;
        }
        finally
        {
            _recording = false;
        }
    }

    private static Color4 RenderTargetClearColor(GraphicsColor color, bool sceneOutput) =>
        new(color.Red, color.Green, color.Blue, sceneOutput ? -1.0f - color.Alpha : color.Alpha);

    private void RenderFrameCore(GraphicsColor clearColor, Action<IGraphicsCommandContext>? recordCommands)
    {
        _resources.Collect(GetCompletedFence());

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

        long waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        WaitForFrame(_frameIndex);
        _frameWaitMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(waitStarted).TotalMilliseconds;
        if (_telemetryActive) _cpuIntervals = _cpuIntervals with { FenceWait = new(waitStarted, System.Diagnostics.Stopwatch.GetTimestamp()) };
        _resources.Collect(GetCompletedFence());
        ReadCompletedGpuTiming(
            _frameIndex);

        ID3D12CommandAllocator allocator = _commandAllocators[_frameIndex];
        allocator.Reset();
        _commandList.Reset(allocator);
        _recording = true;
        _constantOffset = 256;
        _shadowRecorded = _shadowRecording = _worldDrawRecorded = false;
        _particleRecording = _particleRecorded = false;
        _atmosphereView = null;
        _recordedAtmosphereApplied = false;
        _recordedAtmosphereDensity = 0;
        _shadowDraws = _shadowCasters = 0;
        _shadowCpu = null;
        _passState = default;
        _recordedCompositeCpuMilliseconds = null;
        _frameHasOverlay[_frameIndex] = false;
        _passStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        BeginGpuTiming(
            _frameIndex);

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
        if (_sceneOutput.Enabled)
        {
            _commandList.ResourceBarrierTransition(_sceneTargets![_frameIndex],
                ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            rtv = SceneRtv(_frameIndex);
            _commandList.OMSetRenderTargets(rtv, dsv);
        }
        _commandList.ClearRenderTargetView(
            rtv,
            RenderTargetClearColor(clearColor, _sceneOutput.Enabled));
        _commandList.ClearDepthStencilView(
            dsv,
            ClearFlags.Depth,
            1.0f,
            0);

        if (_overlayDepthTarget is not null)
            _commandList.ClearDepthStencilView(OverlayDsv, ClearFlags.Depth, 1, 0);

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
            context.Width,
            context.Height);
        context.SetScissor(
            0,
            0,
            context.Width,
            context.Height);
        try
        {
            recordCommands?.Invoke(context);
            if (_shadowRecording || _particleRecording) throw new InvalidOperationException("The shadow or particle submission was not ended.");
            if (_sceneOutput.Enabled && _passState.Current == GraphicsFramePass.World)
                context.BeginPass(GraphicsFramePass.Overlay);
        }
        finally
        {
            context.Close();
        }

        FinishPassTiming();
        EndGpuTiming(
            _frameIndex);
        RecordFrameCapture(backBuffer);

        _commandList.ResourceBarrierTransition(
            backBuffer,
            ResourceStates.RenderTarget,
            ResourceStates.Present);
        _commandList.Close();

        FlushUploads();
        InjectSchedulingFailure(SchedulingOperation.ExecuteFrame);
        _commandQueue.ExecuteCommandList(_commandList);
        _submittedFrameCount++;
        SignalSubmittedFrame();
        PublishDirectionalShadowSubmission();
        _submittedAtmosphereApplied = _recordedAtmosphereApplied;
        _submittedAtmosphereDensity = _recordedAtmosphereDensity;
        _cpuTimingSubmission = _submittedFrameCount;
        _worldCpuMilliseconds = Math.Max(0, _recordedWorldCpuMilliseconds - (_shadowCpu ?? 0));
        _overlayCpuMilliseconds = _recordedOverlayCpuMilliseconds;
        _compositeCpuMilliseconds = _recordedCompositeCpuMilliseconds;
        _recording = false;

        long presentStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        InjectSchedulingFailure(SchedulingOperation.Present);
        var presentResult = _swapChain.Present(
            _configuration.EnableVSync ? 1u : 0u,
            PresentFlags.None);
        _presentMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(presentStarted).TotalMilliseconds;
        if (_telemetryActive) _cpuIntervals = _cpuIntervals with { Present = new(presentStarted, System.Diagnostics.Stopwatch.GetTimestamp()), Submitted = true };

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

        _frameIndex = GetCurrentBackBufferIndex();
    }

    public void Resize(int width, int height)
    {
        ThrowIfDisposed();
        ThrowIfRecording();

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
        ThrowIfRecording();
        WaitForIdleCore();
    }

    private void WaitForIdleCore(bool provenanceCleanup = false)
    {
        FlushUploads(provenanceCleanup);

        ulong fenceValue = _nextFenceValue++;
        CheckResult(_commandQueue.Signal(_frameFence, fenceValue), "fence-signal-failed");

        WaitForFence(fenceValue);
        ulong completed = GetCompletedFence();
        _resources.Collect(completed);
        _postFrameIdleObserved = completed;

        CaptureCompletedGpuTimings();
        Array.Clear(_frameFenceValues);
    }

    public void Dispose()
    {
        VerifyOwner();
        ThrowIfRecording();
        if (_disposed)
        {
            return;
        }

        InjectSchedulingFailure(SchedulingOperation.Dispose);
        try
        {
            WaitForIdleCore(provenanceCleanup: true);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[graphics:shutdown-warning] type={exception.GetType().Name} " +
                $"message={exception.Message}");
            if (!_device.DeviceRemovedReason.Failure)
            {
                // Retain native ownership if a live GPU has not retired its work.
                _ = Interlocked.CompareExchange(ref s_unretiredDevice, this, null);
                throw;
            }
        }

        _resources.ReleaseAll();
        DisposeNativeObjects();
        _disposed = true;
    }

    private void DisposeNativeObjects()
    {
        InvalidateTextureBindings();
        ReleaseUploadArena();
        ReleaseDirectionalShadows();
        ReleaseParticleQueries();
        if (_renderTargets is not null)
        {
            ReleaseRenderTargets();
        }

        foreach (ID3D12CommandAllocator? allocator in _commandAllocators ?? [])
        {
            allocator?.Dispose();
        }

        _timestampReadback?.Dispose();
        _timestampQueryHeap?.Dispose();
        _debugInfoQueue?.Dispose();
        _commandList?.Dispose();
        _shaderResourceHeap?.Dispose();
        _dsvHeap?.Dispose();
        _rtvHeap?.Dispose();
        _swapChain?.Dispose();
        _frameFence?.Dispose();
        _frameFenceEvent?.Dispose();
        _commandQueue?.Dispose();

#if DEBUG
        if (_deviceInfo?.DebugLayerEnabled == true)
        {
            using ID3D12DebugDevice? debugDevice =
                _device.QueryInterfaceOrNull<ID3D12DebugDevice>();
            debugDevice?.ReportLiveDeviceObjects(
                ReportLiveDeviceObjectFlags.Detail |
                ReportLiveDeviceObjectFlags.IgnoreInternal);
        }
#endif

        _device?.Dispose();
        _factory?.Dispose();
    }

    internal ID3D12DescriptorHeap ShaderResourceHeap =>
        _shaderResourceHeap;

    internal void RecordTextureBindingFailure()
    {
        _textureBindingFailureCount++;
    }

    internal void ReleaseTexture(
        ID3D12Resource resource,
        int descriptorIndex,
        long residentByteCount)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (_disposed)
        {
            resource.Dispose();
            return;
        }

        resource.Dispose();
        _shaderResourceDescriptors.Release(
            descriptorIndex);
        _loadedTextureCount--;
        _residentTextureBytes =
            checked(
                _residentTextureBytes -
                residentByteCount);
        _textureReleaseCount++;
    }

    private void CaptureDebugLayerMessages()
    {
        if (_debugInfoQueue is null)
        {
            return;
        }

        try
        {
            ulong messageCount =
                _debugInfoQueue.NumStoredMessages;

            for (ulong index = 0;
                 index < messageCount;
                 index++)
            {
                Message message =
                    _debugInfoQueue.GetMessage(
                        index);

                if (message.Severity is MessageSeverity.Warning or MessageSeverity.Error or MessageSeverity.Corruption &&
                    _debugWarningCount + _debugErrorCount < 16)
                {
                    Console.Error.WriteLine(
                        $"[graphics:debug-layer] severity={message.Severity} id={message.Id} description={message.Description.TrimEnd('\0').ReplaceLineEndings(" ")}");
                }

                switch (message.Severity)
                {
                    case MessageSeverity.Warning:
                        _debugWarningCount++;
                        break;

                    case MessageSeverity.Error:
                    case MessageSeverity.Corruption:
                        _debugErrorCount++;
                        break;
                }
            }

            if (messageCount > 0)
            {
                _debugInfoQueue.ClearStoredMessages();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[graphics:debug-diagnostics-warning] type={exception.GetType().Name} message={exception.Message}");
        }
    }

    private void TryInitializeGpuTiming(
        int bufferCount)
    {
        try
        {
            if (_commandQueue.GetTimestampFrequency(
                    out ulong frequency).Failure ||
                frequency == 0)
            {
                _gpuTimingUnavailableReason = "The direct queue does not expose a valid timestamp frequency.";
                return;
            }

            uint queryCount =
                checked(
                    (uint)bufferCount *
                    4U);
            _timestampQueryHeap =
                _device.CreateQueryHeap<ID3D12QueryHeap>(
                    new QueryHeapDescription(
                        QueryHeapType.Timestamp,
                        queryCount));
            _timestampReadback =
                _device.CreateCommittedResource(
                    HeapType.Readback,
                    ResourceDescription.Buffer(
                        checked(
                            (ulong)queryCount *
                            sizeof(ulong))),
                    ResourceStates.CopyDest);
            _timestampReadback.Name =
                "ForgeLine GPU Timestamp Readback";
            _timestampFrequency =
                frequency;
        }
        catch (Exception exception)
        {
            _gpuTimingUnavailableReason = exception.Message;
            _timestampReadback?.Dispose();
            _timestampReadback =
                null;
            _timestampQueryHeap?.Dispose();
            _timestampQueryHeap =
                null;
            _timestampFrequency =
                0;
            Console.Error.WriteLine(
                $"[graphics:gpu-timing-unavailable] type={exception.GetType().Name} message={exception.Message}");
        }
    }

    private void BeginGpuTiming(
        int frameIndex)
    {
        if (_timestampQueryHeap is null)
        {
            return;
        }

        uint queryIndex =
            checked(
                (uint)frameIndex *
                4U);
        _commandList.EndQuery(
            _timestampQueryHeap,
            QueryType.Timestamp,
            queryIndex);
    }

    private void EndGpuTiming(
        int frameIndex)
    {
        if (_timestampQueryHeap is null ||
            _timestampReadback is null)
        {
            return;
        }

        uint queryIndex =
            checked(
                (uint)frameIndex *
                4U);
        _commandList.EndQuery(
            _timestampQueryHeap,
            QueryType.Timestamp,
            queryIndex + 1U);
        _commandList.ResolveQueryData(
            _timestampQueryHeap,
            QueryType.Timestamp,
            queryIndex,
            4,
            _timestampReadback,
            checked(
                (ulong)queryIndex *
                sizeof(ulong)));
        _frameTimestampReady[frameIndex] =
            true;
    }

    private void CaptureCompletedGpuTimings()
    {
        if (_timestampReadback is null ||
            _timestampFrequency == 0)
        {
            return;
        }

        ulong completedFence =
            _frameFence.CompletedValue;
        // Device removal is not completed work and must never publish a timing sample.
        if (completedFence == ulong.MaxValue)
            return;

        for (int frameIndex = 0;
             frameIndex < _frameTimestampReady.Length;
             frameIndex++)
        {
            ulong fenceValue =
                _frameFenceValues[frameIndex];

            if (_frameTimestampReady[frameIndex] &&
                fenceValue != 0 &&
                completedFence >= fenceValue)
            {
                ReadCompletedGpuTiming(
                    frameIndex);
            }
        }
    }

    private void ReadCompletedGpuTiming(
        int frameIndex)
    {
        if (!_frameTimestampReady[frameIndex] ||
            _timestampReadback is null ||
            _timestampFrequency == 0)
        {
            return;
        }

        int offset =
            checked(
                frameIndex *
                4 *
                sizeof(ulong));
        ulong start =
            _timestampReadback.GetData<ulong>(
                offset);
        ulong end =
            _timestampReadback.GetData<ulong>(
                checked(
                    offset +
                    sizeof(ulong)));

        ulong worldEnd = _timestampReadback.GetData<ulong>(offset + 2 * sizeof(ulong));
        ulong overlayStart = _timestampReadback.GetData<ulong>(offset + 3 * sizeof(ulong));
        RecordRawGpuTiming(frameIndex, start, end, worldEnd, overlayStart);
        ReadCompletedShadowTiming(frameIndex);
        ReadCompletedParticleTiming(frameIndex);

        if (end >= start && _frameFenceValues[frameIndex] > _gpuTimingFence)
        {
            _lastGpuFrameMilliseconds =
                (end - start) *
                1000.0 /
                _timestampFrequency;
            if (worldEnd >= start && overlayStart >= worldEnd && overlayStart <= end)
            {
                _worldGpuMilliseconds = Math.Max(0, (worldEnd - start) * 1000.0 / _timestampFrequency -
                    (_shadowGpuFence == _frameFenceValues[frameIndex] ? _shadowGpu ?? 0 : 0));
                _compositeGpuMilliseconds = _sceneOutput.Enabled
                    ? (overlayStart - worldEnd) * 1000.0 / _timestampFrequency : null;
                _overlayGpuMilliseconds = _frameHasOverlay[frameIndex]
                    ? (end - overlayStart) * 1000.0 / _timestampFrequency : null;
                _gpuTimingFence = _frameFenceValues[frameIndex];
            }
        }

        _frameTimestampReady[frameIndex] =
            false;
    }

    internal GraphicsFrameTargetFormat ActiveTargetFormat => _shadowRecording ? GraphicsFrameTargetFormat.Depth32Float :
        _sceneOutput.Enabled && _passState.Current == GraphicsFramePass.World
        ? GraphicsFrameTargetFormat.Rgba16Float : GraphicsFrameTargetFormat.Rgba8Unorm;

    internal bool BeginPass(GraphicsFramePass pass)
    {
        ValidateRecording();
        if (_shadowRecording || _particleRecording) throw new InvalidOperationException("End shadow/particle submission before entering world or overlay passes.");
        if (!_passState.Enter(pass))
            return false;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        _recordedWorldCpuMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(_passStartedAt, now).TotalMilliseconds;
        WritePassBoundary();
        ComposeScene();
        WriteOverlayBoundary();
        _passStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _frameHasOverlay[_frameIndex] = true;
        return true;
    }

    private void WritePassBoundary()
    {
        if (_timestampQueryHeap is not null)
            _commandList.EndQuery(_timestampQueryHeap, QueryType.Timestamp, checked((uint)_frameIndex * 4U + 2U));
    }

    private void WriteOverlayBoundary()
    {
        if (_timestampQueryHeap is not null)
            _commandList.EndQuery(_timestampQueryHeap, QueryType.Timestamp, checked((uint)_frameIndex * 4U + 3U));
    }

    private void FinishPassTiming()
    {
        double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_passStartedAt).TotalMilliseconds;
        if (_frameHasOverlay[_frameIndex])
            _recordedOverlayCpuMilliseconds = elapsed;
        else
        {
            _recordedWorldCpuMilliseconds = elapsed;
            _recordedOverlayCpuMilliseconds = null;
            WritePassBoundary();
            WriteOverlayBoundary();
        }
    }

    private void UploadTexture(
        ID3D12Resource destination,
        ResourceDescription resourceDescription,
        GraphicsTextureData texture,
        GraphicsResourceRetirement.Resource destinationLifetime,
        ulong size, GraphicsBoundFrozenTexture? frozenSource)
    {
        int count = texture.Description.MipCount;
        UploadBatch batch = AcquireUploadBatch(size);
        ulong offset = UploadArenaLimits.Align(batch.Used);
        InjectSchedulingFailure(SchedulingOperation.MapUpload);
        Span<byte> mapped = batch.Upload.Map<byte>(0, checked((int)batch.Capacity));
        try
        {
            mapped.Slice(checked((int)offset), checked((int)size)).Clear();
            for (int mipIndex = 0; mipIndex < count; mipIndex++)
            {
                GraphicsTextureMipData mip = frozenSource is null ? texture.Mips[mipIndex] : frozenSource.Sources[mipIndex];
                int rowBytes = texture.Description.Format == GraphicsTextureFormat.Bc7Unorm
                    ? checked(((mip.Width + 3) / 4) * 16)
                    : checked(mip.Width * (texture.Description.Format == GraphicsTextureFormat.R8Unorm ? 1 : 4));
                if (_uploadRowSizes[mipIndex] < (ulong)rowBytes)
                    throw new GraphicsDeviceException($"D3D12 footprint for mip {mipIndex} is smaller than its source row.");
                for (int row = 0; row < checked((int)_uploadRows[mipIndex]); row++)
                {
                    ReadOnlySpan<byte> source = frozenSource is null
                        ? mip.Pixels.AsSpan(checked(row * mip.RowPitch), rowBytes)
                        : frozenSource.Bytes(mipIndex).Slice(checked(row * rowBytes), rowBytes);
                    source.CopyTo(mapped.Slice(checked((int)(offset + _uploadLayouts[mipIndex].Offset) + row * (int)_uploadLayouts[mipIndex].Footprint.RowPitch), rowBytes));
                }
            }
        }
        finally { batch.Upload.Unmap(0); }
        for (int mipIndex = 0; mipIndex < count; mipIndex++)
        {
            PlacedSubresourceFootPrint layout = _uploadLayouts[mipIndex];
            layout.Offset += offset;
            batch.Commands.CopyTextureRegion(new TextureCopyLocation(destination, checked((uint)mipIndex)),
                0, 0, 0, new TextureCopyLocation(batch.Upload, layout));
        }
        batch.Commands.ResourceBarrierTransition(destination, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        FinishUploadTransfer(batch, destinationLifetime, offset + size, size);
        if (frozenSource is not null)
        {
            ulong transfer = _boundInputTracker!.Pending(destinationLifetime);
            for (int mip = 0; mip < count; mip++)
            {
                var source = frozenSource.Sources[mip];
                int rowBytes = GraphicsBoundFrozenTexture.RowBytes(texture.Description.Format, source.Width);
                var footprint = _uploadLayouts[mip];
                var layout = new GraphicsTextureReadbackLayout(source.Width, source.Height, checked((int)footprint.Footprint.RowPitch),
                    checked(offset + footprint.Offset), footprint.Footprint.Format.ToString());
                var descriptor = new GraphicsBoundTextureSource(mip, source.Width, source.Height, source.RowPitch, rowBytes,
                    frozenSource.Bytes(mip).Length / rowBytes, layout);
                var bytes = frozenSource.Bytes(mip);
                _boundInputTracker.Write(destinationLifetime, "texture-mip", 0, (ulong)bytes.Length, 1,
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), (ulong)bytes.Length, transfer, descriptor);
            }
        }
        if (!texture.Description.DeferUploadWait)
        {
            FlushUploads();
            WaitForFence(batch.Fence);
        }
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
            (_atmosphere.Enabled || _particleDepth || ScaledScene) ? Format.R32_Typeless : DepthBufferFormat,
            checked((uint)GraphicsFramePlan.ScaledDimension(width, _sceneOutput.ResolutionScale)),
            checked((uint)GraphicsFramePlan.ScaledDimension(height, _sceneOutput.ResolutionScale)),
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
        if (ScaledScene)
        {
            _overlayDepthTarget = _device.CreateCommittedResource(HeapType.Default,
                ResourceDescription.Texture2D(DepthBufferFormat, checked((uint)width), checked((uint)height),
                    flags: ResourceFlags.AllowDepthStencil), ResourceStates.DepthWrite, clearValue);
            _overlayDepthTarget.Name = "ForgeLine Overlay Depth";
            _device.CreateDepthStencilView(_overlayDepthTarget, depthViewDescription, OverlayDsv);
        }
        CreateAtmosphereDepthView();
        CreateSceneTargets(width, height);
    }

    private void ReleaseRenderTargets()
    {
        ReleaseSceneTargets();
        ReleaseAtmosphereDepthView();
        _depthTarget?.Dispose();
        _depthTarget = null;
        _overlayDepthTarget?.Dispose();
        _overlayDepthTarget = null;

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

        InjectSchedulingFailure(SchedulingOperation.Resize);
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
        ResetFrameMeasurements();

        Console.WriteLine(
            $"[graphics:resize-applied] size={request.Width}x{request.Height} " +
            $"generation={request.Generation} frameIndex={_frameIndex} " +
            $"buffers={_configuration.BufferCount}");
    }

    private void ResetFrameMeasurements()
    {
        _particleGpu = null; _particleGpuFence = 0;
        if (_particleSamples is not null) Array.Clear(_particleSamples);
        _submittedAtmosphereApplied = false;
        _submittedAtmosphereDensity = 0;
        _submittedShadowDraws = _submittedShadowCasters = 0;
        _submittedShadowCpu = null;
        _shadowGpu = null;
        _shadowGpuFence = 0;
        if (_shadowSamples is not null) Array.Clear(_shadowSamples);
        _cpuTimingSubmission = 0;
        _gpuTimingFence = 0;
        _worldCpuMilliseconds = 0;
        _overlayCpuMilliseconds = null;
        _worldGpuMilliseconds = null;
        _overlayGpuMilliseconds = null;
        _compositeGpuMilliseconds = null;
        _compositeCpuMilliseconds = null;
        _lastGpuFrameMilliseconds = null;
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
        InjectSchedulingFailure(SchedulingOperation.SignalFrame);
        ulong fenceValue =
            _nextFenceValue++;
        CheckResult(_commandQueue.Signal(
            _frameFence,
            fenceValue), "fence-signal-failed");
        _frameFenceValues[_frameIndex] =
            fenceValue;
        _resources.CompleteRecording(fenceValue);
        _lastSubmittedFrameFence = fenceValue;
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
        // WARP shares CPU capacity with recording. Several individually healthy
        // multi-second frames can otherwise accumulate beyond the idle deadline.
        // Admit one software frame at a time; hardware keeps per-buffer overlap.
        ulong fenceValue = _deviceInfo.IsSoftwareAdapter
            ? _lastSubmittedFrameFence : _frameFenceValues[frameIndex];
        if (fenceValue == 0)
        {
            return;
        }
        WaitForFence(fenceValue);
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
        if (removedReason < 0) CaptureDredFailure();

        return new GraphicsDeviceException(
            $"{message} HRESULT=0x{resultCode:X8}; " +
            $"deviceRemovedReason=0x{removedReason:X8}; " +
            $"adapter=\"{_deviceInfo.AdapterName}\".",
            removedReason < 0 ? "device-removed" : "presentation-failed");
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

    internal static bool IsSoftwareAdapter(in AdapterDescription1 description) =>
        (description.Flags & AdapterFlags.Software) != AdapterFlags.None ||
        // The primary Basic Render Driver can omit the software flag.
        (description.VendorId == 0x1414 && description.DeviceId == 0x8c);

    private static (ID3D12Device Device, GraphicsDeviceInfo Info) CreateDevice(
        IDXGIFactory4 factory,
        bool allowSoftwareAdapterFallback,
        bool debugLayerEnabled,
        bool forceSoftwareAdapter)
    {
        for (uint adapterIndex = 0;
             !forceSoftwareAdapter && factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1? adapter).Success;
             adapterIndex++)
        {
            using (adapter)
            {
                AdapterDescription1 description = adapter!.Description1;
                if (IsSoftwareAdapter(description))
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
        VerifyOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is not null)
        {
            throw _failure;
        }
    }

    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Graphics operations must run on the creating render thread.");
        }
    }

    private void ThrowIfRecording()
    {
        if (_recording)
        {
            throw new InvalidOperationException("This graphics operation is unavailable during frame recording.");
        }
    }

    internal GraphicsResourceRetirement.Resource RegisterResource(Action release, long logicalBytes = 0) => _resources.Register(release, logicalBytes);

    internal void UseResource(GraphicsResourceRetirement.Resource resource)
    {
        ValidateRecording();
        if (_activeUploadBatch >= 0) FlushUploads();
        _resources.Use(resource);
    }

    internal void ValidateRecording()
    {
        ThrowIfDisposed();
        if (!_recording)
        {
            throw new InvalidOperationException("Command recording has ended.");
        }
    }

    internal void ValidateWrite(GraphicsResourceRetirement.Resource resource)
    {
        ThrowIfDisposed();
        resource.ValidateWrite(GetCompletedFence());
    }

    internal void RetireResource(GraphicsResourceRetirement.Resource resource)
    {
        VerifyOwner();
        if (resource.IsDisposed)
        {
            return;
        }
        if (_failure is not null || _device.DeviceRemovedReason.Failure)
        {
            resource.IsDisposed = true;
            return;
        }
        ulong completed = GetCompletedFence();
        _resources.Collect(completed);
        if (_resources.PendingCount == GraphicsResourceRetirement.Capacity && !_recording)
        {
            WaitForIdleCore();
            completed = GetCompletedFence();
        }
        _resources.Retire(resource, completed);
        _boundInputTracker?.Retire(resource);
    }

    private ulong GetCompletedFence()
    {
        ulong value = _frameFence.CompletedValue;
        if (value == ulong.MaxValue)
        {
            throw CreateDeviceFailure("Direct3D 12 device removed during fence observation.", _device.DeviceRemovedReason.Code);
        }
        return value;
    }

    private void WaitForFence(ulong fenceValue)
    {
        if (GetCompletedFence() >= fenceValue)
        {
            return;
        }
        _fenceWaitCount++;
        CheckResult(_frameFence.SetEventOnCompletion(fenceValue, _frameFenceEvent), "fence-event-failed");
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (GetCompletedFence() < fenceValue)
        {
            _ = _frameFenceEvent.WaitOne(100);
            // Completion can occur during the wait that reaches the deadline.
            // Observe it before classifying a timeout; no further wait is granted.
            ulong completed = GetCompletedFence();
            if (completed >= fenceValue) return;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(10))
            {
                throw new GraphicsDeviceException(
                    $"GPU fence did not complete within ten seconds; restart the client session. target={fenceValue}; completed={completed}; lastFrame={_lastSubmittedFrameFence}; software={_deviceInfo.IsSoftwareAdapter}.",
                    "fence-timeout");
            }
        }
    }

    private void CheckResult(SharpGen.Runtime.Result result, string reasonCode)
    {
        if (result.Failure)
        {
            int removedReason = _device.DeviceRemovedReason.Code;
            if (removedReason < 0) CaptureDredFailure();
            throw new GraphicsDeviceException(
                $"Direct3D 12 operation failed. HRESULT=0x{result.Code:X8}; deviceRemovedReason=0x{removedReason:X8}; adapter=\"{_deviceInfo.AdapterName}\".",
                removedReason < 0 ? "device-removed" : reasonCode);
        }
    }
}
