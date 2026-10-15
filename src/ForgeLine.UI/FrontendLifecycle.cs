namespace ForgeLine.UI;

public readonly record struct CreditsSection(
    string Heading,
    IReadOnlyList<string> Lines);

public sealed class CreditsModel
{
    public const string Studio = "Undefined Behavior Studios";
    public const string LeadDeveloper = "Dave Beusing";
    public const string Engine = "ForgeLine Engine";
    private static readonly CreditsSection[] s_sections =
    [
        new("STUDIO", [Studio]),
        new("LEAD DEVELOPER", [LeadDeveloper]),
        new("ENGINE", [Engine])
    ];

    public static string ProductName =>
        FrontendLoadingController.ProductName;

    public static string Tagline =>
        FrontendLoadingController.Tagline;

    public static IReadOnlyList<CreditsSection> Sections =>
        s_sections;

    public static GameFrontendAction Back() =>
        GameFrontendAction.Back;
}

public readonly record struct FrontendExitRequest(
    bool Requested,
    int ExitCode)
{
    public static FrontendExitRequest None =>
        new(false, 0);

    public static FrontendExitRequest Success =>
        new(true, 0);
}

public sealed class FrontendLifecycle
{
    public FrontendExitRequest ExitRequest { get; private set; } =
        FrontendExitRequest.None;

    public void Synchronize(
        GameFrontendShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (shell.IsExitRequested)
        {
            ExitRequest =
                FrontendExitRequest.Success;
        }
    }
}
