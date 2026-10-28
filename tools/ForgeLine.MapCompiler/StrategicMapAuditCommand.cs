using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ForgeLine.Game;
using ForgeLine.World;

namespace ForgeLine.MapCompiler;

internal static class StrategicMapAuditCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    internal sealed record MapEvidence(string Scenario, string MapSha256, string TerrainSha256,
        string ContentSha256, int TerrainRecipeVersion, string GeometryRandomness, StrategicMapAuditReport Audit);

    internal static int Run(string[] args)
    {
        string output = "artifacts/strategic-map-audit.json";
        string? previews = null, exceptions = null;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length && !string.IsNullOrWhiteSpace(args[i]) && !args[i].StartsWith("--", StringComparison.Ordinal)
                ? args[i] : throw new ArgumentException("Audit option requires a value.");
            switch (args[i])
            {
                case "--audit-all": break;
                case "--qualification-output": output = Value(); break;
                case "--preview-directory": previews = Value(); break;
                case "--exceptions": exceptions = Value(); break;
                default: throw new ArgumentException($"Unknown strategic audit argument '{args[i]}'.");
            }
        }
        Dictionary<string, Dictionary<string, string>> accepted = ReadExceptions(exceptions);
        var reports = new List<MapEvidence>();
        var registry = ScenarioRegistry.Default;
        foreach (var descriptor in registry.Descriptors.OrderBy(d => d.Key, StringComparer.Ordinal))
        {
            // Resolve first so digest, sidecar, content and registered rules fail closed.
            var composition = registry.Resolve(descriptor.Key);
            var terrain = composition.CreateTerrain(composition.Battlefield);
            accepted.TryGetValue(descriptor.Identity.MapKey, out var warnings);
            var audit = StrategicMapAudit.Analyze(composition.Battlefield, terrain, acceptedWarnings: warnings);
            reports.Add(Evidence(descriptor.Key, descriptor.Identity.MapSha256, descriptor.Identity.ContentSha256,
                descriptor.Identity.TerrainVersion, terrain, audit));
            if (previews is not null)
                BattlefieldMapPreview.Write(Path.Combine(previews, descriptor.Identity.MapKey + ".svg"), composition.Battlefield, terrain, audit);
        }
        if (accepted.Keys.Any(key => reports.All(r => r.Audit.MapKey != key)))
            throw new ArgumentException("Exception file names an unregistered map.");
        Write(output, JsonSerializer.Serialize(reports, JsonOptions));
        Write(Path.ChangeExtension(output, ".md"), Markdown(reports));
        foreach (var report in reports)
            Console.WriteLine($"{report.Audit.MapKey}: {report.Audit.Verdict}; errors={report.Audit.ErrorCount}, warnings={report.Audit.WarningCount}, accepted={report.Audit.AcceptedWarningCount}, queries={report.Audit.RouteQueryCount}");
        return reports.Any(r => r.Audit.ErrorCount > 0) ? 1 : 0;
    }

    internal static MapEvidence Evidence(string scenario, string mapDigest, string contentDigest,
        int terrainVersion, TerrainWorld terrain, StrategicMapAuditReport audit)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var chunk in terrain.Chunks)
        for (int z = 0; z < chunk.Heightfield.SamplesPerSide; z++)
        for (int x = 0; x < chunk.Heightfield.SamplesPerSide; x++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes, chunk.Heightfield.GetHeight(x, z));
            hash.AppendData(bytes);
        }
        return new(scenario, mapDigest, Convert.ToHexStringLower(hash.GetHashAndReset()), contentDigest,
            terrainVersion, "Authored deterministic terrain recipe; no RNG or match seed is consumed.", audit);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadExceptions(string? path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (path is null) return result;
        using var stream = File.OpenRead(path);
        if (stream.Length > 65_536) throw new ArgumentException("Exception file exceeds 64 KiB.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
        foreach (var map in document.RootElement.EnumerateObject())
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in map.Value.EnumerateObject())
                if (!entries.TryAdd(entry.Name, entry.Value.GetString() ?? string.Empty))
                    throw new ArgumentException("Duplicate warning exception.");
            if (!result.TryAdd(map.Name, entries)) throw new ArgumentException("Duplicate map exception.");
        }
        return result;
    }

    internal static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text);
    }

    internal static string Markdown(IEnumerable<MapEvidence> reports)
    {
        var text = new StringBuilder("# Strategic map audit\n\n");
        text.AppendLine("Offline geometry diagnostics; no win-rate, travel-time, physical vehicle or complete fairness guarantee. Null distances mean no measured route. Warning exceptions retain the original diagnostic.");
        foreach (var report in reports)
        {
            var audit = report.Audit;
            text.AppendLine();
            text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"## {audit.MapKey} revision {audit.MapRevision}: {audit.Verdict}");
            text.AppendLine();
            text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Map SHA-256: {report.MapSha256}; terrain SHA-256: {report.TerrainSha256}; content SHA-256: {report.ContentSha256}.");
            text.AppendLine(report.GeometryRandomness);
            text.AppendLine(System.FormattableString.Invariant($"Grid {audit.NavigationCellMeters} m / sectors {audit.SectorSizeCells} cells; local opening <= {audit.Rules.LocalOpeningDistanceMeters} m; asymmetry warning ratio > {audit.Rules.AsymmetryWarningRatio}."));
            text.AppendLine();
            text.AppendLine("| Movement | Player | Resource | Local deposit | Route m | Quantity | Extraction/s |");
            text.AppendLine("|---|---:|---:|---|---:|---:|---:|");
            foreach (var opening in audit.Openings)
                text.AppendLine(System.FormattableString.Invariant($"| {opening.Movement} | {opening.Player} | {opening.Resource} | {opening.Deposit ?? "unavailable"} | {opening.AccessibleDistanceMeters?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "unreachable"} | {opening.Quantity:0.###} | {opening.ExtractionPerSecond:0.###} |"));
            text.AppendLine();
            text.AppendLine("| Movement | Player | Reachable permitted yard samples m² |");
            text.AppendLine("|---|---:|---:|");
            foreach (var area in audit.BuildAreas)
                text.AppendLine(System.FormattableString.Invariant($"| {area.Movement} | {area.Player} | {area.ReachablePermittedSquareMeters:0} |"));
            text.AppendLine();
            text.AppendLine("| Crossing state | From | To | Widest road-path capacity/s |");
            text.AppendLine("|---|---:|---:|---:|");
            foreach (var capacity in audit.RoadCapacities)
                text.AppendLine(System.FormattableString.Invariant($"| {capacity.State} | {capacity.FromPlayer} | {capacity.ToPlayer} | {capacity.WidestPathCapacityPerSecond:0.###} |"));
            text.AppendLine();
            foreach (var diagnostic in audit.Diagnostics)
                text.AppendLine($"- {diagnostic.Severity} [{diagnostic.Id}]: {diagnostic.Message}" +
                    (diagnostic.AcceptedReason is null ? string.Empty : $" Accepted: {diagnostic.AcceptedReason}"));
            text.AppendLine();
            text.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"Errors: {audit.ErrorCount}; review warnings: {audit.WarningCount}; accepted warnings: {audit.AcceptedWarningCount}. Complete route matrix is in the accompanying JSON.");
        }
        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
