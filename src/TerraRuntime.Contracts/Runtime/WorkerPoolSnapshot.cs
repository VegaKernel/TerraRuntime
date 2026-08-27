namespace TerraRuntime.Contracts.Runtime;

public readonly record struct WorkerPoolSnapshot(
    int WorkerCount,
    int ActiveWorkers,
    int PendingWork,
    long AcceptedWork,
    long RejectedWork,
    long CompletedWork,
    long FailedWork);
