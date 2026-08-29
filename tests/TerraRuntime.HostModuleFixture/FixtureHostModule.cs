using TerraRuntime.HostContracts;

namespace TerraRuntime.HostModuleFixture;

public sealed class FixtureHostModule : ITerraRuntimeHostModule
{
    private string? dataDirectory;

    public string Name => "FixtureHostModule";

    public async ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        dataDirectory = environment.DataDirectory;
        Directory.CreateDirectory(dataDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.started"),
            environment.ServerPluginsDirectory,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.stopped"),
            "stopped",
            cancellationToken).ConfigureAwait(false);
    }
}
