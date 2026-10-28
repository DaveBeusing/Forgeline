using System.Globalization;
using ForgeLine.Game;

namespace ForgeLine.Headless;

internal enum HeadlessScenarioKind : byte
{
    Lightweight = 1,
    Match = 2
}

internal readonly record struct HeadlessOptions(
    HeadlessScenarioKind Scenario,
    MatchScenarioProfile Profile,
    ulong TickCount,
    ulong Seed,
    int TickRate,
    int EntityCount,
    int MatchCount,
    bool RequireTerminal,
    string? DiagnosticsOutput,
    string? TelemetryOutput,
    string? TelemetryBaseline,
    string? SaveOutput,
    string? ReplayOutput,
    string? LoadInput,
    string? ReplayInput,
    bool ShowHelp)
{
    public string ScenarioKey { get; init; } = CentralDivideScenario.CompositionKey;
    public string? ScenarioRoot { get; init; }
    public string? DeveloperPresetInput { get; init; }
    public string? ReproductionId { get; init; }
    public string? ReproductionOutput { get; init; }
    public bool ScenarioWasSpecified { get; init; }
    public bool ProfileWasSpecified { get; init; }
    public bool SeedWasSpecified { get; init; }
#if FORGELINE_DEVELOPER_TOOLS
    public DeveloperPresetDocument? DeveloperPreset { get; init; }
#endif

    public static HeadlessOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HeadlessScenarioKind scenario =
            HeadlessScenarioKind.Lightweight;
        MatchScenarioProfile profile =
            MatchScenarioProfile.Gameplay;
        ulong tickCount = 1_000;
        ulong seed = 1;
        int tickRate =
            ForgeLine.Simulation.FixedTickClock.DefaultTicksPerSecond;
        int entityCount = 0;
        int matchCount = 1;
        bool requireTerminal = false;
        string? diagnosticsOutput = null;
        string? telemetryOutput = null;
        string? telemetryBaseline = null;
        string? saveOutput = null;
        string? replayOutput = null;
        string? loadInput = null;
        string? replayInput = null;
        bool showHelp = false;
        string? scenarioRoot = null;
        string scenarioKey = CentralDivideScenario.CompositionKey;
        string? developerPresetInput = null;
        string? reproductionId = null;
        string? reproductionOutput = null;
        bool scenarioWasSpecified = false, profileWasSpecified = false, seedWasSpecified = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            switch (argument)
            {
                case "--scenario":
                    scenarioWasSpecified = true;
                    string selected = GetValue(args, ref index, argument);
                    scenario = ParseScenario(selected);
                    scenarioKey = PracticeLessons.TryGet(selected.ToLowerInvariant(),out _) ? selected.ToLowerInvariant() : selected.ToLowerInvariant() is "split-basin" or SplitBasinBattlefield.CompositionKey
                        ? SplitBasinBattlefield.CompositionKey : CentralDivideScenario.CompositionKey;
                    break;

                case "--scenario-root":
                    scenarioRoot = GetValue(args, ref index, argument);
                    break;

                case "--profile":
                    profileWasSpecified = true;
                    profile =
                        ParseProfile(
                            GetValue(args, ref index, argument));
                    break;

                case "--ticks":
                    tickCount =
                        ParseUInt64(args, ref index, argument);
                    break;

                case "--seed":
                    seedWasSpecified = true;
                    seed =
                        ParseUInt64(args, ref index, argument);
                    break;

                case "--preset-input":
#if FORGELINE_DEVELOPER_TOOLS
                    developerPresetInput = GetValue(args, ref index, argument);
                    scenario = HeadlessScenarioKind.Match;
                    break;
#else
                    throw new ArgumentException("--preset-input requires a Developer build.", nameof(args));
#endif

                case "--reproduction-id":
#if FORGELINE_DEVELOPER_TOOLS
                    reproductionId = GetValue(args, ref index, argument);
                    break;
#else
                    throw new ArgumentException("--reproduction-id requires a Developer build.", nameof(args));
#endif

                case "--reproduction-output":
#if FORGELINE_DEVELOPER_TOOLS
                    reproductionOutput = GetValue(args, ref index, argument);
                    break;
#else
                    throw new ArgumentException("--reproduction-output requires a Developer build.", nameof(args));
#endif

                case "--tick-rate":
                    tickRate =
                        ParsePositiveInt32(
                            args,
                            ref index,
                            argument);
                    break;

                case "--entities":
                    entityCount =
                        ParseNonNegativeInt32(
                            args,
                            ref index,
                            argument);
                    break;

                case "--matches":
                    matchCount =
                        ParsePositiveInt32(
                            args,
                            ref index,
                            argument);
                    break;

                case "--require-terminal":
                    requireTerminal = true;
                    break;

                case "--diagnostics-output":
                    diagnosticsOutput =
                        GetValue(
                            args,
                            ref index,
                            argument);
                    break;

                case "--telemetry-output":
                    telemetryOutput =
                        GetValue(
                            args,
                            ref index,
                            argument);
                    break;

                case "--telemetry-baseline":
                    telemetryBaseline =
                        GetValue(
                            args,
                            ref index,
                            argument);
                    break;

                case "--save-output":
                    saveOutput =
                        GetValue(
                            args,
                            ref index,
                            argument);
                    scenario =
                        HeadlessScenarioKind.Match;
                    break;

                case "--replay-output":
                    replayOutput =
                        GetValue(
                            args,
                            ref index,
                            argument);
                    scenario =
                        HeadlessScenarioKind.Match;
                    break;

                case "--load-input":
                    loadInput =
                        GetValue(
                            args,
                            ref index,
                            argument);
                    scenario =
                        HeadlessScenarioKind.Match;
                    break;

                case "--replay-input":
                    replayInput =
                        GetValue(
                            args,
                            ref index,
                            argument);
                    scenario =
                        HeadlessScenarioKind.Match;
                    break;

                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown argument '{argument}'.",
                        nameof(args));
            }
        }

        if (!showHelp)
        {
            if (scenarioRoot is not null && scenario != HeadlessScenarioKind.Match)
                throw new ArgumentException("--scenario-root requires a registered match scenario.");
            ValidateCombination(
                scenario,
                profile,
                tickRate,
                entityCount,
                matchCount,
                requireTerminal,
                diagnosticsOutput,
                telemetryOutput,
                telemetryBaseline,
                saveOutput,
                replayOutput,
                loadInput,
                replayInput,
                developerPresetInput,
                reproductionId,
                reproductionOutput);
        }

        return new HeadlessOptions(
            scenario,
            profile,
            tickCount,
            seed,
            tickRate,
            entityCount,
            matchCount,
            requireTerminal,
            diagnosticsOutput,
            telemetryOutput,
            telemetryBaseline,
            saveOutput,
            replayOutput,
            loadInput,
            replayInput,
            showHelp) { ScenarioRoot = scenarioRoot, ScenarioKey = scenarioKey,
                DeveloperPresetInput = developerPresetInput, ReproductionId = reproductionId, ReproductionOutput = reproductionOutput,
                ScenarioWasSpecified = scenarioWasSpecified, ProfileWasSpecified = profileWasSpecified, SeedWasSpecified = seedWasSpecified };
    }

    private static void ValidateCombination(
        HeadlessScenarioKind scenario,
        MatchScenarioProfile profile,
        int tickRate,
        int entityCount,
        int matchCount,
        bool requireTerminal,
        string? diagnosticsOutput,
        string? telemetryOutput,
        string? telemetryBaseline,
        string? saveOutput,
        string? replayOutput,
        string? loadInput,
        string? replayInput,
        string? developerPresetInput,
        string? reproductionId,
        string? reproductionOutput)
    {
        if (scenario == HeadlessScenarioKind.Match)
        {
            if (tickRate !=
                ForgeLine.Simulation.FixedTickClock.DefaultTicksPerSecond)
            {
                throw new ArgumentException(
                    "The match scenario uses the canonical 20 Hz simulation rate.",
                    nameof(tickRate));
            }

            if (entityCount != 0)
            {
                throw new ArgumentException(
                    "--entities is only valid for the lightweight scenario.",
                    nameof(entityCount));
            }

            if (telemetryBaseline is not null &&
                telemetryOutput is null)
            {
                throw new ArgumentException(
                    "--telemetry-baseline requires --telemetry-output.",
                    nameof(telemetryBaseline));
            }

            if (loadInput is not null &&
                replayInput is not null)
            {
                throw new ArgumentException(
                    "--load-input and --replay-input are mutually exclusive.");
            }

            if (developerPresetInput is not null && (loadInput is not null || replayInput is not null))
                throw new ArgumentException("--preset-input cannot be combined with save or replay recovery input.");
            if (developerPresetInput is not null && matchCount != 1)
                throw new ArgumentException("A developer preset currently requires --matches 1.");
            if (reproductionId is not null && developerPresetInput is null)
                throw new ArgumentException("--reproduction-id requires --preset-input.");
            if (reproductionOutput is not null && (developerPresetInput is null || reproductionId is null))
                throw new ArgumentException("--reproduction-output requires --preset-input and --reproduction-id.");
            if (reproductionId is not null && !IsReproductionId(reproductionId))
                throw new ArgumentException("--reproduction-id must be 1-64 lowercase letters, digits or hyphens and start with a letter or digit.");

            bool hasRecoveryInput =
                loadInput is not null ||
                replayInput is not null;
            bool hasPersistenceOutput =
                saveOutput is not null ||
                replayOutput is not null;

            if ((hasRecoveryInput ||
                 hasPersistenceOutput) &&
                matchCount != 1)
            {
                throw new ArgumentException(
                    "Save/replay input and output currently require --matches 1.",
                    nameof(matchCount));
            }

            if (hasRecoveryInput &&
                (hasPersistenceOutput ||
                 diagnosticsOutput is not null ||
                 telemetryOutput is not null ||
                 telemetryBaseline is not null))
            {
                throw new ArgumentException(
                    "Recovery input validation cannot be combined with output generation or diagnostics/telemetry options.");
            }

            return;
        }

        if (telemetryOutput is not null ||
            telemetryBaseline is not null ||
            saveOutput is not null ||
            replayOutput is not null ||
            loadInput is not null ||
            replayInput is not null || developerPresetInput is not null || reproductionId is not null || reproductionOutput is not null)
        {
            throw new ArgumentException(
                "Telemetry and save/replay options are only valid for the match scenario.");
        }

        if (profile != MatchScenarioProfile.Gameplay)
        {
            throw new ArgumentException(
                "--profile is only valid for the match scenario.",
                nameof(profile));
        }

        if (matchCount != 1)
        {
            throw new ArgumentException(
                "--matches is only valid for the match scenario.",
                nameof(matchCount));
        }

        if (requireTerminal)
        {
            throw new ArgumentException(
                "--require-terminal is only valid for the match scenario.",
                nameof(requireTerminal));
        }
    }

    private static HeadlessScenarioKind ParseScenario(string value) =>
        value.ToLowerInvariant() switch
        {
            "lightweight" =>
                HeadlessScenarioKind.Lightweight,
            PracticeLessons.ReconKey or PracticeLessons.IndustryKey or PracticeLessons.SupplyKey or
            "central-divide" or "vertical-slice" or CentralDivideScenario.CompositionKey or "split-basin" or SplitBasinBattlefield.CompositionKey =>
                HeadlessScenarioKind.Match,
            _ =>
                throw new ArgumentException(
                    $"Unknown headless scenario '{value}'.")
        };

    private static MatchScenarioProfile ParseProfile(
        string value) =>
        value.ToLowerInvariant() switch
        {
            "gameplay" =>
                MatchScenarioProfile.Gameplay,
            "validation" =>
                MatchScenarioProfile.Validation,
            _ =>
                throw new ArgumentException(
                    $"Unknown match profile '{value}'.")
        };

    private static bool IsReproductionId(string value) => value.Length is > 0 and <= 64 &&
        char.IsAsciiLetterOrDigit(value[0]) && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static ulong ParseUInt64(
        string[] args,
        ref int index,
        string option)
    {
        string value =
            GetValue(
                args,
                ref index,
                option);

        if (!ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out ulong parsed))
        {
            throw new ArgumentException(
                $"Option '{option}' requires an unsigned integer value.",
                nameof(args));
        }

        return parsed;
    }

    private static int ParsePositiveInt32(
        string[] args,
        ref int index,
        string option)
    {
        int parsed =
            ParseInt32(
                args,
                ref index,
                option);

        if (parsed <= 0)
        {
            throw new ArgumentException(
                $"Option '{option}' requires an integer greater than zero.",
                nameof(args));
        }

        return parsed;
    }

    private static int ParseNonNegativeInt32(
        string[] args,
        ref int index,
        string option)
    {
        int parsed =
            ParseInt32(
                args,
                ref index,
                option);

        if (parsed < 0)
        {
            throw new ArgumentException(
                $"Option '{option}' requires a non-negative integer.",
                nameof(args));
        }

        return parsed;
    }

    private static int ParseInt32(
        string[] args,
        ref int index,
        string option)
    {
        string value =
            GetValue(
                args,
                ref index,
                option);

        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            throw new ArgumentException(
                $"Option '{option}' requires an integer value.",
                nameof(args));
        }

        return parsed;
    }

    private static string GetValue(
        string[] args,
        ref int index,
        string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException(
                $"Option '{option}' requires a value.",
                nameof(args));
        }

        index++;
        return args[index];
    }
}
