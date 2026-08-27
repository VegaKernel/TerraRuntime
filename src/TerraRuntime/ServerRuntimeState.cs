namespace TerraRuntime;

internal sealed class ServerRuntimeState
{
    private int lastWorkerResult;

    public long AppliedCommands { get; private set; }

    public long Updates { get; private set; }

    public int LastWorkerResult => Volatile.Read(ref lastWorkerResult);

    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AppliedCommands++;

        if (command is WorkerResultCommand result)
        {
            Volatile.Write(ref lastWorkerResult, result.Value);
        }
    }

    public void Tick()
    {
        Updates++;
    }
}

internal abstract record RuntimeCommand;

internal sealed record ProbeCommand : RuntimeCommand;

internal sealed record WorkerResultCommand(int Value) : RuntimeCommand;
