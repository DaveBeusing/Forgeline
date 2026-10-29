using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeLine.Game;

namespace ForgeLine.MapCompiler;

internal static class GeneratedMapQualificationCommand
{
    private static readonly JsonSerializerOptions Options = new()
    { WriteIndented = true, PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record Matrix(int SchemaVersion, string AssetSha256, SeedCase[] Cases);
    private sealed record SeedCase(string Id, string Seed, int Side, int BaseHeightMillimeters,
        string ExpectedOutcome, Dictionary<string,string> Fingerprints, string? ErrorSha256);
    private sealed record RunEvidence(string Outcome, Dictionary<string,string> Fingerprints, string? Error,
        string? ErrorSha256, double GenerationMilliseconds, long AllocatedBytes, bool SchedulingEqual, string? MatchStateSha256, int SchedulingWorkers, int? AuditErrors, int? AuditWarnings);
    private sealed record CaseReport(string Id, bool Passed, string[] Failures, RunEvidence[] Runs);

    internal static int Run(string[] args)
    {
        if (args is not ["--qualify-generated-maps", string matrixPath, "--output", string output])
            throw new ArgumentException("Use --qualify-generated-maps matrix.json --output new-directory.");
        using var input = File.OpenRead(matrixPath);
        if (input.Length is < 1 or > 65536) throw new InvalidDataException("Seed matrix exceeds its input budget.");
        byte[] bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        if (bytes.Length is < 1 or > 65536) throw new InvalidDataException("Seed matrix exceeds its input budget.");
        var matrix = JsonSerializer.Deserialize<Matrix>(bytes, Options) ?? throw new InvalidDataException("Missing seed matrix.");
        if (matrix.SchemaVersion != 1 || matrix.Cases is not { Length: >= 1 and <= 16 } ||
            matrix.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != matrix.Cases.Length ||
            matrix.AssetSha256 is not { Length: 64 } || matrix.AssetSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Unsupported seed matrix.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Qualification output already exists.");
        Directory.CreateDirectory(output);
        var reports = new List<CaseReport>();
        foreach (var item in matrix.Cases)
        {
            if (string.IsNullOrEmpty(item.Id) || item.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
                item.ExpectedOutcome is not ("qualified" or "rejected"))
                throw new InvalidDataException("Invalid seed matrix case.");
            var original = new GeneratedMapRecipe();
            var recipe = original with { Terrain = original.Terrain with
            {
                BaseHeightMillimeters = item.BaseHeightMillimeters,
                Generation = original.Terrain.Generation with { Seed = ulong.Parse(item.Seed, CultureInfo.InvariantCulture),
                    WidthInChunks = item.Side, HeightInChunks = item.Side }
            }};
            var runs = new List<RunEvidence>(); var failures = new List<string>();
            for (int repetition = 0; repetition < 2; repetition++)
            {
                long allocated = GC.GetTotalAllocatedBytes(true);
                long start = Stopwatch.GetTimestamp();
                GeneratedMapPackage? map = null; string? error = null;
                try { map = GeneratedMapCompiler.Compile(recipe, matrix.AssetSha256); }
                catch (InvalidDataException exception) { error = exception.Message; }
                double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                long allocation = GC.GetTotalAllocatedBytes(true) - allocated;
                if (map is null)
                {
                    string? errorHash = error is null ? null : Hash(Encoding.UTF8.GetBytes(error.Trim().Replace("\r\n","\n",StringComparison.Ordinal)));
                    runs.Add(new("rejected", [], error, errorHash, milliseconds, allocation, false, null, 0, null, null));
                    if (item.ExpectedOutcome != "rejected" || errorHash != item.ErrorSha256) failures.Add("unexpected-rejection");
                    continue;
                }
                var fingerprints = Fingerprints(map);
                if (item.ExpectedOutcome != "qualified" || fingerprints.Count != item.Fingerprints.Count ||
                    fingerprints.Any(p => !item.Fingerprints.TryGetValue(p.Key,out string? expected) || expected != p.Value))
                    failures.Add("golden-fingerprint-mismatch");
                var scheduling = Decoration(map, repetition == 0 ? 1 : 4, repetition != 0);
                bool schedulingEqual = scheduling.Hash == map.Manifest.DecorationSha256;
                if (!schedulingEqual) failures.Add("chunk-scheduling-mismatch");
                var runtime = repetition == 0 ? RoundTrip(map, Path.Combine(output,item.Id,"package"), matrix.AssetSha256) : map;
                string state = Headless(runtime, verifyRecovery: repetition == 0);
                runs.Add(new("qualified",fingerprints,null,null,milliseconds,allocation,schedulingEqual,state,scheduling.Workers,map.Audit.ErrorCount,map.Audit.WarningCount));
            }
            if (runs[0].Outcome != runs[1].Outcome ||
                runs[0].MatchStateSha256 != runs[1].MatchStateSha256 ||
                runs[0].ErrorSha256 != runs[1].ErrorSha256) failures.Add("independent-run-mismatch");
            var report = new CaseReport(item.Id,failures.Count == 0,failures.ToArray(),runs.ToArray());
            reports.Add(report);
            Console.WriteLine($"{item.Id}: {(report.Passed ? "pass" : "FAIL")}; {runs[0].Outcome}; repeats=2.");
        }
        var result = new
        {
            SchemaVersion=1, GeneratorVersion=GeneratedMapRecipe.CurrentVersion,
            MatrixSha256=Hash(bytes), matrix.AssetSha256, Repetitions=2, SchedulingMaximumParallelism=new[]{1,4},
            Framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OS=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            Build=typeof(GeneratedMapCompiler).Assembly.GetName().Version?.ToString(),
            Cases=reports, Passed=reports.All(r=>r.Passed),
            Qualification="Technical generated-package, scheduling and headless evidence; synthetic fixed asset identity, no renderer or art acceptance."
        };
        using (var file = new FileStream(Path.Combine(output,"report.json"),FileMode.CreateNew))
            JsonSerializer.Serialize(file,result,Options);
        return result.Passed ? 0 : 1;
    }

    private static Dictionary<string,string> Fingerprints(GeneratedMapPackage map) => new()
    {
        ["Manifest"]=map.ManifestSha256,["Gameplay"]=map.Manifest.GameplaySha256,["Visual"]=map.Manifest.VisualSha256,
        ["Terrain"]=map.Manifest.Terrain.Sha256,["Geography"]=map.Manifest.Geography.Sha256,
        ["Biome"]=map.Manifest.BiomeSha256,["Decoration"]=map.Manifest.DecorationSha256
    };
    private static (string Hash,int Workers) Decoration(GeneratedMapPackage map,int threads,bool reverse)
    {
        var chunks = map.CreateTerrain().Chunks;
        var workers = new ConcurrentDictionary<int,byte>();
        var hashes = new string[chunks.Count];
        Parallel.For(0,chunks.Count,new ParallelOptions { MaxDegreeOfParallelism=threads },position =>
        {
            workers.TryAdd(Environment.CurrentManagedThreadId,0);
            int index=reverse ? chunks.Count-1-position : position;
            hashes[index]=map.GenerateDecoration(chunks[index].Coordinate).Sha256;
        });
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(string value in hashes) hash.AppendData(Convert.FromHexString(value));
        return (Convert.ToHexStringLower(hash.GetHashAndReset()),workers.Count);
    }
    private static GeneratedMapPackage RoundTrip(GeneratedMapPackage map,string output,string assets)
    {
        map.Write(output);
        var restored=GeneratedMapPackage.Load(output,assets,map.ManifestSha256);
        if (!map.GetManifestBytes().AsSpan().SequenceEqual(restored.GetManifestBytes()) ||
            restored.Audit.ErrorCount!=0 || restored.Audit.WarningCount!=0)
            throw new InvalidDataException("Runtime package roundtrip or geography qualification failed.");
        return restored;
    }
    private static string Headless(GeneratedMapPackage map,bool verifyRecovery)
    {
        MatchComposition Resolve(string key) => key==map.CompositionKey ? map.CreateComposition() : throw new InvalidOperationException("Unknown generated composition.");
        using var runtime=MatchRuntime.Create(CentralDivideScenario.CreateHeadless(MatchScenarioProfile.Validation,2026,composition:map.CreateComposition()));
        runtime.Simulation.RunTicks(20);
        string state=MatchAuthoritativeSnapshot.Capture(runtime).ComputeSha256();
        if(verifyRecovery)
        {
            var save=MatchPersistenceSerializer.DeserializeSave(MatchPersistenceSerializer.SerializeSave(MatchPersistenceService.CaptureSave(runtime)));
            var replay=MatchPersistenceSerializer.DeserializeReplay(MatchPersistenceSerializer.SerializeReplay(MatchPersistenceService.CaptureReplay(runtime)));
            using var restored=MatchPersistenceService.Restore(save,Resolve);
            using var playback=MatchPersistenceService.PlayReplay(replay,Resolve);
            if(state!=MatchAuthoritativeSnapshot.Capture(restored).ComputeSha256() ||
                state!=MatchAuthoritativeSnapshot.Capture(playback).ComputeSha256())
                throw new InvalidDataException("Generated save/replay state mismatch.");
        }
        return state;
    }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
