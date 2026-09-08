using TerraRuntime.Application.Diagnostics;
using TerraRuntime.Application.Operations;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldStartupPreparationTests
{
    [Theory]
    [InlineData(8364953747496893878UL, 4200, 1200)]
    [InlineData(3679097605675226385UL, 4200, 1200)]
    [InlineData(8675309UL, 8400, 2400)]
    public async Task Generated_vanilla_world_reaches_actual_primary_startup_after_liquid_preparation(ulong seed, int width, int height)
    {
        // Regression seeds from two real Small startup failures and an independent NativeAOT Large repro.
        // A generator/finalizer or official load alone does not exercise TerraRuntime's post-load boundary.
        string directory = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Generated-Startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string worldPath = Path.Combine(directory, "generated.wld");
            var request = new WorldGenerationRequest(new("terraruntime:vanilla"), "Loading regression", seed, width, height)
                { SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            var pipeline = new RuntimeWorldCreationPersistencePipeline(BuiltInWorldGeneratorSource.Instance, ServerWorldLoadPolicy.CreateLimits().MaxTileCount);
            long timestamp = DateTime.UtcNow.ToBinary();
            var generated = pipeline.TryCreateAndPersist(request, worldPath, Guid.NewGuid(), 1458, timestamp, timestamp,
                TestContext.Current.CancellationToken);
            Assert.True(generated.Succeeded, generated.Creation?.Finalization?.Validation?.Detail ?? generated.Status.ToString());
            AssertCompleteJungleDetritus(generated.Creation!.Value.Candidate!.TileStore);
            var logs = new RuntimeLogBuffer();
            using var errors = new StringWriter();
            var options = new ServerHostOptions(worldPath, ServerHostOptions.DefaultPort, MaxPlayers: 1,
                InterestManagementEnabled: false, TerminalUiEnabled: false);
            WorldStartupPreparationResult result;
            await using (var hostLog = new RuntimeHostLog(logs, TextWriter.Null, errors,
                new RuntimeHostLoggingOptions { ConsoleEnabled = true, JsonLinesEnabled = false }))
                result = await WorldStartupPreparation.PrepareAsync(options, hostLog);
            Assert.True(result.Status == WorldStartupPreparationStatus.Ready, errors.ToString());
            Assert.NotNull(result.Startup);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void AssertCompleteJungleDetritus(WorldTileStore tiles)
    {
        for (int x = 0; x < tiles.Dimensions.WidthTiles; x++)
        for (int y = 0; y < tiles.Dimensions.HeightTiles; y++)
        {
            WorldTile tile = tiles.Get(x, y);
            if (!tile.IsActive || tile.Type != 233) continue;
            int left = x - tile.FrameX / 18 % 3, top = y - tile.FrameY / 18;
            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile cell = tiles.Get(left + dx, top + dy);
                Assert.True(cell.IsActive && cell.Type == 233, $"Incomplete detritus at {x},{y}");
                Assert.Equal(tile.FrameX / 54 * 54 + dx * 18, cell.FrameX);
                Assert.Equal(dy * 18, cell.FrameY);
            }
        }
    }

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
