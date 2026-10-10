using ForgeLine.Graphics;

namespace ForgeLine.Presentation;

internal static class DirectionalShadowCasterPipeline
{
    internal static IGraphicsPipeline Create(IGraphicsDevice graphics, bool instanced)
    {
        string vertex = """
            cbuffer ShadowCaster : register(b0) { row_major float4x4 ViewProjection; };
            struct Input { float3 Position : POSITION;
            """ + (instanced ? "float4 R0 : INSTANCEWORLD0; float4 R1 : INSTANCEWORLD1; float4 R2 : INSTANCEWORLD2; float4 R3 : INSTANCEWORLD3;" : "") + """
            };
            float4 VSMain(Input input) : SV_Position {
                float4 position = float4(input.Position, 1);
            """ + (instanced ? "position = mul(position, float4x4(input.R0, input.R1, input.R2, input.R3));" : "") + "return mul(position, ViewProjection); }";
        var compiler = new DxcShaderCompiler();
        var elements = new List<GraphicsVertexElement> { new("POSITION", 0, GraphicsVertexElementFormat.Float3, 0) };
        if (instanced)
            for (int row = 0; row < 4; row++) elements.Add(new("INSTANCEWORLD", row, GraphicsVertexElementFormat.Float4,
                row * 16, 1, GraphicsVertexInputRate.PerInstance, 1));
        return graphics.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            compiler.Compile(vertex, GraphicsShaderStage.Vertex, "VSMain", "DirectionalCasterVertex.hlsl"),
            compiler.Compile("void PSMain() {}", GraphicsShaderStage.Pixel, "PSMain", "DirectionalCasterPixel.hlsl"))
        {
            TargetFormat = GraphicsFrameTargetFormat.Depth32Float, DepthEnabled = true,
            CullMode = GraphicsCullMode.None, VertexRootConstantCount = 16, VertexElements = elements
        });
    }
}
