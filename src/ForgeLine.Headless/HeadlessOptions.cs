using System.Globalization;
using ForgeLine.Game;

namespace ForgeLine.Headless;

internal enum HeadlessScenarioKind : byte
{
    Lightweight = 1,
    VerticalSlice = 2
}

internal readonly record struct HeadlessOptions(
    HeadlessScenarioKind Scenario,
    VerticalSliceScenarioProfile Profile,
    ulong TickCount,
    ulong Seed,
    int TickRate,
    int EntityCount,
    int MatchCount,
    bool RequireTerminal,
    string? DiagnosticsOutput,
    bool ShowHelp)
{
    public static HeadlessOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HeadlessScenarioKind scenario =
            HeadlessScenarioKind.Lightweight;
        VerticalSliceScenarioProfile profile =
            VerticalSliceScenarioProfile.Gameplay;
        ulong tickCount = 1_000;
        ulong seed = 1;
        int tickRate =
            ForgeLine.Simulation.FixedTickClock.DefaultTicksPerSecond;
        int entityCount = 0;
        int matchCount = 1;
        bool requireTerminal = false;
        string? diagnosticsOutput = null;
        bool showHelp = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            switch (argument)
            {
                case "--scenario":
                    scenario =
                        ParseScenario(
                            GetValue(args, ref index, argument));
                    break;

                case "--profile":
                    profile =
                        ParseProfile(
                            GetValue(args, ref index, argument));
                    break;

                case "--ticks":
                    tickCount =
                        ParseUInt64(args, ref index, argument);
                    break;

                case "--seed":
                    seed =
                        ParseUInt64(args, ref index, argument);
                    break;

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

        ValidateCombination(
            scenario,
            profile,
            tickRate,
            entityCount,
            matchCount,
            requireTerminal);

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
            showHelp);
    }

    private static void ValidateCombination(
        HeadlessScenarioKind scenario,
        VerticalSliceScenarioProfile profile,
        int tickRate,
        int entityCount,
        int matchCount,
        bool requireTerminal)
    {
        if (scenario == HeadlessScenarioKind.VerticalSlice)
        {
            if (tickRate !=
                ForgeLine.Simulation.FixedTickClock.DefaultTicksPerSecond)
            {
                throw new ArgumentException(
                    "The vertical-slice scenario uses the canonical 20 Hz simulation rate.",
                    nameof(tickRate));
            }

            if (entityCount != 0)
            {
                throw new ArgumentException(
                    "--entities is only valid for the lightweight scenario.",
                    nameof(entityCount));
            }

            return;
        }

        if (profile != VerticalSliceScenarioProfile.Gameplay)
        {
            throw new ArgumentException(
                "--profile is only valid for the vertical-slice scenario.",
                nameof(profile));
        }

        if (matchCount != 1)
        {
            throw new ArgumentException(
                "--matches is only valid for the vertical-slice scenario.",
                nameof(matchCount));
        }

        if (requireTerminal)
        {
            throw new ArgumentException(
                "--require-terminal is only valid for the vertical-slice scenario.",
                nameof(requireTerminal));
        }
    }

    private static HeadlessScenarioKind ParseScenario(string value) =>
        value.ToLowerInvariant() switch
        {
            "lightweight" =>
                HeadlessScenarioKind.Lightweight,
            "vertical-slice" =>
                HeadlessScenarioKind.VerticalSlice,
            _ =>
                throw new ArgumentException(
                    $"Unknown headless scenario '{value}'.")
        };

    private static VerticalSliceScenarioProfile ParseProfile(
        string value) =>
        value.ToLowerInvariant() switch
        {
            "gameplay" =>
                VerticalSliceScenarioProfile.Gameplay,
            "validation" =>
                VerticalSliceScenarioProfile.Validation,
            _ =>
                throw new ArgumentException(
                    $"Unknown vertical-slice profile '{value}'.")
        };

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
