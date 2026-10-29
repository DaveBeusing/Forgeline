using System.Text.Json;
using System.Security.Cryptography;
using ForgeLine.Game;
using ForgeLine.Navigation;
using ForgeLine.World;

namespace ForgeLine.MapCompiler;

internal static class Program
{
    private static readonly JsonSerializerOptions QualificationSerializerOptions =
        new()
        {
            WriteIndented = true
        };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--generate-map", StringComparer.Ordinal) || args.Contains("--load-generated-map", StringComparer.Ordinal)) return GeneratedMapCommand.Run(args);
            if (args.Contains("--scatter-diagnostics", StringComparer.Ordinal)) return EnvironmentScatterCommand.Run(args);
            if (args.Contains("--generate-terrain", StringComparer.Ordinal)) return ProceduralTerrainCommand.Run(args);
            if (args.Contains("--audit-all", StringComparer.Ordinal)) return StrategicMapAuditCommand.Run(args);

            CompilerOptions options =
                CompilerOptions.Parse(args);

            bool splitBasin = options.Scenario == SplitBasinBattlefield.CompositionKey;
            BattlefieldDefinition authored = splitBasin ? SplitBasinBattlefield.Create() : CentralDivideBattlefield.Create();
            BattlefieldMapArtifact artifact = BattlefieldMapArtifact.Capture(authored,
                splitBasin ? SplitBasinBattlefield.CreateTerrainRecipe() : BattlefieldTerrainRecipe.CentralDivide());
            byte[] payload = artifact.Serialize();
            BattlefieldMapArtifact loaded = BattlefieldMapArtifact.Deserialize(payload);
            BattlefieldDefinition definition = loaded.ToDefinition();
            loaded.ValidateMatches(authored);
            string expected = splitBasin ? ScenarioRegistry.SplitBasinMapSha256 : ScenarioRegistry.CentralDivideMapSha256;
            string digest = Convert.ToHexStringLower(SHA256.HashData(payload));
            if (digest != expected)
                throw new InvalidOperationException($"Compiled map digest {digest} differs from the registered runtime revision.");
            TerrainWorld terrain = BattlefieldTerrainFactory.Create(definition, loaded.Terrain);

            var gridSettings =
                new NavigationGridSettings
                {
                    CellSizeMeters = 16.0f,
                    StaticObstacleClearanceMeters = 0.5f
                };
            var sectorSettings =
                new NavigationSectorSettings
                {
                    SectorSizeCells = 8
                };

            BattlefieldOperationalGeographyReport qualification =
                BattlefieldOperationalGeographyValidator.Validate(
                    definition,
                    terrain,
                    gridSettings,
                    sectorSettings);

            StrategicMapAuditReport balance = StrategicMapAudit.Analyze(definition, terrain);

            WriteBytes(
                options.OutputPath,
                payload);
            File.WriteAllText(options.OutputPath + ".sha256", Convert.ToHexStringLower(SHA256.HashData(payload)) + "\n");


            if (options.PreviewOutputPath is not null) BattlefieldMapPreview.Write(options.PreviewOutputPath, definition, terrain, balance);
            using var terrainHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var sample = new byte[4];
            foreach (var chunk in terrain.Chunks)
                for (int z = 0; z < chunk.Heightfield.SamplesPerSide; z++)
                    for (int x = 0; x < chunk.Heightfield.SamplesPerSide; x++)
                    {
                        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(sample, chunk.Heightfield.GetHeight(x, z));
                        terrainHash.AppendData(sample);
                    }
            string terrainDigest = Convert.ToHexStringLower(terrainHash.GetHashAndReset());
            WriteQualification(options.QualificationOutputPath, qualification, definition, terrain, digest, terrainDigest, balance);
            Console.WriteLine($"Map SHA-256: {digest}; terrain SHA-256: {terrainDigest}.");
            Console.WriteLine(
                $"Compiled map '{definition.Metadata.DisplayName}' ({definition.Metadata.Key}) to '{options.OutputPath}'.");
            Console.WriteLine(
                $"Operational geography: reachability={qualification.ReachabilityChecks}; " +
                $"alternate-navigation={qualification.AlternateNavigationChecks}; " +
                $"alternate-road={qualification.AlternateRoadChecks}; " +
                $"elevation-range={qualification.TerrainElevationRangeMeters:F1}m.");

            Console.WriteLine($"Strategic audit: {balance.Verdict}; errors={balance.ErrorCount}, warnings={balance.WarningCount}.");
            return balance.ErrorCount > 0 ? 1 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                exception.Message);
            return 1;
        }
    }

    private static void WriteBytes(
        string path,
        byte[] payload)
    {
        string? directory =
            Path.GetDirectoryName(
                Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.WriteAllBytes(
            path,
            payload);
    }

    private static void WriteQualification(
        string path,
        BattlefieldOperationalGeographyReport report, BattlefieldDefinition map, TerrainWorld terrain, string mapDigest, string terrainDigest, StrategicMapAuditReport balance)
    {
        string? directory =
            Path.GetDirectoryName(
                Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        var document = JsonSerializer.SerializeToNode(report, QualificationSerializerOptions)!;
        document["MapSha256"] = mapDigest;
        document["TerrainSha256"] = terrainDigest;
        var routes = new List<object>();
        foreach (string? disabled in new string?[] { null }.Concat(map.Crossings.Select(x => (string?)x.Key)))
        {
            var world = NavigationWorld.Build(terrain, map.CreateNavigationObstacles(disabled is null ? null :
                new Dictionary<string, bool> { [disabled] = false }),
                new NavigationGridSettings { CellSizeMeters = 16, StaticObstacleClearanceMeters = .5f },
                new NavigationSectorSettings { SectorSizeCells = 8 });
            var finder = new HierarchicalPathfinder(world);
            foreach (var movement in new[] { NavigationMovementClass.Infantry, NavigationMovementClass.Wheeled, NavigationMovementClass.Tracked })
            foreach (bool reverse in new[] { false, true })
            {
                var route = finder.FindPath(map.Starts[reverse ? 1 : 0].Position, map.Starts[reverse ? 0 : 1].Position, NavigationCapabilities.For(movement));
                if (!route.Succeeded || route.Path is null) throw new InvalidOperationException("Measured route is unreachable.");
                routes.Add(new { Movement = movement.ToString(), DisabledCrossing = disabled, Reverse = reverse, route.Path.Diagnostics.RouteLengthMeters });
            }
        }
        document["Routes"] = JsonSerializer.SerializeToNode(routes);
        document["StrategicBalance"] = JsonSerializer.SerializeToNode(balance, QualificationSerializerOptions);
        var evidence = StrategicMapAuditCommand.Evidence(map.Metadata.Key, mapDigest, ScenarioRegistry.CentralDivideContentSha256, BattlefieldTerrainRecipe.CurrentVersion, terrain, balance);
        StrategicMapAuditCommand.Write(Path.ChangeExtension(path, ".md"), StrategicMapAuditCommand.Markdown([evidence]));
        File.WriteAllText(path, document.ToJsonString(QualificationSerializerOptions));
    }

    private sealed record CompilerOptions(
        string OutputPath,
        string QualificationOutputPath, string Scenario, string? PreviewOutputPath)
    {
        public static CompilerOptions Parse(
            string[] args)
        {
            string scenario = CentralDivideScenario.CompositionKey;
            string? preview = null;
            string output =
                Path.Combine(
                    "artifacts",
                    "maps",
                    "central-divide.flmap.json");
            string qualification =
                Path.Combine(
                    "artifacts",
                    "map-qualification.json");

            for (int index = 0;
                 index < args.Length;
                 index++)
            {
                string argument =
                    args[index];

                switch (argument)
                {
                    case "--preview-output":
                        preview = ReadValue(args, ref index, argument);
                        break;
                    case "--scenario":
                        scenario = ReadValue(args, ref index, argument) switch
                        {
                            "central-divide" or CentralDivideScenario.CompositionKey => CentralDivideScenario.CompositionKey,
                            "split-basin" or SplitBasinBattlefield.CompositionKey => SplitBasinBattlefield.CompositionKey,
                            _ => throw new ArgumentException("Unknown registered authoring scenario.")
                        };
                        break;
                    case "--output":
                        output =
                            ReadValue(
                                args,
                                ref index,
                                argument);
                        break;

                    case "--qualification-output":
                        qualification =
                            ReadValue(
                                args,
                                ref index,
                                argument);
                        break;

                    default:
                        throw new ArgumentException(
                            $"Unknown map compiler argument '{argument}'.");
                }
            }

            return new CompilerOptions(
                output,
                qualification, scenario, preview);
        }

        private static string ReadValue(
            string[] args,
            ref int index,
            string argument)
        {
            int valueIndex =
                index + 1;

            if (valueIndex >= args.Length ||
                string.IsNullOrWhiteSpace(
                    args[valueIndex]))
            {
                throw new ArgumentException(
                    $"Argument '{argument}' requires a value.");
            }

            index =
                valueIndex;
            return args[valueIndex];
        }
    }
}
