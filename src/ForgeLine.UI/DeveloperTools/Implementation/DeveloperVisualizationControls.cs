namespace ForgeLine.UI;

/// <summary>Bounded local visualization controls over their existing owners.</summary>
public sealed record DeveloperVisualizationSwitch(string Label, Func<bool> Read, Action Toggle, Func<bool>? Available = null);
public sealed record DeveloperVisualizationCommand(string Label, Func<string> Execute, Func<bool>? Available = null);
