using TerraRuntime.Application.Diagnostics;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class WorldStartupPreparationTests
{
    [Fact]
    public async Task Missing_world_fails_during_preparation_before_process_session()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"TerraRuntime-Startup-Preparation-{Guid.NewGuid():N}");
        string worldPath = Path.Combine(directory, "missing.wld");
        Directory.CreateDirectory(directory);

        try
        {
            var logs = new RuntimeLogBuffer();
            await using var hostLog = new RuntimeHostLog(
                logs,
                TextWriter.Null,
                TextWriter.Null,
                new RuntimeHostLoggingOptions
                {
                    ConsoleEnabled = false,
                    JsonLinesEnabled = false
                });
            var options = new ServerHostOptions(
                worldPath,
                ServerHostOptions.DefaultPort,
                MaxPlayers: 1,
                InterestManagementEnabled: false,
                TerminalUiEnabled: false);

            WorldStartupPreparationResult result = await WorldStartupPreparation.PrepareAsync(options, hostLog);

            Assert.Equal(WorldStartupPreparationStatus.Failed, result.Status);
            Assert.Equal(24, result.ExitCode);
            Assert.Null(result.Startup);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Server_world_load_policy_keeps_hostile_world_sizes_bounded()
    {
        var limits = ServerWorldLoadPolicy.CreateLimits();

        Assert.Equal(32_000_000, limits.MaxTileCount);
        Assert.Equal(1_000_000, limits.MaxTotalChestItems);
        Assert.Equal(100_000, limits.MaxTileEntities);
        Assert.Equal(4 * 1024 * 1024, limits.RuntimeMetadata.MaxManifestBytes);
    }
}
