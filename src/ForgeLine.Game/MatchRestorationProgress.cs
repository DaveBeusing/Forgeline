namespace ForgeLine.Game;

public enum MatchRestorationPhase { Configuration, ScenarioAssembly, Replay, HashVerification }

public readonly record struct MatchRestorationProgress(
    MatchRestorationPhase Phase, ulong CompletedTicks = 0, ulong TotalTicks = 0);
