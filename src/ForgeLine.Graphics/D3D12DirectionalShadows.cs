using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private const ResourceStates ShadowReadState = ResourceStates.PixelShaderResource;
    private GraphicsDirectionalShadowSettings _directionalShadows;
    private ID3D12Resource[]? _shadowTargets;
    private ID3D12Resource[]? _shadowConstants;
    private int[]? _shadowDescriptors;
    private ID3D12DescriptorHeap? _shadowDsvHeap;
    private ID3D12QueryHeap? _shadowQueries;
    private ID3D12Resource? _shadowReadback;
    private bool[]? _shadowSamples;
    private bool _shadowRecording;
    private bool _shadowRecorded;
    private bool _worldDrawRecorded;
    private int _constantOffset;
    private int _shadowDraws;
    private int _shadowCasters;
    private int _submittedShadowDraws;
    private int _submittedShadowCasters;
    private double? _submittedShadowCpu;
    private long _shadowStarted;
    private double? _shadowCpu;
    private double? _shadowGpu;
    private ulong _shadowGpuFence;

    public GraphicsDirectionalShadowSettings DirectionalShadows
    {
        get { ThrowIfDisposed(); return _directionalShadows; }
    }

    public void ConfigureDirectionalShadows(GraphicsDirectionalShadowSettings settings)
    {
        ThrowIfDisposed();
        ThrowIfRecording();
        settings.Validate();
        if (settings == _directionalShadows) return;
        WaitForIdle();
        ReleaseDirectionalShadows();
        _directionalShadows = settings;
        _shadowCpu = _shadowGpu = null;
        _shadowDraws = _shadowCasters = 0;
        _submittedShadowDraws = _submittedShadowCasters = 0;
        _submittedShadowCpu = null;
        _shadowGpuFence = 0;
        if (!settings.Enabled) return;
        int count = _configuration.BufferCount;
        _shadowTargets = new ID3D12Resource[count];
        _shadowConstants = new ID3D12Resource[count];
        _shadowDescriptors = new int[count];
        Array.Fill(_shadowDescriptors, -1);
        _shadowSamples = new bool[count];
        try
        {
            _shadowDsvHeap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.DepthStencilView, checked((uint)count)));
            for (int i = 0; i < count; i++)
            {
                _shadowTargets[i] = _device.CreateCommittedResource(HeapType.Default,
                    ResourceDescription.Texture2D(Format.R32_Typeless, checked((uint)settings.Resolution), checked((uint)settings.Resolution),
                        mipLevels: 1, flags: ResourceFlags.AllowDepthStencil), ShadowReadState,
                    new ClearValue(Format.D32_Float, 1, 0));
                _shadowTargets[i].Name = $"ForgeLine Directional Shadow {i}";
                _device.CreateDepthStencilView(_shadowTargets[i], new DepthStencilViewDescription
                { Format = Format.D32_Float, ViewDimension = DepthStencilViewDimension.Texture2D }, ShadowDsv(i));
                int descriptor = _shaderResourceDescriptors.Allocate();
                _shadowDescriptors[i] = descriptor;
                _device.CreateShaderResourceView(_shadowTargets[i], new ShaderResourceViewDescription
                {
                    Format = Format.R32_Float, ViewDimension = ShaderResourceViewDimension.Texture2D,
                    Shader4ComponentMapping = ShaderComponentMapping.Default,
                    Texture2D = new Texture2DShaderResourceView { MipLevels = 1 }
                }, new CpuDescriptorHandle(_shaderResourceHeap.GetCPUDescriptorHandleForHeapStart(), descriptor, _shaderResourceDescriptorSize));
                // Slot zero holds shadow frame data; subsequent aligned slots hold per-draw b0 constants.
                _shadowConstants[i] = _device.CreateCommittedResource(HeapType.Upload,
                    ResourceDescription.Buffer(GraphicsDirectionalShadowSettings.ConstantArenaBytesPerFrame), ResourceStates.GenericRead);
                _shadowConstants[i].Name = $"ForgeLine Shadow Frame Constants {i}";
            }
            _peakShaderResourceDescriptorsUsed = Math.Max(_peakShaderResourceDescriptorsUsed, _shaderResourceDescriptors.UsedCount);
            if (_timestampFrequency != 0)
            {
                _shadowQueries = _device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription(QueryHeapType.Timestamp, checked((uint)count * 2)));
                _shadowReadback = _device.CreateCommittedResource(HeapType.Readback, ResourceDescription.Buffer(checked((ulong)count * 16)), ResourceStates.CopyDest);
            }
        }
        catch
        {
            ReleaseDirectionalShadows();
            _directionalShadows = default;
            throw;
        }
    }

    private CpuDescriptorHandle ShadowDsv(int frame) => new(_shadowDsvHeap!.GetCPUDescriptorHandleForHeapStart(), frame,
        _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.DepthStencilView));

    private void ReleaseDirectionalShadows()
    {
        if (_shadowTargets is not null)
            for (int i = 0; i < _shadowTargets.Length; i++)
            {
                _shadowTargets[i]?.Dispose();
                _shadowConstants![i]?.Dispose();
                if (_shadowDescriptors![i] >= 0) _shaderResourceDescriptors.Release(_shadowDescriptors[i]);
            }
        _shadowTargets = _shadowConstants = null;
        _shadowDescriptors = null;
        _shadowDsvHeap?.Dispose();
        _shadowDsvHeap = null;
        _shadowQueries?.Dispose();
        _shadowQueries = null;
        _shadowReadback?.Dispose();
        _shadowReadback = null;
        _shadowSamples = null;
    }

    internal void BeginDirectionalShadowPass(GraphicsDirectionalShadowFrame frame)
    {
        ValidateRecording();
        frame.Validate();
        if (frame.Radius > _directionalShadows.Radius || frame.DepthRange > _directionalShadows.Radius * 4)
            throw new ArgumentException("Shadow frame coverage exceeds the selected quality profile.", nameof(frame));
        if (!_directionalShadows.Enabled || _shadowRecorded || _shadowRecording || _worldDrawRecorded || _passState.Current != GraphicsFramePass.World)
            throw new InvalidOperationException("An enabled shadow pass must precede all world and overlay draws exactly once.");
        _shadowRecording = true;
        _shadowRecorded = true;
        _shadowStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        Span<float> data = stackalloc float[24];
        Matrix4x4 matrix = frame.ViewProjection;
        MemoryMarshal.Cast<float, Matrix4x4>(data[..16])[0] = matrix;
        data[16] = frame.Center.X; data[17] = frame.Center.Y; data[18] = frame.Center.Z; data[19] = frame.Radius;
        data[20] = _directionalShadows.Resolution; data[21] = frame.DepthRange; data[22] = frame.ContactStrength; data[23] = 1;
        _shadowConstants![_frameIndex].SetData<float>(data);
        RecordCaptureInput(MemoryMarshal.AsBytes(data));
        if (_shadowQueries is not null)
            _commandList.EndQuery(_shadowQueries, QueryType.Timestamp, checked((uint)_frameIndex * 2));
        _commandList.ResourceBarrierTransition(_shadowTargets![_frameIndex], ShadowReadState, ResourceStates.DepthWrite);
        _commandList.OMSetRenderTargets(Array.Empty<CpuDescriptorHandle>(), ShadowDsv(_frameIndex));
        _commandList.ClearDepthStencilView(ShadowDsv(_frameIndex), ClearFlags.Depth, 1, 0);
        _commandList.RSSetViewport(0, 0, _directionalShadows.Resolution, _directionalShadows.Resolution);
        _commandList.RSSetScissorRect(Vortice.Mathematics.RectI.FromLTRB(0, 0, _directionalShadows.Resolution, _directionalShadows.Resolution));
    }

    internal void EndDirectionalShadowPass()
    {
        ValidateRecording();
        if (!_shadowRecording) throw new InvalidOperationException("No directional shadow pass is active.");
        _commandList.ResourceBarrierTransition(_shadowTargets![_frameIndex], ResourceStates.DepthWrite, ShadowReadState);
        RecordShadowCapture();
        if (_shadowQueries is not null)
        {
            uint query = checked((uint)_frameIndex * 2);
            _commandList.EndQuery(_shadowQueries, QueryType.Timestamp, query + 1);
            _commandList.ResolveQueryData(_shadowQueries, QueryType.Timestamp, query, 2, _shadowReadback!, checked((ulong)_frameIndex * 16));
            _shadowSamples![_frameIndex] = true;
        }
        _commandList.OMSetRenderTargets(_sceneOutput.Enabled ? SceneRtv(_frameIndex) :
            new CpuDescriptorHandle(_rtvHeap.GetCPUDescriptorHandleForHeapStart(), _frameIndex, _rtvDescriptorSize), _dsvHeap.GetCPUDescriptorHandleForHeapStart());
        _commandList.RSSetViewport(0, 0, _surfaceLifecycle.Width, _surfaceLifecycle.Height);
        _commandList.RSSetScissorRect(Vortice.Mathematics.RectI.FromLTRB(0, 0, _surfaceLifecycle.Width, _surfaceLifecycle.Height));
        _shadowRecording = false;
        _shadowCpu = System.Diagnostics.Stopwatch.GetElapsedTime(_shadowStarted).TotalMilliseconds;
    }

    internal void BindDirectionalShadowReceiver(D3D12GraphicsPipeline pipeline)
    {
        if (!_shadowRecorded || _shadowRecording || !_directionalShadows.Enabled || _passState.Current != GraphicsFramePass.World)
            throw new InvalidOperationException("Shadow receivers require a completed shadow pass in the same frame.");
        uint root = checked((uint)(1 + pipeline.Description.PixelTextureCount));
        _commandList.SetGraphicsRootConstantBufferView(root, _shadowConstants![_frameIndex].GPUVirtualAddress);
        _commandList.SetDescriptorHeaps(_shaderResourceHeap);
        _commandList.SetGraphicsRootDescriptorTable(root + 1, new GpuDescriptorHandle(_shaderResourceHeap.GetGPUDescriptorHandleForHeapStart(),
            _shadowDescriptors![_frameIndex], _shaderResourceDescriptorSize));
    }

    internal void SetBufferedVertexConstants(ReadOnlySpan<float> values)
    {
        if (_shadowConstants is null || _constantOffset >= 4096 * 256)
            throw new InvalidOperationException("The bounded shadow frame constant arena is unavailable or exhausted.");
        _shadowConstants[_frameIndex].SetData(values, _constantOffset);
        _commandList.SetGraphicsRootConstantBufferView(0, _shadowConstants[_frameIndex].GPUVirtualAddress + checked((ulong)_constantOffset));
        _constantOffset += 256;
    }

    internal void RecordDirectionalShadowDraw(int instances)
    {
        if (_particleRecorded && !_particleRecording && _passState.Current == GraphicsFramePass.World)
            throw new InvalidOperationException("Opaque world draws must precede particle submission.");
        if (!_shadowRecording) { _worldDrawRecorded = true; return; }
        if (_shadowDraws >= _directionalShadows.MaximumDraws || instances > _directionalShadows.MaximumCasters - _shadowCasters)
            throw new InvalidOperationException("Directional shadow submissions exceed the selected quality budget.");
        _shadowDraws++;
        _shadowCasters += instances;
    }

    private void ReadCompletedShadowTiming(int frameIndex)
    {
        if (_shadowSamples is null || !_shadowSamples[frameIndex] || _shadowReadback is null) return;
        ulong fence = _frameFenceValues[frameIndex];
        if (fence > _shadowGpuFence)
        {
            ulong start = _shadowReadback.GetData<ulong>(frameIndex * 16);
            ulong end = _shadowReadback.GetData<ulong>(frameIndex * 16 + 8);
            _shadowGpu = end >= start ? (end - start) * 1000.0 / _timestampFrequency : null;
            _shadowGpuFence = fence;
        }
        _shadowSamples[frameIndex] = false;
    }

    private GraphicsDirectionalShadowDiagnostics ShadowDiagnostics => new(_directionalShadows.Quality, _directionalShadows.Resolution,
        _directionalShadows.CascadeCount, _directionalShadows.PayloadBytes(_configuration.BufferCount),
        _directionalShadows.Enabled ? _configuration.BufferCount : 0, _submittedShadowDraws, _submittedShadowCasters, _submittedShadowCpu, _shadowGpuFence, _shadowGpu)
    {
        CpuSubmission = _cpuTimingSubmission,
        ConstantUploadBytes = _directionalShadows.Enabled ? (long)_configuration.BufferCount * 256 * 4096 : 0
    };

    private void PublishDirectionalShadowSubmission()
    {
        _submittedShadowDraws = _shadowDraws;
        _submittedShadowCasters = _shadowCasters;
        _submittedShadowCpu = _shadowCpu;
    }
}
