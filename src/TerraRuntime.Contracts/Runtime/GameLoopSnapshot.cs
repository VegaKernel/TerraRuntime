namespace TerraRuntime.Contracts.Runtime;

public readonly record struct GameLoopSnapshot(
    long Tick,
    int GameThreadId,
    int CommandsProcessed,
    int PendingCommands,
    long RejectedCommands,
    double LastTickMilliseconds,
    double WorstTickMilliseconds,
    DateTimeOffset CapturedAtUtc);
