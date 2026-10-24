namespace ForgeLine.UI;

public readonly record struct NewGameConfiguration(string MapName, string FactionName, ulong Seed);
public sealed record NewGameScenarioOption(string Key, string DisplayName, bool Available, string? Error = null);

public sealed class NewGameModel
{
    public const string PrototypeMapName = "Central Divide";
    public const string DirectorateFactionName = "Directorate";
    public const ulong DefaultSeed = 17;
    private NewGameScenarioOption[] _scenarios;
    private int _selected;

    public NewGameModel(IReadOnlyList<NewGameScenarioOption>? scenarios = null)
    {
        _scenarios = Copy(scenarios ?? [new("central-divide.directorate.v1", PrototypeMapName, true)]);
        Configuration = new(SelectedScenario.DisplayName, DirectorateFactionName, DefaultSeed);
    }

    public NewGameConfiguration Configuration { get; private set; }
    public IReadOnlyList<NewGameScenarioOption> Scenarios => Array.AsReadOnly(_scenarios);
    public NewGameScenarioOption SelectedScenario => _scenarios[_selected];
    public bool CanStartSelectedScenario => SelectedScenario.Available;
    public static bool CanStart => true;

    public void SetSeed(ulong seed) => Configuration = Configuration with { Seed = seed };

    public void RefreshScenarios(IReadOnlyList<NewGameScenarioOption> scenarios)
    {
        string selected = SelectedScenario.Key;
        _scenarios = Copy(scenarios);
        _selected = Math.Max(0, Array.FindIndex(_scenarios, option => option.Key == selected));
        Configuration = Configuration with { MapName = SelectedScenario.DisplayName };
    }

    public void MoveScenario(int direction)
    {
        _selected = Math.Clamp(_selected + Math.Sign(direction), 0, _scenarios.Length - 1);
        Configuration = Configuration with { MapName = SelectedScenario.DisplayName };
    }

    private static NewGameScenarioOption[] Copy(IReadOnlyList<NewGameScenarioOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count is < 1 or > 16 || options.Any(o => o is null || string.IsNullOrWhiteSpace(o.Key) || string.IsNullOrWhiteSpace(o.DisplayName)) ||
            options.Select(o => o.Key).Distinct(StringComparer.Ordinal).Count() != options.Count)
            throw new ArgumentException("Scenario choices must be bounded and have unique keys.", nameof(options));
        return options.ToArray();
    }

    public static GameFrontendAction Start() => GameFrontendAction.StartMatch;
    public static GameFrontendAction Back() => GameFrontendAction.Back;
}
