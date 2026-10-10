using ForgeLine.Platform;
using System.Text.RegularExpressions;

namespace ForgeLine.Input;

public enum GameplayAction : byte { Build, Process, Units, Logistics, Supply, Combat, Technology, NextItem, PrimarySetting, SecondarySetting, Decrease, Increase, Activate, CancelJob, FocusBase, Formation, Overlay, Minimap, Pause, CorePlacement, PowerPlacement, ExtractorPlacement, StoragePlacement, SmelterPlacement, RotatePlacement }
public enum GameplayBindingScope : byte { Global, Dock }
public readonly record struct GameplayBindingDefinition(GameplayAction Action, PlatformKey DefaultKey, string Label, GameplayBindingScope Scope);
public readonly record struct GameplayBindingOverride(GameplayAction Action, PlatformKey Key);
public sealed record GameplayBindings
{
    public IReadOnlyList<GameplayBindingOverride> Overrides { get; init; } = Array.Empty<GameplayBindingOverride>();
    public GameplayBindings With(GameplayAction action, PlatformKey key) => new()
    {
        Overrides = Overrides.Where(x => x.Action != action).Append(new(action, key)).ToArray()
    };
}

/// <summary>One immutable mapping and prompt catalog; dock keys intentionally replace camera keys while the dock owns focus.</summary>
public sealed class GameplayBindingRegistry
{
    public const PlatformKey HelpKey = PlatformKey.F1;
    public const PlatformKey HelpAlias = PlatformKey.F12;
    public const PlatformKey CancelKey = PlatformKey.Escape;
    public static IReadOnlyList<string> FixedBindings { get; } = Array.AsReadOnly(new[]
    {
        "F1 / F12 HELP", "ESC CANCEL / PAUSE", "LEFT SELECT / DOUBLE LEFT VISIBLE SAME TYPE",
        "SHIFT + LEFT EXTEND SELECTION", "RIGHT MOVE / TARGET", "CTRL + DIGIT ASSIGN / DIGIT RECALL / DOUBLE DIGIT FOCUS",
        "CTRL + SHIFT + DIGIT CLEAR", "SHIFT + CLICK REPEAT ACCEPTED BUILD", "SHIFT + F1 METRICS",
        "SHIFT + F12 GUIDE", "F2 WORLD DEBUG / SHIFT + F4-F9 DEBUG CATEGORIES", "R RESTART TERMINAL MATCH"
    });
    private static readonly GameplayBindingDefinition[] DefinitionsArray =
    [
        new(GameplayAction.Build, PlatformKey.B, "BUILD", GameplayBindingScope.Global),
        new(GameplayAction.Process, PlatformKey.P, "PROCESS", GameplayBindingScope.Global),
        new(GameplayAction.Units, PlatformKey.U, "UNITS", GameplayBindingScope.Global),
        new(GameplayAction.Logistics, PlatformKey.L, "LOGISTICS", GameplayBindingScope.Global),
        new(GameplayAction.Supply, PlatformKey.Y, "SUPPLY", GameplayBindingScope.Global),
        new(GameplayAction.Combat, PlatformKey.K, "COMBAT", GameplayBindingScope.Global),
        new(GameplayAction.Technology, PlatformKey.H, "TECHNOLOGY", GameplayBindingScope.Global),
        new(GameplayAction.NextItem, PlatformKey.Tab, "NEXT ITEM", GameplayBindingScope.Dock),
        new(GameplayAction.PrimarySetting, PlatformKey.T, "PRIMARY SETTING", GameplayBindingScope.Dock),
        new(GameplayAction.SecondarySetting, PlatformKey.M, "SECONDARY SETTING", GameplayBindingScope.Dock),
        new(GameplayAction.Decrease, PlatformKey.Left, "DECREASE", GameplayBindingScope.Dock),
        new(GameplayAction.Increase, PlatformKey.Right, "INCREASE", GameplayBindingScope.Dock),
        new(GameplayAction.Activate, PlatformKey.Enter, "ACTIVATE", GameplayBindingScope.Dock),
        new(GameplayAction.CancelJob, PlatformKey.C, "CANCEL JOB", GameplayBindingScope.Dock),
        new(GameplayAction.FocusBase, PlatformKey.Home, "BASE FOCUS", GameplayBindingScope.Global),
        new(GameplayAction.Formation, PlatformKey.F3, "FORMATION", GameplayBindingScope.Global),
        new(GameplayAction.Overlay, PlatformKey.F10, "STRATEGIC LAYER", GameplayBindingScope.Global),
        new(GameplayAction.Minimap, PlatformKey.F11, "MINIMAP", GameplayBindingScope.Global),
        new(GameplayAction.Pause, PlatformKey.Space, "PAUSE", GameplayBindingScope.Global),
        new(GameplayAction.CorePlacement, PlatformKey.F4, "PLACE CORE", GameplayBindingScope.Global),
        new(GameplayAction.PowerPlacement, PlatformKey.F5, "PLACE POWER", GameplayBindingScope.Global),
        new(GameplayAction.ExtractorPlacement, PlatformKey.F6, "PLACE EXTRACTOR", GameplayBindingScope.Global),
        new(GameplayAction.StoragePlacement, PlatformKey.F7, "PLACE STORAGE", GameplayBindingScope.Global),
        new(GameplayAction.SmelterPlacement, PlatformKey.F8, "PLACE SMELTER", GameplayBindingScope.Global),
        new(GameplayAction.RotatePlacement, PlatformKey.F9, "ROTATE BUILDING", GameplayBindingScope.Global)
    ];
    public static IReadOnlyList<GameplayBindingDefinition> Definitions { get; } = Array.AsReadOnly(DefinitionsArray);
    public static GameplayBindingRegistry Default { get; } = new(new());
    private readonly PlatformKey[] _keys = new PlatformKey[DefinitionsArray.Length];
    private readonly string[] _prompts = new string[DefinitionsArray.Length];
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
    public string Text(string englishTemplate) => _text.TryGetValue(englishTemplate, out var text) ? text : englishTemplate;
    public string DockModesPrompt { get; }
    public string PanelsHelpPrompt { get; }
    public string TacticalPrompt { get; }
    public string DockNavigationPrompt { get; }
    public GameplayBindingRegistry(GameplayBindings bindings, RtsCameraBindings? camera = null)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(bindings.Overrides);
        for (int i = 0; i < _keys.Length; i++) _keys[i] = DefinitionsArray[i].DefaultKey;
        var seen = new HashSet<GameplayAction>();
        foreach (var item in bindings.Overrides)
        {
            if (!Enum.IsDefined(item.Action) || !seen.Add(item.Action)) throw new InvalidDataException("Unknown or duplicate gameplay action.");
            if (!IsAssignable(item.Key)) throw new InvalidDataException("Unknown, modifier, group, help or developer key is reserved.");
            if (DefinitionsArray[(int)item.Action].Scope == GameplayBindingScope.Global && UsesCamera(camera, item.Key))
                throw new InvalidDataException("Gameplay binding conflicts with a saved camera key; change the gameplay key.");
            _keys[(int)item.Action] = item.Key;
        }
        for (int i = 0; i < _keys.Length; i++)
        {
            for (int j = 0; j < i; j++)
                if (_keys[i] == _keys[j]) throw new InvalidDataException($"{DefinitionsArray[i].Label} conflicts with {DefinitionsArray[j].Label}.");
            // Legacy camera mappings win over conflicting default global shortcuts without rewriting the saved camera.
            bool blocked = DefinitionsArray[i].Scope == GameplayBindingScope.Global && UsesCamera(camera, _keys[i]);
            _prompts[i] = blocked ? "CAMERA KEY" : KeyLabel(_keys[i]);
            if (blocked) _keys[i] = PlatformKey.Unknown;
        }
        var substitutions = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < DefinitionsArray.Length; i++)
            if (DefinitionsArray[i].Action is not (GameplayAction.Decrease or GameplayAction.Increase or GameplayAction.PrimarySetting or GameplayAction.SecondarySetting or GameplayAction.CancelJob))
                substitutions.Add(KeyLabel(DefinitionsArray[i].DefaultKey), _prompts[i]);
        string pattern = @"\b(?:" + string.Join("|", substitutions.Keys.Select(Regex.Escape)) + @")\b";
        foreach (string template in GameplayPromptCatalog.English)
            _text.Add(template, Regex.Replace(template, pattern, match => substitutions[match.Value], RegexOptions.CultureInvariant));
        DockModesPrompt = string.Join(" ", _prompts.Take(7));
        PanelsHelpPrompt = string.Join("  ", DefinitionsArray.Take(7).Select(x => Prompt(x.Action) + " " + x.Label));
        TacticalPrompt = $"{Prompt(GameplayAction.Combat)} / {Prompt(GameplayAction.NextItem)} / {Prompt(GameplayAction.Activate)}";
        DockNavigationPrompt = $"{Prompt(GameplayAction.NextItem)} NEXT  {Prompt(GameplayAction.Activate)} ACT  {Prompt(GameplayAction.CancelJob)} CANCEL";
    }
    public PlatformKey Key(GameplayAction action) => _keys[(int)action];
    public string Prompt(GameplayAction action) => _prompts[(int)action];
    public static bool IsAssignable(PlatformKey key) => Enum.IsDefined(key) && key != PlatformKey.Unknown &&
        key is not (PlatformKey.LeftShift or PlatformKey.RightShift or PlatformKey.LeftControl or PlatformKey.RightControl or
        PlatformKey.F1 or PlatformKey.F2 or PlatformKey.F12 or PlatformKey.Escape) && key is not (>= PlatformKey.D0 and <= PlatformKey.D9);
    public static string KeyLabel(PlatformKey key) => key switch
    {
        PlatformKey.Unknown => "UNBOUND", PlatformKey.LeftControl or PlatformKey.RightControl => "CTRL",
        PlatformKey.LeftShift or PlatformKey.RightShift => "SHIFT", _ => key.ToString().ToUpperInvariant()
    };
    public static bool UsesCamera(RtsCameraBindings? b, PlatformKey key) => b is not null &&
        (b.PanForward == key || b.PanForwardAlternate == key || b.PanBackward == key || b.PanBackwardAlternate == key ||
        b.PanLeft == key || b.PanLeftAlternate == key || b.PanRight == key || b.PanRightAlternate == key ||
        b.RotateLeft == key || b.RotateRight == key || b.PitchUp == key || b.PitchDown == key);
}
