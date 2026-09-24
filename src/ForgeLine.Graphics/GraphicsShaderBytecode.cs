namespace ForgeLine.Graphics;

public sealed class GraphicsShaderBytecode
{
    private readonly byte[] _data;

    internal GraphicsShaderBytecode(
        GraphicsShaderStage stage,
        string entryPoint,
        string sourceName,
        byte[] data)
    {
        Stage = stage;
        EntryPoint = entryPoint;
        SourceName = sourceName;
        _data = data;
    }

    public GraphicsShaderStage Stage { get; }

    public string EntryPoint { get; }

    public string SourceName { get; }

    public ReadOnlyMemory<byte> Data => _data;
}
