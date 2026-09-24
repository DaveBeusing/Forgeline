using System.Runtime.InteropServices;
using System.Text.Json;
using ForgeLine.Simulation;

namespace ForgeLine.Headless;

internal sealed record HeadlessDiagnosticsReport(
    string Runtime,
    string OperatingSystem,
    string ProcessArchitecture,
    int ProcessorCount,
    ulong Seed,
    int TickRate,
    ulong RequestedTicks,
    ulong ExecutedTicks,
    int RequestedEntities,
    double ElapsedMilliseconds,
    double ThroughputTicksPerSecond,
    SimulationLoopMetrics Loop,
    SimulationDiagnosticsSnapshot Diagnostics)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    public static HeadlessDiagnosticsReport Create(
        HeadlessOptions options,
        ulong executedTicks,
        TimeSpan elapsed,
        SimulationCoordinator simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);

        double elapsedSeconds = elapsed.TotalSeconds;
        double throughput = elapsedSeconds > 0d
            ? executedTicks / elapsedSeconds
            : 0d;

        return new HeadlessDiagnosticsReport(
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            options.Seed,
            options.TickRate,
            options.TickCount,
            executedTicks,
            options.EntityCount,
            elapsed.TotalMilliseconds,
            throughput,
            simulation.Metrics,
            simulation.Diagnostics.Capture(simulation));
    }

    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(this, s_jsonOptions);
        File.WriteAllText(fullPath, json);
    }
}
