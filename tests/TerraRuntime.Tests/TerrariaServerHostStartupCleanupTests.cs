namespace TerraRuntime.Tests;

public sealed class TerrariaServerHostStartupCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_host_startup_refuses_live_save_lease_before_missing_world_check(bool backup)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Host-Lease-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string world = Path.Combine(directory, "world.wld");
        string target = backup ? world + ".bak" : world;
        string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        string lease = temporary + ".lease";
        try
        {
            await File.WriteAllTextAsync(temporary, "in-flight candidate", TestContext.Current.CancellationToken);
            using (var ownedLease = new FileStream(lease, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                int exit = await TerrariaServerHost.RunAsync(new ServerHostOptions(world,
                    ServerHostOptions.DefaultPort, MaxPlayers: 1, TerminalUiEnabled: false));
                Assert.Equal(26, exit);
                Assert.False(File.Exists(world));
                Assert.Equal("in-flight candidate", await File.ReadAllTextAsync(temporary, TestContext.Current.CancellationToken));
                Assert.True(File.Exists(lease));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Shared_host_startup_refuses_quarantined_transaction_before_missing_world_check()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Host-Conflict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string world = Path.Combine(directory, "world.wld");
        string temporary = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, "quarantined", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(temporary + ".lease", "lease", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(temporary + ".recovery-conflict", "conflict", TestContext.Current.CancellationToken);
            int exit = await TerrariaServerHost.RunAsync(new ServerHostOptions(world,
                ServerHostOptions.DefaultPort, MaxPlayers: 1, TerminalUiEnabled: false));
            Assert.Equal(26, exit);
            Assert.False(File.Exists(world));
            Assert.Equal("quarantined", await File.ReadAllTextAsync(temporary, TestContext.Current.CancellationToken));
            Assert.True(File.Exists(temporary + ".recovery-conflict"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
