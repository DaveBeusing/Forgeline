using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

if (args.Length == 1 && args[0] == "--identity")
{
    var assembly = typeof(Program).Assembly;
    string file = assembly.Location;
    using var bytes = File.OpenRead(file);
    Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, kind = "allocation-parser-identity",
        buildIdentity = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        assemblySha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        dependenciesSha256 = Hash(Path.ChangeExtension(file, ".deps.json")),
        runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        scope = "Standalone parser process identity only; not the target renderer or its original clock" }));
    return 0;
}

if (args.Length != 3 || !int.TryParse(args[2], out int expectedPid) || expectedPid <= 0)
{ Console.Error.WriteLine("Usage: AllocationTrace input.nettrace fresh-output-directory expected-process-id"); return 2; }
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) { Console.Error.WriteLine("Output must be fresh; prior attempts are immutable."); return 2; }
Directory.CreateDirectory(output);
string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
var losses = new List<object>();
var rows = new List<object>();
int legacy = 0, modern = 0, ownLegacy = 0, ownModern = 0, wrongPid = 0, missingStack = 0, resolvedEvents = 0, truncatedStacks = 0;
string? before = null, after = null, failure = null;
int? logLost = null, sourceLost = null;
bool hasStacks = false;
DateTime? sessionStartUtc = null, sessionEndUtc = null;
object? clock = null;
long? traceLength = null;
try
{
    var file = new FileInfo(input);
    if (file.Length <= 0 || file.Length > 4L * 1024 * 1024 * 1024) throw new InvalidDataException("Trace input outside declared4GiB bound.");
    before = Hash(input); traceLength = file.Length;
    // Pinned original v4/v6 metadata only; independent producer binding is separate.
    using (var stream = File.OpenRead(input))
    {
        clock = NetTraceClockHeader.Read(stream, expectedPid);
    }
    string etlx = Path.Combine(output, "converted.etlx");
    var options = new TraceLogOptions { OnLostEvents = (truncated, lost, total) => losses.Add(new { truncated, lost, total }) };
    TraceLog.CreateFromEventPipeDataFile(input, etlx, options);
    using var log = new TraceLog(etlx);
    using var source = log.Events.GetSource();
    hasStacks = log.HasCallStacks;
    sessionStartUtc = log.SessionStartTime.ToUniversalTime(); sessionEndUtc = log.SessionEndTime.ToUniversalTime();
    void Record(TraceEvent e, string family, object payload)
    {
        if (e.ProcessID != expectedPid) { wrongPid++; return; }
        if (rows.Count >= 250000) throw new InvalidDataException("Allocation diagnostic event bound exceeded; no sampled tail selection.");
        var frames = new List<object>();
        var stack = log.GetCallStackForEvent(e);
        bool resolved = false, truncated = false;
        while (stack is not null)
        {
            if (frames.Count == 256) { truncated = true; break; }
            string name = stack.CodeAddress.FullMethodName;
            resolved |= !string.IsNullOrEmpty(name);
            frames.Add(new { name, address = stack.CodeAddress.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture), module = stack.CodeAddress.ModuleFilePath });
            stack = stack.Caller;
        }
        if (truncated) truncatedStacks++;
        if (family == "GCAllocationTick") ownLegacy++; else ownModern++;
        if (frames.Count == 0) missingStack++;
        if (resolved) resolvedEvents++;
#pragma warning disable CS0618 // Exact original event timestamp provenance; no inferred offline-parser clock.
        long qpc = e.TimeStampQPC;
#pragma warning restore CS0618
        rows.Add(new { family, eventId = (int)e.ID, version = e.Version, processId = e.ProcessID, nativeThreadId = e.ThreadID,
            timestampUtc = e.TimeStamp.ToUniversalTime(), e.TimeStampRelativeMSec, timestampQpc = qpc,
            payload, stack = frames, stackTruncated = truncated, stackAvailable = frames.Count > 0, resolvedStackAvailable = resolved });
    }
    source.Clr.GCAllocationTick += e => { legacy++; Record(e, "GCAllocationTick", new {
        e.AllocationAmount, allocationAmount64 = e.Version >= 2 ? e.AllocationAmount64 : (long?)null,
        allocationKind = e.AllocationKind.ToString(), clrInstanceId = e.Version >= 1 ? e.ClrInstanceID : (int?)null,
        typeId = e.Version >= 2 ? e.TypeID.ToString("x", System.Globalization.CultureInfo.InvariantCulture) : null, typeName = e.Version >= 2 ? e.TypeName : null,
        heapIndex = e.Version >= 2 ? e.HeapIndex : (int?)null, address = e.Version >= 3 ? e.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture) : null,
        objectSize = e.Version >= 4 ? e.ObjectSize : (long?)null }); };
    source.Clr.AllocationSampling += e => { modern++; Record(e, "AllocationSampling", new {
        allocationKind = e.AllocationKind.ToString(), e.ClrInstanceID, typeId = e.TypeID.ToString("x", System.Globalization.CultureInfo.InvariantCulture), e.TypeName,
        address = e.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture), e.ObjectSize, e.SampledByteOffset }); };
    source.Process();
    logLost = log.EventsLost; sourceLost = source.EventsLost;
    after = Hash(input);
    if (before != after) throw new InvalidDataException("Original trace changed during conversion/parse.");
    if (rows.Count == 0 || resolvedEvents == 0 || truncatedStacks != 0 || !hasStacks || logLost != 0 || sourceLost != 0 || losses.Count != 0)
        throw new InvalidDataException("Diagnostic attribution incomplete: missing own-process events/resolved stacks or reported loss/conversion notices.");
}
catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
{ failure = error.ToString(); }
var result = new { schemaVersion = 1, kind = "sampled-allocation-stack-diagnostic", status = failure is null ? "diagnostic-pass" : "unavailable",
    qualified = false, expectedPid, traceLength, traceSha256Before = before, traceSha256After = after,
    parser = "TraceEvent4.0.0", parserBuildIdentity = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    parserAssemblySha256 = Hash(typeof(Program).Assembly.Location), clock, sessionStartUtc, sessionEndUtc, hasStacks, logEventsLost = logLost, sourceEventsLost = sourceLost,
    conversionNotices = losses, traceWideLegacyEventsObserved = legacy, traceWideModernEventsObserved = modern, ownProcessLegacyEvents = ownLegacy, ownProcessModernEvents = ownModern, truncatedStacks, foreignProcessEvents = wrongPid,
    missingStackEvents = missingStack, resolvedStackEvents = resolvedEvents, events = rows, failure,
    semantics = "Sampled families retained separately; never summed into exact allocation bytes or substituted for normal timing/full-frame precise process/thread totals" };
using (var file = new FileStream(Path.Combine(output, "allocation-stacks.json"), FileMode.CreateNew))
    JsonSerializer.Serialize(file, result, new JsonSerializerOptions { WriteIndented = true });
return failure is null ? 0 : 2;
