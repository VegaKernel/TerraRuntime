namespace TerraRuntime.HostContracts;

/// <summary>
/// A trusted application-layer module hosted by the CoreCLR TerraRuntime profile.
/// Host modules are intentionally more privileged than ordinary Vega plugins, but they still receive
/// explicit contracts instead of TerraRuntime implementation objects.
/// </summary>
public interface IModule
{
    string Name { get; }

    ValueTask StartAsync(
        IEnvironment environment,
        CancellationToken cancellationToken = default);

    ValueTask AttachRuntimeAsync(
        IRuntime runtime,
        CancellationToken cancellationToken = default);

    ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
