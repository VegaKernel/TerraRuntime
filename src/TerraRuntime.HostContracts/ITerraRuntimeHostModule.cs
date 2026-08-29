namespace TerraRuntime.HostContracts;

/// <summary>
/// A trusted application-layer module hosted by the CoreCLR TerraRuntime profile.
/// Host modules are intentionally more privileged than ordinary Vega plugins, but they still receive
/// explicit contracts instead of TerraRuntime implementation objects.
/// </summary>
public interface ITerraRuntimeHostModule
{
    string Name { get; }

    ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default);

    ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default);

    ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
