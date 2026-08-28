namespace TerraRuntime.Operations;

internal enum RuntimeLifecycleState
{
    Stopped = 0,
    Running = 1,
    Faulted = 2
}

internal readonly record struct RuntimeDashboardSnapshot(
    RuntimeLifecycleState Lifecycle,
    string WorldName,
    int WorldWidthTiles,
    int WorldHeightTiles,
    int Port,
    int MaxPlayers,
    bool InterestManagementEnabled,
    long Tick,
    int TargetTicksPerSecond,
    double ObservedTicksPerSecond,
    double LastTickMilliseconds,
    double WorstTickMilliseconds,
    bool CpuTimeAvailable,
    double LastTickCpuMilliseconds,
    double WorstTickCpuMilliseconds,
    string SlowestPhase,
    double SlowestPhaseMilliseconds,
    long MissedTickDeadlines,
    int CommandsProcessed,
    int PendingCommands,
    int DeferredCommands,
    long RejectedCommands,
    long CommandBudgetExhaustions,
    double OldestPendingCommandAgeMilliseconds,
    long ManagedHeapBytes,
    long TotalAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ActiveConnections,
    long AcceptedConnections,
    long RejectedConnections,
    DateTimeOffset CapturedAtUtc);
