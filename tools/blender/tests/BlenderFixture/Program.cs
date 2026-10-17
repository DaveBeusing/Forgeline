using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);
if (args.Contains("--fixture-child")) { Thread.Sleep(15000); return 0; }
string mode = Environment.GetEnvironmentVariable("FORGELINE_BLENDER_FIXTURE_MODE") ?? "ok";
if (args.SequenceEqual(new[] { "--version" }))
{
    Console.WriteLine(mode == "version" ? "Blender 99.0.0" : "Blender 5.2.2 LTS");
    return 0;
}
string[] required = ["--background", "--factory-startup", "--disable-autoexec", "--offline-mode", "--threads", "--python-exit-code", "--python", "--"];
if (required.Any(value => !args.Contains(value)) || args.Contains("--python-use-system-env")) { return 90; }
string script = args[Array.IndexOf(args, "--python") + 1];
if (Path.GetFileName(script) == "probe.py")
{
    if (mode == "bpy") { Console.Error.WriteLine("ModuleNotFoundError: No module named 'bpy'"); return 23; }
    if (mode == "missing-report") { return 0; }
    var probe = new
    {
        schemaVersion = 1, version = mode == "probe-version" ? "5.2.1" : "5.2.2",
        versionCycle = mode == "cycle" ? "alpha" : "release", buildHash = "fixture",
        pythonVersion = "fixture", pointerBits = mode == "arch" ? 32 : 64,
        machine = mode == "machine" ? "ARM64" : "AMD64",
        system = OperatingSystem.IsWindows() ? "Windows" : "Linux",
        background = mode != "foreground", bpy = true
    };
    File.WriteAllText(args[^1], mode == "malformed" ? "{}" : JsonSerializer.Serialize(probe));
    return 0;
}
if (mode == "exit") { Console.Error.WriteLine("fixture script failure Grüße 日本語"); return 17; }
if (mode is "timeout" or "cancel")
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add("--fixture-child");
    using Process child = Process.Start(start)!;
    File.WriteAllText(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_FIXTURE_PIDFILE")!,
        JsonSerializer.Serialize(new { parent = Environment.ProcessId, child = child.Id }));
    if (mode == "cancel")
    {
        Thread.Sleep(250);
        File.WriteAllText(Environment.GetEnvironmentVariable("FORGELINE_BLENDER_FIXTURE_CANCELFILE")!, "cancel");
    }
    Thread.Sleep(15000);
    return 0;
}
int separator = Array.IndexOf(args, "--");
if (args[separator + 1] != "--request" || args.Length != separator + 3) { return 91; }
using JsonDocument request = JsonDocument.Parse(File.ReadAllText(args[^1]));
string output = request.RootElement.GetProperty("outputRoot").GetString()!;
string[] pythonKeys = ["PYTHONHOME", "PYTHONPATH", "PYTHONUSERBASE", "PYTHONSTARTUP", "VIRTUAL_ENV", "CONDA_PREFIX", "PYTHONOPTIMIZE", "PYTHONINSPECT"];
if (pythonKeys.Any(key => Environment.GetEnvironmentVariable(key) is not null)) { return 92; }
File.WriteAllText(Path.Combine(output, "fixture-received.json"), JsonSerializer.Serialize(new
{
    request = request.RootElement, cwd = Environment.CurrentDirectory,
    pythonEnvironmentCleared = true, args
}));
Console.WriteLine("fixture stdout Grüße 日本語");
Console.Error.WriteLine("fixture stderr Grüße 日本語");
return 0;
