namespace ForgeLine.Graphics;

public interface IGraphicsPipeline : IDisposable
{
    GraphicsPipelineDescription Description { get; }
}
