namespace ForgeLine.UI;

public readonly record struct NewGameConfiguration(
    string MapName,
    string FactionName,
    ulong Seed);

public sealed class NewGameModel
{
    public const string PrototypeMapName = "Central Divide";
    public const string DirectorateFactionName = "Directorate";
    public const ulong DefaultSeed = 17;

    public NewGameModel()
    {
        Configuration = new NewGameConfiguration(
            PrototypeMapName,
            DirectorateFactionName,
            DefaultSeed);
    }

    public NewGameConfiguration Configuration { get; private set; }

    public bool CanStart => true;

    public void SetSeed(ulong seed)
    {
        Configuration = Configuration with
        {
            Seed = seed
        };
    }

    public GameFrontendAction Start() =>
        GameFrontendAction.StartMatch;

    public GameFrontendAction Back() =>
        GameFrontendAction.Back;
}
