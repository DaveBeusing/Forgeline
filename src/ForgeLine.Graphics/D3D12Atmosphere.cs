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
        { RequiredScenePayloadBytes = _atmosphere.Enabled ? checked((long)WorldWidth * WorldHeight * 8 * _configuration.BufferCount) : 0 };

    private void CreateAtmosphereDepthView()
    {
        if (!_atmosphere.Enabled && !_particleDepth && !ScaledScene) return;
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
        _recordedAtmosphereApplied = _atmosphere.Enabled && _atmosphereView.HasValue;
        if (!_atmosphere.Enabled || !_atmosphereView.HasValue) return;
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

}
