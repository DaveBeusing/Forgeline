namespace ForgeLine.Graphics;

public interface IShaderCompiler
{
    GraphicsShaderBytecode Compile(
        string source,
        GraphicsShaderStage stage,
        string entryPoint,
        string sourceName = "<memory>");

    GraphicsShaderBytecode CompileFile(
        string path,
        GraphicsShaderStage stage,
        string entryPoint);
}
