namespace ForgeLine.AssetCompiler;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Any(static argument =>
                argument.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            PrintUsage();
            return 0;
        }

        string sourceRoot = Path.Combine("assets", "source");
        string runtimeRoot = Path.Combine("assets", "runtime");
        var clean = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--source":
                    sourceRoot = ReadValue(args, ref index, argument);
                    break;
                case "--runtime":
                    runtimeRoot = ReadValue(args, ref index, argument);
                    break;
                case "--clean":
                    clean = true;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument '{argument}'.");
                    PrintUsage();
                    return 2;
            }
        }

        var compiler = new AssetPipelineCompiler();
        var result = compiler.Compile(sourceRoot, runtimeRoot, clean);

        foreach (var diagnostic in result.Diagnostics)
        {
            var location = diagnostic.SourcePath is null ? string.Empty : $" [{diagnostic.SourcePath}]";
            var asset = diagnostic.AssetId is null ? string.Empty : $" ({diagnostic.AssetId})";
            Console.WriteLine(
                $"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code}{asset}{location}: {diagnostic.Message}");
        }

        Console.WriteLine(
            $"Asset compilation {(result.Success ? "succeeded" : "failed")}: " +
            $"{result.CompiledCount} compiled, {result.SkippedCount} unchanged.");

        return result.Success ? 0 : 1;
    }

    private static string ReadValue(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Argument '{argument}' requires a value.");
        }

        index++;
        return args[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "ForgeLine.AssetCompiler [--source <assets/source>] [--runtime <assets/runtime>] [--clean]");
    }
}
