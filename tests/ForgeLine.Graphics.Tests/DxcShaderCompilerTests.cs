using ForgeLine.Graphics;

namespace ForgeLine.Graphics.Tests;

public sealed class DxcShaderCompilerTests
{
    [Fact]
    public void CompileProducesVertexShaderBytecode()
    {
        const string source = """
            float4 VSMain(uint vertexId : SV_VertexID) : SV_Position
            {
                return float4((float)vertexId, 0.0f, 0.0f, 1.0f);
            }
            """;

        var compiler = new DxcShaderCompiler();

        GraphicsShaderBytecode bytecode = compiler.Compile(
            source,
            GraphicsShaderStage.Vertex,
            "VSMain",
            "ShaderCompilerTest.hlsl");

        Assert.Equal(GraphicsShaderStage.Vertex, bytecode.Stage);
        Assert.Equal("VSMain", bytecode.EntryPoint);
        Assert.Equal("ShaderCompilerTest.hlsl", bytecode.SourceName);
        Assert.NotEmpty(bytecode.Data.ToArray());
    }

    [Fact]
    public void CompileRejectsInvalidShaderSource()
    {
        const string source = "this is not valid HLSL";
        var compiler = new DxcShaderCompiler();

        GraphicsShaderCompilationException exception = Assert.Throws<GraphicsShaderCompilationException>(
            () => compiler.Compile(
                source,
                GraphicsShaderStage.Pixel,
                "PSMain",
                "InvalidShader.hlsl"));

        Assert.NotEmpty(exception.Message);
    }
}
