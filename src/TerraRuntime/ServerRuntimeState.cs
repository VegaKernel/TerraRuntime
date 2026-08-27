namespace TerraRuntime;

internal sealed class ServerRuntimeState
{
    public long AppliedCommands { get; private set; }

    public long Updates { get; private set; }

    public void Apply(RuntimeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        AppliedCommands++;
    }

    public void Tick()
    {
        Updates++;
    }
}

internal abstract record RuntimeCommand;

internal sealed record ProbeCommand : RuntimeCommand;
