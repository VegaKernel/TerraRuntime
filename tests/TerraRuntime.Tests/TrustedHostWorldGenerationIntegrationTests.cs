using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Extensibility;
using TerraRuntime.HostContracts;
using TerraRuntime.HostContracts.TerminalUI;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.HostModuleFixture;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TrustedHostWorldGenerationIntegrationTests
{
    [Fact]
    public async Task Loaded_host_generator_creates_reloadable_world_through_startup_source()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string root = Path.Combine(Path.GetTempPath(), $"terraruntime-host-worldgen-{Guid.NewGuid():N}");
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

        var loader = new TrustedHostModuleLoader(hostModules);
        var environment = new TestHostEnvironment(
            root,
            hostModules,
            serverPlugins,
            worlds,
            config,
            data,
            logs,
            loader.TerminalDashboards,
            loader.WorldGenerators);
        string worldPath = Path.Combine(worlds, "fixture-generated.wld");

        try
        {
            Assert.Equal(1, await loader.StartAllAsync(environment, cancellationToken));

            var source = new StartupWorldGeneratorSource(loader);
            Assert.Contains(
                StartupWorldGeneratorCatalog.Capture(source),
                static id => id.Value == FixtureHostModule.WorldGeneratorId);

            var request = new WorldGenerationRequest(
                new WorldGeneratorId(FixtureHostModule.WorldGeneratorId),
                "Fixture Generated",
                Seed: 987654321UL,
                WidthTiles: 128,
                HeightTiles: 96)
            {
                Options = new WorldGenerationOptions(
                    WorldGenerationGameMode.Journey,
                    WorldGenerationEvil.Crimson)
            };
            var pipeline = new RuntimeWorldCreationPersistencePipeline(source, maxTileCount: 32_000_000);
            long timestamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc).ToBinary();

            RuntimeWorldCreationPersistenceResult result = pipeline.TryCreateAndPersist(
                request,
                worldPath,
                Guid.Parse("98765432-1000-4000-8000-000000000001"),
                worldId: 987654321,
                creationTimeBinary: timestamp,
                lastPlayedBinary: timestamp,
                cancellationToken: cancellationToken);

            Assert.True(result.Succeeded, result.ToString());
            Assert.Equal(Path.GetFullPath(worldPath), result.WorldPath);
            Assert.True(File.Exists(worldPath));

            WorldFileLoadDiagnostic load = WorldFileLoader.TryLoad(
                File.ReadAllBytes(worldPath),
                TerrariaServerHost.CreateServerWorldLoadLimits(),
                out WorldFileData? world);
            Assert.True(load.IsLoaded, load.ToString());
            Assert.NotNull(world);
            Assert.Equal("Fixture Generated", world.Header.Name);
            Assert.Equal("987654321", world.Header.SeedText);
            Assert.Equal((short)64, world.RuntimeMetadata.SpawnX);
            Assert.Equal((byte)WorldGenerationGameMode.Journey, world.RuntimeMetadata.GameMode);
            Assert.True(world.RuntimeMetadata.Crimson);
        }
        finally
        {
            await loader.DisposeAsync();
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Collectible host module contexts can keep files alive briefly on Windows.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best-effort for the same collectible-context reason.
            }
        }
    }

    private sealed record TestHostEnvironment(
        string RootDirectory,
        string HostModulesDirectory,
        string ServerPluginsDirectory,
        string WorldsDirectory,
        string ConfigDirectory,
        string DataDirectory,
        string LogsDirectory,
        ITerraRuntimeTerminalDashboardRegistry TerminalDashboards,
        ITerraRuntimeWorldGeneratorRegistry WorldGenerators) : ITerraRuntimeHostEnvironment;
}
