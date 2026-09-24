using ForgeLine.Platform.Windows;

namespace ForgeLine.Client;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!TryParseArguments(args, out bool smokeTest))
        {
            return 2;
        }

        try
        {
            using var platform = new WindowsPlatform();
            var application = new ClientApplication(platform);
            return application.Run(smokeTest);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[platform:error] type={exception.GetType().Name} message={exception.Message}");
            return 1;
        }
    }

    private static bool TryParseArguments(string[] args, out bool smokeTest)
    {
        smokeTest = false;

        foreach (string argument in args)
        {
            if (string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                smokeTest = true;
                continue;
            }

            if (string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("ForgeLine.Client [--smoke-test]");
                return false;
            }

            Console.Error.WriteLine($"Unknown argument: {argument}");
            return false;
        }

        return true;
    }
}
