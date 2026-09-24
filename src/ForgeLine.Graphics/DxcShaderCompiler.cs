using Vortice.Dxc;

namespace ForgeLine.Graphics;

public sealed class DxcShaderCompiler : IShaderCompiler
{
    public GraphicsShaderBytecode Compile(
        string source,
        GraphicsShaderStage stage,
        string entryPoint,
        string sourceName = "<memory>")
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Shader source must not be empty.", nameof(source));
        }

        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            throw new ArgumentException("Shader entry point must not be empty.", nameof(entryPoint));
        }

        string profile = stage switch
        {
            GraphicsShaderStage.Vertex => "vs_6_0",
            GraphicsShaderStage.Pixel => "ps_6_0",
            GraphicsShaderStage.Compute => "cs_6_0",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unsupported shader stage.")
        };

        string[] arguments =
        [
            sourceName,
            "-E",
            entryPoint,
            "-T",
            profile,
            "-HV",
            "2021",
            "-Ges",
            "-WX",
#if DEBUG
            "-Od",
#else
            "-O3",
#endif
        ];

        using IDxcResult result = DxcCompiler.Compile(source, arguments);
        if (result.GetStatus().Failure)
        {
            string errors = result.GetErrors();
            throw new GraphicsShaderCompilationException(
                string.IsNullOrWhiteSpace(errors)
                    ? $"DXC failed to compile {sourceName} ({profile})."
                    : errors.Trim());
        }

        return new GraphicsShaderBytecode(
            stage,
            entryPoint,
            sourceName,
            result.GetObjectBytecodeArray());
    }

    public GraphicsShaderBytecode CompileFile(
        string path,
        GraphicsShaderStage stage,
        string entryPoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string source = File.ReadAllText(fullPath);
        return Compile(source, stage, entryPoint, fullPath);
    }
}
