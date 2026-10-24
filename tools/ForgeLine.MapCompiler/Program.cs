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

            WriteBytes(
                options.OutputPath,
                payload);
            File.WriteAllText(options.OutputPath + ".sha256", Convert.ToHexStringLower(SHA256.HashData(payload)) + "\n");
            WriteQualification(
                options.QualificationOutputPath,
                qualification);

            Console.WriteLine(
                $"Compiled map '{definition.Metadata.DisplayName}' ({definition.Metadata.Key}) to '{options.OutputPath}'.");
            Console.WriteLine(
                $"Operational geography: reachability={qualification.ReachabilityChecks}; " +
                $"alternate-navigation={qualification.AlternateNavigationChecks}; " +
                $"alternate-road={qualification.AlternateRoadChecks}; " +
                $"elevation-range={qualification.TerrainElevationRangeMeters:F1}m.");

            return 0;
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
        BattlefieldOperationalGeographyReport report)
    {
        string? directory =
            Path.GetDirectoryName(
                Path.GetFullPath(path));

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                report,
                QualificationSerializerOptions));
    }

    private sealed record CompilerOptions(
        string OutputPath,
        string QualificationOutputPath, string Scenario)
    {
        public static CompilerOptions Parse(
            string[] args)
        {
            string scenario = CentralDivideScenario.CompositionKey;
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
                qualification, scenario);
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
