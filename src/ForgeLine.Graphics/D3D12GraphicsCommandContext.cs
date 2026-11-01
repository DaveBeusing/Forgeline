using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsCommandContext : IGraphicsCommandContext
{
    private readonly D3D12GraphicsDevice _owner;
    private readonly ID3D12GraphicsCommandList _commandList;

    private D3D12GraphicsPipeline? _pipeline;
    private bool _closed, _heapBound;
    private readonly int _outputWidth, _outputHeight;
    internal void Close() => _closed = true;
    private void InvalidatePipelineBindings()
    {
        _pipeline = null;
        _heapBound = false;
        _owner.InvalidateTextureBindings();
        _owner.WorldBindingRecorder?.InvalidateRoot();
        _owner.BoundInputTracker?.InvalidateRoot();
    }
    private void ValidateRecording()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _owner.ValidateRecording();
    }

    internal D3D12GraphicsCommandContext(
        D3D12GraphicsDevice owner,
        ID3D12GraphicsCommandList commandList,
        int width,
        int height,
        int frameIndex)
    {
        _owner = owner;
        _owner.InvalidateTextureBindings();
        _commandList = commandList;
        _outputWidth = width;
        _outputHeight = height;
        FrameIndex = frameIndex;
    }

    public int Width => _owner.IsWorldPass ? _owner.WorldWidth : _outputWidth;

    public int Height => _owner.IsWorldPass ? _owner.WorldHeight : _outputHeight;

    public int FrameIndex { get; }

    public void BeginParticlePass() { ValidateRecording(); _owner.BeginParticlePass(); InvalidatePipelineBindings(); }
    public void EndParticlePass() { ValidateRecording(); _owner.EndParticlePass(); InvalidatePipelineBindings(); }

    public void SetAtmosphereView(GraphicsAtmosphereView view)
    {
        ValidateRecording();
        _owner.SetAtmosphereView(view);
    }

    public void BeginDirectionalShadowPass(GraphicsDirectionalShadowFrame frame)
    {
        ValidateRecording();
        _owner.BeginDirectionalShadowPass(frame);
        _owner.RecordWorldShadowFrame(frame);
        InvalidatePipelineBindings();
    }

    public void EndDirectionalShadowPass()
    {
        ValidateRecording();
        _owner.EndDirectionalShadowPass();
        _owner.WorldBindingRecorder?.Viewport(0,0,_owner.WorldWidth,_owner.WorldHeight);
        _owner.WorldBindingRecorder?.Scissor(0,0,_owner.WorldWidth,_owner.WorldHeight);
        InvalidatePipelineBindings();
    }

    public void BeginPass(GraphicsFramePass pass)
    {
        ValidateRecording();
        if (_owner.BeginPass(pass))
            InvalidatePipelineBindings();
    }

    public void SetViewport(float x, float y, float width, float height)
    {
        ValidateRecording();
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Viewport dimensions must be positive.");
        }

        _commandList.RSSetViewport(new Viewport(x, y, width, height));
        _owner.WorldBindingRecorder?.Viewport(x,y,width,height);
    }

    public void SetScissor(int left, int top, int right, int bottom)
    {
        ValidateRecording();
        if (right <= left || bottom <= top)
        {
            throw new ArgumentOutOfRangeException(
                nameof(right),
                "Scissor bounds must describe a positive area.");
        }

        RectI rectangle = RectI.FromLTRB(left, top, right, bottom);
        _commandList.RSSetScissorRect(rectangle);
        _owner.WorldBindingRecorder?.Scissor(left,top,right,bottom);
    }

    public void SetPipeline(IGraphicsPipeline pipeline)
    {
        ValidateRecording();
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline.Description.TargetFormat != _owner.ActiveTargetFormat)
            throw new ArgumentException("Pipeline output format does not match the active frame pass.", nameof(pipeline));

        if (pipeline is not D3D12GraphicsPipeline d3d12Pipeline ||
            !ReferenceEquals(d3d12Pipeline.Owner, _owner))
        {
            throw new ArgumentException(
                "The graphics pipeline was not created by this graphics device.",
                nameof(pipeline));
        }

        _owner.UseResource(d3d12Pipeline.Lifetime);
        if (ReferenceEquals(_pipeline, d3d12Pipeline))
        {
            _owner.ValidateParticlePipeline(d3d12Pipeline);
            _owner.RecordWorldPipeline(d3d12Pipeline,false);
            return;
        }
        _heapBound = false;
        _owner.InvalidateTextureBindings();
        _pipeline = d3d12Pipeline;
        _commandList.SetGraphicsRootSignature(d3d12Pipeline.RootSignature);
        _commandList.SetPipelineState(d3d12Pipeline.PipelineState);
        if (pipeline.Description.DirectionalShadowReceiver) _owner.BindDirectionalShadowReceiver(d3d12Pipeline);
        _owner.ValidateParticlePipeline(d3d12Pipeline);
        _owner.RecordWorldPipeline(d3d12Pipeline,true);
        _commandList.IASetPrimitiveTopology(
            d3d12Pipeline.Description.PrimitiveTopology switch
            {
                GraphicsPrimitiveTopology.TriangleList =>
                    PrimitiveTopology.TriangleList,
                GraphicsPrimitiveTopology.LineList =>
                    PrimitiveTopology.LineList,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(pipeline),
                    d3d12Pipeline.Description.PrimitiveTopology,
                    "Unsupported graphics primitive topology.")
            });
    }

    public void SetVertexBuffer(
        IGraphicsBuffer buffer,
        int strideInBytes,
        int offsetInBytes = 0,
        int inputSlot = 0)
    {
        D3D12GraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(strideInBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(inputSlot);

        ulong remaining = ValidateBufferOffset(d3d12Buffer, offsetInBytes);
        var view = new VertexBufferView(
            d3d12Buffer.Resource.GPUVirtualAddress + (ulong)offsetInBytes,
            checked((uint)remaining),
            checked((uint)strideInBytes));

        _commandList.IASetVertexBuffers(
            checked((uint)inputSlot),
            view);
        if (_owner.WorldBindingRecorder is { } journal)
        {
            ulong identity = journal.Define(d3d12Buffer.Lifetime, "buffer", buffer: d3d12Buffer.Description);
            journal.Vertex(identity, inputSlot, strideInBytes, offsetInBytes, remaining);
            _owner.BoundInputTracker?.BindBuffer(d3d12Buffer.Lifetime, identity, false, inputSlot, checked((ulong)offsetInBytes), remaining);
        }
    }

    public void SetIndexBuffer(
        IGraphicsBuffer buffer,
        GraphicsIndexFormat format,
        int offsetInBytes = 0)
    {
        D3D12GraphicsBuffer d3d12Buffer = ValidateBuffer(buffer);
        ulong remaining = ValidateBufferOffset(d3d12Buffer, offsetInBytes);

        Format nativeFormat = format switch
        {
            GraphicsIndexFormat.SixteenBit => Format.R16_UInt,
            GraphicsIndexFormat.ThirtyTwoBit => Format.R32_UInt,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        _commandList.IASetIndexBuffer(
            d3d12Buffer.Resource.GPUVirtualAddress + (ulong)offsetInBytes,
            checked((uint)remaining),
            nativeFormat);
        if (_owner.WorldBindingRecorder is { } journal)
        {
            ulong identity = journal.Define(d3d12Buffer.Lifetime, "buffer", buffer: d3d12Buffer.Description);
            journal.Index(identity, format, offsetInBytes, remaining);
            _owner.BoundInputTracker?.BindBuffer(d3d12Buffer.Lifetime, identity, true, 0, checked((ulong)offsetInBytes), remaining);
        }
    }

    public void SetVertexConstants(ReadOnlySpan<float> values)
    {
        ValidateRecording();
        if (_pipeline is null)
        {
            throw new InvalidOperationException(
                "A graphics pipeline must be bound before setting root constants.");
        }

        int expectedCount = _pipeline.Description.VertexRootConstantCount;
        if (expectedCount == 0)
        {
            throw new InvalidOperationException(
                "The current graphics pipeline does not declare vertex root constants.");
        }

        if (values.Length != expectedCount)
        {
            throw new ArgumentException(
                $"The current graphics pipeline expects {expectedCount} vertex constants.",
                nameof(values));
        }

        if (_owner.BoundInputTracker is not null)
        {
            if (values.Length > 64) throw new InvalidOperationException("Bound constants exceed64words.");
            Span<float> frozen = stackalloc float[64];
            values.CopyTo(frozen);
            SetVertexConstantsCore(frozen[..values.Length]);
        }
        else SetVertexConstantsCore(values);
    }

    private void SetVertexConstantsCore(ReadOnlySpan<float> values)
    {
        _owner.RecordCaptureInput(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values));
        if (_pipeline!.Description.VertexConstantsInBuffer) _owner.SetBufferedVertexConstants(values);
        else _commandList.SetGraphicsRoot32BitConstants(0, values);
        _owner.RecordWorldConstants(values, _pipeline.Description.VertexConstantsInBuffer);
    }

    public void SetPixelTexture(
        int slot,
        IGraphicsTexture texture)
    {
        ValidateRecording();
        if (_pipeline is null)
        {
            _owner.RecordTextureBindingFailure();
            throw new InvalidOperationException(
                "A graphics pipeline must be bound before setting pixel textures.");
        }

        if (slot < 0 ||
            slot >= _pipeline.Description.PixelTextureCount)
        {
            _owner.RecordTextureBindingFailure();
            throw new ArgumentOutOfRangeException(
                nameof(slot),
                slot,
                $"The current graphics pipeline declares {_pipeline.Description.PixelTextureCount} pixel textures.");
        }

        ArgumentNullException.ThrowIfNull(texture);

        if (texture is not D3D12GraphicsTexture d3d12Texture ||
            !ReferenceEquals(d3d12Texture.Owner, _owner))
        {
            _owner.RecordTextureBindingFailure();
            throw new ArgumentException(
                "The graphics texture was not created by this graphics device.",
                nameof(texture));
        }

        if (d3d12Texture.IsDisposed)
        {
            _owner.RecordTextureBindingFailure();
            throw new ObjectDisposedException(
                nameof(texture),
                "Disposed graphics textures cannot be rebound.");
        }

        _owner.UseResource(d3d12Texture.Lifetime);
        bool cached=_owner.TextureBindingMatches(slot,d3d12Texture);
        if (_owner.WorldBindingRecorder is { } journal)
        {
            ulong identity = journal.Define(d3d12Texture.Lifetime, "texture", texture: d3d12Texture.Description, descriptor: d3d12Texture.DescriptorIndex);
            journal.Texture(identity, slot, !cached);
            _owner.BoundInputTracker?.BindTexture(d3d12Texture.Lifetime, identity, slot);
        }
        if (cached) return;
        if (!_heapBound)
        {
            _commandList.SetDescriptorHeaps(_owner.ShaderResourceHeap);
            _heapBound = true;
        }

        int rootParameterIndex =
            (_pipeline.Description.VertexRootConstantCount > 0 ? 1 : 0) +
            slot;

        _commandList.SetGraphicsRootDescriptorTable(
            checked((uint)rootParameterIndex),
            d3d12Texture.GpuDescriptorHandle);
        _owner.RememberTextureBinding(slot, d3d12Texture);
    }

    public void Draw(int vertexCount, int startVertex = 0)
    {
        ValidateRecording();
        if (vertexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(vertexCount),
                vertexCount,
                "Draw calls must contain at least one vertex.");
        }

        if (startVertex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startVertex),
                startVertex,
                "The start vertex cannot be negative.");
        }

        _owner.RecordDirectionalShadowDraw(1);
        if (_owner.WorldBindingRecorder is { } journal && _owner.OpaqueWorldBindingPass)
        {
            journal.Draw(false, vertexCount, 1, startVertex, 0, 0);
            _owner.BoundInputTracker?.Draw(journal.RecordedDrawCount - 1);
        }
        _commandList.DrawInstanced(
            checked((uint)vertexCount),
            1,
            checked((uint)startVertex),
            0);
    }

    public void DrawIndexed(
        int indexCount,
        int startIndex = 0,
        int baseVertex = 0) =>
        DrawIndexedInstanced(
            indexCount,
            1,
            startIndex,
            baseVertex);

    public void DrawIndexedInstanced(
        int indexCount,
        int instanceCount,
        int startIndex = 0,
        int baseVertex = 0,
        int startInstance = 0)
    {
        ValidateRecording();
        if (indexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(indexCount),
                indexCount,
                "Indexed draw calls must contain at least one index.");
        }

        if (instanceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instanceCount),
                instanceCount,
                "Instanced draw calls must contain at least one instance.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(startInstance);

        _owner.RecordDirectionalShadowDraw(instanceCount);
        if (_owner.WorldBindingRecorder is { } journal && _owner.OpaqueWorldBindingPass)
        {
            journal.Draw(true, indexCount, instanceCount, startIndex, baseVertex, startInstance);
            _owner.BoundInputTracker?.Draw(journal.RecordedDrawCount - 1);
        }
        _commandList.DrawIndexedInstanced(
            checked((uint)indexCount),
            checked((uint)instanceCount),
            checked((uint)startIndex),
            baseVertex,
            checked((uint)startInstance));
    }

    private D3D12GraphicsBuffer ValidateBuffer(IGraphicsBuffer buffer)
    {
        ValidateRecording();
        ArgumentNullException.ThrowIfNull(buffer);

        if (buffer is not D3D12GraphicsBuffer d3d12Buffer ||
            !ReferenceEquals(d3d12Buffer.Owner, _owner))
        {
            throw new ArgumentException(
                "The graphics buffer was not created by this graphics device.",
                nameof(buffer));
        }

        _owner.UseResource(d3d12Buffer.Lifetime);
        return d3d12Buffer;
    }

    private static ulong ValidateBufferOffset(
        D3D12GraphicsBuffer buffer,
        int offsetInBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);

        ulong offset = (ulong)offsetInBytes;
        if (offset >= buffer.Description.SizeInBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetInBytes));
        }

        return buffer.Description.SizeInBytes - offset;
    }
}
