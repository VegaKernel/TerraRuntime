namespace TerraRuntime.Application;

internal abstract record RuntimeCommand;

internal sealed record ProbeCommand : RuntimeCommand;

internal sealed record WorkerResultCommand(int Value) : RuntimeCommand;
