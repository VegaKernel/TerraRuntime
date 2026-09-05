namespace TerraRuntime.Application.Operations;

internal enum RuntimeLifecycleState
{
    Stopped = 0,
    Running = 1,
    Faulted = 2
}

internal enum ListenerLifecycleState
{
    Active = 0,
    Draining = 1,
    Closed = 2
}

internal readonly record struct ListenerChangeResult(bool Success, string Message)
{
    public static ListenerChangeResult Accepted(string message) => new(true, message);

    public static ListenerChangeResult Rejected(string message) => new(false, message);
}

internal readonly record struct ListenerManagerSnapshot(
    string BindAddress,
    int Port,
    ListenerLifecycleState State,
    long Generation,
    int DrainingListeners,
    long SuccessfulRebinds,
    DateTimeOffset CapturedAtUtc);

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
    DateTimeOffset CapturedAtUtc,
    long WorkingSetBytes = 0,
    double ProcessCpuPercent = 0d,
    double GcPauseTimePercentage = 0d,
    string BindAddress = "0.0.0.0",
    ListenerLifecycleState ListenerState = ListenerLifecycleState.Closed,
    long ListenerGeneration = 0,
    int DrainingListeners = 0,
    long ListenerRebinds = 0);