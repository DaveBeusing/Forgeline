namespace ForgeLine.UI;

public readonly record struct NewGameConfiguration(string MapName, string FactionName, ulong Seed);
public sealed record NewGameScenarioOption(string Key, string DisplayName, bool Available, string? Error = null, string Description = "");

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
        FocusedRow = SeedRow;
    }

    public NewGameConfiguration Configuration { get; private set; }
    public IReadOnlyList<NewGameScenarioOption> Scenarios => Array.AsReadOnly(_scenarios);
    public NewGameScenarioOption SelectedScenario => _scenarios[_selected];
    public bool SeedSelectAll { get; set; } = true;
    public int FocusedRow { get; private set; } = 1;
    public int VisibleCapacity { get; set; } = 7;
    public int RowCount => MapKind == SkirmishMapKind.Generated ? 18 : 4 + (string.IsNullOrEmpty(SelectedScenario.Description) ? 0 : 1);
    public int SeedRow => MapKind == SkirmishMapKind.Generated ? 1 : RowCount - 2;
    public int MapKindRow => MapKind == SkirmishMapKind.Generated ? 0 : RowCount - 1;
    public int VisibleStart => Math.Clamp(FocusedRow - Math.Clamp(VisibleCapacity, 1, 7) + 1, 0, Math.Max(0, RowCount - Math.Clamp(VisibleCapacity, 1, 7)));
    public void FocusRow(int row) => FocusedRow = Math.Clamp(row, 0, RowCount - 1);
    public void MoveFocus(int direction) => FocusRow((FocusedRow + Math.Sign(direction) + RowCount) % RowCount);
    public SkirmishMapKind MapKind { get; private set; }
    public SeededSkirmishModel Generated { get; } = new();
    public bool CanStartSelectedScenario => MapKind == SkirmishMapKind.Authored ? SelectedScenario.Available : Generated.Ready;
    public void SelectMapKind(SkirmishMapKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Generated.Busy) { MapKind = kind; FocusedRow = kind == SkirmishMapKind.Generated ? 1 : SeedRow; }
    }
    public static bool CanStart => true;

    public void SetSeed(ulong seed) => Configuration = Configuration with { Seed = seed };

    public void RefreshScenarios(IReadOnlyList<NewGameScenarioOption> scenarios)
    {
        string selected = SelectedScenario.Key;
        _scenarios = Copy(scenarios);
        _selected = Math.Max(0, Array.FindIndex(_scenarios, option => option.Key == selected));
        Configuration = Configuration with { MapName = SelectedScenario.DisplayName };
        if (MapKind == SkirmishMapKind.Authored) FocusedRow = SeedRow;
    }

    public void MoveScenario(int direction)
    {
        _selected = Math.Clamp(_selected + Math.Sign(direction), 0, _scenarios.Length - 1);
        Configuration = Configuration with { MapName = SelectedScenario.DisplayName };
        if (MapKind == SkirmishMapKind.Authored) FocusedRow = SeedRow;
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
