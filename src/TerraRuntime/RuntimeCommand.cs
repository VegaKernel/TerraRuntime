namespace TerraRuntime;

internal abstract record RuntimeCommand;

internal sealed record ProbeCommand : RuntimeCommand;

internal sealed record WorkerResultCommand(int Value) : RuntimeCommand;
