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

    public async ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (dataDirectory is null)
            throw new InvalidOperationException("The fixture host module has not been started.");

        string runtimeSummary = $"{runtime.Info.WorldName}|{runtime.Info.Port}|{runtime.InterestManagement.IsEnabled}";
        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.attached"),
            runtimeSummary,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (dataDirectory is null)
            return;

        await File.WriteAllTextAsync(
            Path.Combine(dataDirectory, "fixture-host-module.detached"),
            "detached",
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
