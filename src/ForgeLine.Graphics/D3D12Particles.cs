using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed partial class D3D12GraphicsDevice
{
    private bool _particleDepth, _particleRecording, _particleRecorded;
    private ID3D12QueryHeap? _particleQueries;
    private ID3D12Resource? _particleReadback;
    private bool[]? _particleSamples;
    private ulong _particleGpuFence;
    private double? _particleGpu;

    public bool SupportsParticleRendering => true;

    public void ConfigureParticleDepth(bool enabled)
    {
        ThrowIfDisposed(); ThrowIfRecording();
        if (enabled == _particleDepth) return;
        WaitForIdle();
        ReleaseRenderTargets(); ReleaseParticleQueries();
        _particleDepth = enabled;
        ResetFrameMeasurements();
        try
        {
            CreateRenderTargets(_surfaceLifecycle.Width, _surfaceLifecycle.Height);
            if (enabled && _timestampFrequency > 0)
            {
                _particleQueries = _device.CreateQueryHeap<ID3D12QueryHeap>(new(QueryHeapType.Timestamp, checked((uint)_configuration.BufferCount * 2)));
                _particleReadback = _device.CreateCommittedResource(HeapType.Readback,
                    ResourceDescription.Buffer(checked((ulong)_configuration.BufferCount * 16)), ResourceStates.CopyDest);
                _particleSamples = new bool[_configuration.BufferCount];
            }
        }
        catch (Exception exception)
        {
            ReleaseRenderTargets(); ReleaseParticleQueries();
            _failure = new GraphicsDeviceException("Particle depth configuration failed.", exception);
            throw;
        }
    }

    internal void BeginParticlePass()
    {
        ValidateRecording();
        if (!_particleDepth || _shadowRecording || _particleRecording || _particleRecorded || _passState.Current != GraphicsFramePass.World)
            throw new InvalidOperationException("Particles require one bounded submission after opaque world and before overlay.");
        _particleRecording = _particleRecorded = true;
        if (_particleQueries is not null)
            _commandList.EndQuery(_particleQueries, QueryType.Timestamp, checked((uint)_frameIndex * 2));
        _commandList.OMSetRenderTargets(_sceneOutput.Enabled ? SceneRtv(_frameIndex) :
            new CpuDescriptorHandle(_rtvHeap.GetCPUDescriptorHandleForHeapStart(), _frameIndex, _rtvDescriptorSize));
        _commandList.ResourceBarrierTransition(_depthTarget!, ResourceStates.DepthWrite, ResourceStates.PixelShaderResource);
    }

    internal void EndParticlePass()
    {
        ValidateRecording();
        if (!_particleRecording) throw new InvalidOperationException("No particle submission is active.");
        _commandList.ResourceBarrierTransition(_depthTarget!, ResourceStates.PixelShaderResource, ResourceStates.DepthWrite);
        _commandList.OMSetRenderTargets(_sceneOutput.Enabled ? SceneRtv(_frameIndex) :
            new CpuDescriptorHandle(_rtvHeap.GetCPUDescriptorHandleForHeapStart(), _frameIndex, _rtvDescriptorSize), _dsvHeap.GetCPUDescriptorHandleForHeapStart());
        if (_particleQueries is not null)
        {
            uint query = checked((uint)_frameIndex * 2);
            _commandList.EndQuery(_particleQueries, QueryType.Timestamp, query + 1);
            _commandList.ResolveQueryData(_particleQueries, QueryType.Timestamp, query, 2, _particleReadback!, checked((ulong)_frameIndex * 16));
            _particleSamples![_frameIndex] = true;
        }
        _particleRecording = false;
    }

    internal void ValidateParticlePipeline(D3D12GraphicsPipeline pipeline)
    {
        if (pipeline.Description.SceneDepthReceiver != _particleRecording)
            throw new InvalidOperationException("Depth-aware particle pipelines must be confined to the particle submission.");
        if (!_particleRecording) return;
        _commandList.SetDescriptorHeaps(_shaderResourceHeap);
        _commandList.SetGraphicsRootDescriptorTable(checked((uint)(1 + pipeline.Description.PixelTextureCount)),
            new GpuDescriptorHandle(_shaderResourceHeap.GetGPUDescriptorHandleForHeapStart(), _atmosphereDepthDescriptor, _shaderResourceDescriptorSize));
    }

    private void ReadCompletedParticleTiming(int frameIndex)
    {
        if (_particleSamples is null || !_particleSamples[frameIndex] || _particleReadback is null) return;
        ulong fence = _frameFenceValues[frameIndex];
        if (fence > _particleGpuFence)
        {
            ulong start = _particleReadback.GetData<ulong>(frameIndex * 16);
            ulong end = _particleReadback.GetData<ulong>(frameIndex * 16 + 8);
            _particleGpu = end >= start ? (end - start) * 1000.0 / _timestampFrequency : null;
            _particleGpuFence = fence;
        }
        _particleSamples[frameIndex] = false;
    }

    private void ReleaseParticleQueries()
    {
        _particleReadback?.Dispose(); _particleQueries?.Dispose();
        _particleReadback = null; _particleQueries = null; _particleSamples = null;
        _particleGpuFence = 0; _particleGpu = null;
    }
}
