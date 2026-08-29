namespace TerraRuntime.HostContracts;

/// <summary>
/// Optional bridge used by an extensible host to attach trusted modules to a running TerraRuntime world.
/// The NativeAOT standalone host normally passes no lifecycle implementation.
/// </summary>
public interface ITerraRuntimeHostLifecycle
{
    ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default);

    ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default);
}
