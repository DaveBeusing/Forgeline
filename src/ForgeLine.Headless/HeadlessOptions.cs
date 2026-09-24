using System.Globalization;

namespace ForgeLine.Headless;

internal readonly record struct HeadlessOptions(
    ulong TickCount,
    ulong Seed,
    int TickRate,
    bool ShowHelp)
{
    public static HeadlessOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        ulong tickCount = 1_000;
        ulong seed = 1;
        int tickRate = ForgeLine.Simulation.FixedTickClock.DefaultTicksPerSecond;
        bool showHelp = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            switch (argument)
            {
                case "--ticks":
                    tickCount = ParseUInt64(args, ref index, argument);
                    break;

                case "--seed":
                    seed = ParseUInt64(args, ref index, argument);
                    break;

                case "--tick-rate":
                    tickRate = ParsePositiveInt32(args, ref index, argument);
                    break;

                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.", nameof(args));
            }
        }

        return new HeadlessOptions(tickCount, seed, tickRate, showHelp);
    }

    private static ulong ParseUInt64(string[] args, ref int index, string option)
    {
        string value = GetValue(args, ref index, option);

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

    private static int ParsePositiveInt32(string[] args, ref int index, string option)
    {
        string value = GetValue(args, ref index, option);

        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed)
            || parsed <= 0)
        {
            throw new ArgumentException(
                $"Option '{option}' requires an integer greater than zero.",
                nameof(args));
        }

        return parsed;
    }

    private static string GetValue(string[] args, ref int index, string option)
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
