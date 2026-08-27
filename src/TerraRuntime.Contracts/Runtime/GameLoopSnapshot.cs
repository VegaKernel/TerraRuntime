namespace TerraRuntime.Contracts.Runtime;

public readonly record struct GameLoopSnapshot(
    long Tick,
    int GameThreadId,
    int CommandsProcessed,
    int PendingCommands,
    long RejectedCommands,
    long MissedTickDeadlines,
    double LastTickMilliseconds,
    double WorstTickMilliseconds,
    double LastIngressMilliseconds,
    double WorstIngressMilliseconds,
    double LastCommandMilliseconds,
    double WorstCommandMilliseconds,
    double LastUpdateMilliseconds,
    double WorstUpdateMilliseconds,
    GameLoopPhase SlowestLastPhase,
    double SlowestLastPhaseMilliseconds,
    DateTimeOffset CapturedAtUtc);
