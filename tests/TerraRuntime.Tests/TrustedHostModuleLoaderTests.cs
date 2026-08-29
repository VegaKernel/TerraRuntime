using TerraRuntime.ExtensibleHost;
using TerraRuntime.HostContracts;
using TerraRuntime.HostModuleFixture;

namespace TerraRuntime.Tests;

public sealed class TrustedHostModuleLoaderTests
{
    [Fact]
    public async Task StartAllAsync_LoadsDropInModule_AndStopsIt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"terraruntime-host-{Guid.NewGuid():N}");
        string hostModules = Path.Combine(root, "HostModules");
        string serverPlugins = Path.Combine(root, "ServerPlugins");
        string worlds = Path.Combine(root, "Worlds");
        string config = Path.Combine(root, "config");
        string data = Path.Combine(root, "data");
        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(hostModules);
        Directory.CreateDirectory(serverPlugins);
        Directory.CreateDirectory(worlds);
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(logs);

        string modulePath = Path.Combine(hostModules, "Vega.dll");
        File.Copy(typeof(FixtureHostModule).Assembly.Location, modulePath);

        var environment = new TestHostEnvironment(
            root,
            hostModules,
            serverPlugins,
            worlds,
            config,
            data,
            logs);
        var loader = new TrustedHostModuleLoader(hostModules);

        try
        {
            int loaded = await loader.StartAllAsync(environment);

            Assert.Equal(1, loaded);
            string startedMarker = Path.Combine(data, "fixture-host-module.started");
            Assert.True(File.Exists(startedMarker));
            Assert.Equal(serverPlugins, await File.ReadAllTextAsync(startedMarker));
        }
        finally
        {
            await loader.DisposeAsync();
        }

        Assert.True(File.Exists(Path.Combine(data, "fixture-host-module.stopped")));

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A collectible AssemblyLoadContext becomes reclaimable asynchronously; cleanup is best-effort on Windows.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort cleanup rule as above.
        }
    }

    private sealed record TestHostEnvironment(
        string RootDirectory,
        string HostModulesDirectory,
        string ServerPluginsDirectory,
        string WorldsDirectory,
        string ConfigDirectory,
        string DataDirectory,
        string LogsDirectory) : ITerraRuntimeHostEnvironment;
}
