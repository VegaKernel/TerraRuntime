namespace TerraRuntime.Core;

public readonly record struct WorkerCompletion<TResult>(
    WorkerCompletionStatus Status,
    TResult Result,
    Exception? Error)
{
    public bool IsSuccess => Status == WorkerCompletionStatus.Succeeded;

    public static WorkerCompletion<TResult> Succeeded(TResult result) =>
        new(WorkerCompletionStatus.Succeeded, result, null);

    public static WorkerCompletion<TResult> Failed(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new WorkerCompletion<TResult>(WorkerCompletionStatus.Failed, default!, error);
    }
}
