using System.Text.Json;
using ForgeLine.Assets;

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
        string? qualificationOutput = null;
        string? progressLog = null;
        var clean = false;

        try
        {
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
                    case "--qualification-output":
                        qualificationOutput = ReadValue(args, ref index, argument);
                        break;
                    case "--clean":
                        clean = true;
                        break;
                    case "--progress-log":
                        progressLog = ReadValue(args, ref index, argument);
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument '{argument}'.");
                        PrintUsage();
                        return 2;
                }
            }
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 2;
        }

        using var log = new AssetCompilationLog(progressLog);
        using var reporter = new AssetCompilationReporter(log.Write);
        var result = AssetPipelineCompiler.Compile(sourceRoot, runtimeRoot, clean, log.Write);

        foreach (var diagnostic in result.Diagnostics)
        {
            reporter.Report("diagnostic", diagnostic.Severity.ToString().ToLowerInvariant(),
                id: diagnostic.AssetId, path: diagnostic.SourcePath,
                detail: $"{diagnostic.Code}: {diagnostic.Message}");
            var location = diagnostic.SourcePath is null ? string.Empty : $" [{diagnostic.SourcePath}]";
            var asset = diagnostic.AssetId is null ? string.Empty : $" ({diagnostic.AssetId})";
            Console.WriteLine(
                $"{diagnostic.Severity.ToString().ToUpperInvariant()} {diagnostic.Code}{asset}{location}: {diagnostic.Message}");
        }

        Console.WriteLine(
            $"Asset compilation {(result.Success ? "succeeded" : "failed")}: " +
            $"{result.CompiledCount} compiled, {result.SkippedCount} unchanged.");

        if (!result.Success)
        {
            return 1;
        }

        RuntimeAssetQualificationReport qualification = reporter.Measure("qualification",
            () => RuntimeAssetQualification.Run(runtimeRoot));

        foreach (RuntimeAssetQualificationIssue issue in
                 qualification.Issues)
        {
            reporter.Report("qualification-diagnostic", issue.Severity.ToString().ToLowerInvariant(),
                id: issue.AssetId, path: issue.RuntimePath, detail: $"{issue.Code}: {issue.Message}");
            string runtimePath =
                issue.RuntimePath is null
                    ? string.Empty
                    : $" [{issue.RuntimePath}]";
            string asset =
                issue.AssetId is null
                    ? string.Empty
                    : $" ({issue.AssetId})";

            Console.WriteLine(
                $"{issue.Severity.ToString().ToUpperInvariant()} {issue.Code}{asset}{runtimePath}: {issue.Message}");
        }

        Console.WriteLine(
            $"Runtime asset qualification {(qualification.Success ? "succeeded" : "failed")}: " +
            $"assets={qualification.AssetCount} " +
            $"meshes={qualification.MeshCount} " +
            $"textures={qualification.TextureCount} " +
            $"materials={qualification.MaterialCount} " +
            $"bytes={qualification.TotalRuntimeBytes} " +
            $"catalogLoadMs={qualification.CatalogLoadDuration.TotalMilliseconds:F3} " +
            $"assetReadMs={qualification.AssetReadDuration.TotalMilliseconds:F3}.");

        foreach (RuntimeAssetFootprintEntry entry in
                 qualification.LargestAssets)
        {
            Console.WriteLine(
                $"[asset:footprint] id={entry.AssetId} type={entry.Type} bytes={entry.RuntimeBytes}");
        }

        if (!string.IsNullOrWhiteSpace(
                qualificationOutput))
        {
            WriteQualificationReport(
                qualificationOutput,
                qualification);
        }

        return qualification.Success
            ? 0
            : 1;
    }

    private static void WriteQualificationReport(
        string outputPath,
        RuntimeAssetQualificationReport qualification)
    {
        string fullPath =
            Path.GetFullPath(
                outputPath);
        string? directory =
            Path.GetDirectoryName(
                fullPath);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.WriteAllText(
            fullPath,
            JsonSerializer.Serialize(
                qualification,
                RuntimeAssetCatalog.CreateJsonOptions()));

        Console.WriteLine(
            $"Runtime asset qualification report written to '{fullPath}'.");
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
            "ForgeLine.AssetCompiler [--source <assets/source>] [--runtime <assets/runtime>] " +
            "[--clean] [--qualification-output <report.json>] [--progress-log <progress.jsonl>]");
    }
}
