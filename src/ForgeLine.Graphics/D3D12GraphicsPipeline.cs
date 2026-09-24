using Vortice.Direct3D12;

namespace ForgeLine.Graphics;

internal sealed class D3D12GraphicsPipeline : IGraphicsPipeline
{
    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pipelineState;

    internal D3D12GraphicsPipeline(
        D3D12GraphicsDevice owner,
        GraphicsPipelineDescription description,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipelineState)
    {
        Owner = owner;
        Description = description;
        _rootSignature = rootSignature;
        _pipelineState = pipelineState;
    }

    public GraphicsPipelineDescription Description { get; }

    internal D3D12GraphicsDevice Owner { get; }

    internal ID3D12RootSignature RootSignature =>
        _rootSignature ?? throw new ObjectDisposedException(nameof(D3D12GraphicsPipeline));

    internal ID3D12PipelineState PipelineState =>
        _pipelineState ?? throw new ObjectDisposedException(nameof(D3D12GraphicsPipeline));

    public void Dispose()
    {
        _pipelineState?.Dispose();
        _pipelineState = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }
}
