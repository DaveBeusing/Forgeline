namespace ForgeLine.Graphics;

public enum GraphicsVertexElementFormat
{
    Float2,
    Float3,
    Float4
}

public enum GraphicsVertexInputRate
{
    PerVertex,
    PerInstance
}

public readonly record struct GraphicsVertexElement(
    string SemanticName,
    int SemanticIndex,
    GraphicsVertexElementFormat Format,
    int OffsetInBytes,
    int InputSlot = 0,
    GraphicsVertexInputRate InputRate = GraphicsVertexInputRate.PerVertex,
    int InstanceStepRate = 0)
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

        if (InputSlot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InputSlot));
        }

        if (InstanceStepRate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(InstanceStepRate));
        }

        if (InputRate == GraphicsVertexInputRate.PerVertex &&
            InstanceStepRate != 0)
        {
            throw new ArgumentException(
                "Per-vertex elements must use an instance step rate of zero.",
                nameof(InstanceStepRate));
        }

        if (InputRate == GraphicsVertexInputRate.PerInstance &&
            InstanceStepRate <= 0)
        {
            throw new ArgumentException(
                "Per-instance elements must use a positive instance step rate.",
                nameof(InstanceStepRate));
        }
    }
}

public enum GraphicsIndexFormat
{
    SixteenBit,
    ThirtyTwoBit
}
