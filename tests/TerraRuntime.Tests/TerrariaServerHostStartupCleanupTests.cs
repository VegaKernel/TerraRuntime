namespace TerraRuntime.Tests;

public sealed class TerrariaServerHostStartupCleanupTests
{
    [Fact]
    public async Task Startup_reaps_abandoned_world_save_transaction_before_load()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"TerraRuntime-Host-Cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string worldPath = Path.Combine(directory, "world.wld");
        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".world.wld.{token}.tmp");
        string lease = temporary + ".lease";
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(worldPath, "not-a-valid-world", cancellationToken);
            await File.WriteAllTextAsync(temporary, "abandoned", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);

            int exitCode = await TerrariaServerHost.RunAsync(
                new ServerHostOptions(
                    worldPath,
                    ServerHostOptions.DefaultPort,
                    MaxPlayers: 1,
                    InterestManagementEnabled: false,
                    TerminalUiEnabled: false));

            Assert.Equal(26, exitCode);
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
