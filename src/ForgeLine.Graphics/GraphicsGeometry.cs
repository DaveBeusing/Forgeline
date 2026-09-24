namespace ForgeLine.Graphics;

public enum GraphicsVertexElementFormat
{
    Float2,
    Float3,
    Float4
}

public readonly record struct GraphicsVertexElement(
    string SemanticName,
    int SemanticIndex,
    GraphicsVertexElementFormat Format,
    int OffsetInBytes)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(SemanticName))
        {
            throw new ArgumentException(
                "A vertex semantic name is required.",
                nameof(SemanticName));
        }

        if (SemanticIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SemanticIndex));
        }

        if (OffsetInBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(OffsetInBytes));
        }
    }
}

public enum GraphicsIndexFormat
{
    SixteenBit,
    ThirtyTwoBit
}
