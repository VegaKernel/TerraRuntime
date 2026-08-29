namespace TerraRuntime.HostContracts;

/// <summary>
/// A trusted application-layer module hosted by the CoreCLR TerraRuntime profile.
/// Host modules are intentionally more privileged than ordinary Vega plugins, but they still receive
/// an explicit contract instead of TerraRuntime implementation objects.
/// </summary>
public interface ITerraRuntimeHostModule
{
    string Name { get; }

    ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
